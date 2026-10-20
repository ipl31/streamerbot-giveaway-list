// Shared by both inline-code phases. Only the adapters use runtime reflection;
// pagination, validation and retry policy use typed framework-only objects.
namespace GiveawayRuntime
{
    using System;
    using System.IO;
    using System.Text;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using System.Threading;

    public sealed class FetchFailure : Exception
    {
        public FetchFailure(string safeMessage) : base(safeMessage) { }
    }
    public sealed class TransportFailure : Exception
    {
        public TransportFailure() : base("Twitch request failed or timed out.") { }
    }
    public sealed class RequestSpec
    {
        public string Url;
        public Dictionary<string, string> Headers;
    }
    public sealed class HttpResult
    {
        public int Status;
        public string Body;
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
    public interface ITransport { HttpResult Send(RequestSpec request, int timeoutMilliseconds); }
    public interface IClock
    {
        long ElapsedMilliseconds { get; }
        DateTime UtcNow { get; }
        void Delay(int milliseconds);
    }
    public sealed class TickClock : IClock
    {
        private readonly uint start = unchecked((uint)Environment.TickCount);
        public long ElapsedMilliseconds { get { return unchecked((uint)Environment.TickCount - start); } }
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
        public void Delay(int milliseconds) { Thread.Sleep(milliseconds); }
    }
    // Public DTOs use Twitch's exact field names without custom JSON converters.
    public sealed class TokenInfo
    {
        public string client_id;
        public string user_id;
        public string[] scopes;
        public long? expires_in;
    }
    public sealed class Chatter { public string user_id; public string user_login; }
    public sealed class Pagination { public string cursor; }
    public sealed class ChattersPage { public Chatter[] data; public Pagination pagination; public long? total; }
    public sealed class FetchResult { public string[] Logins; public int Pages; }

    public static class JsonAdapter
    {
        public static T Read<T>(string json)
        {
            try
            {
                Type converter = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
                Type settingsType = Type.GetType("Newtonsoft.Json.JsonSerializerSettings, Newtonsoft.Json", true);
                object settings = Activator.CreateInstance(settingsType);
                settingsType.GetProperty("MaxDepth").SetValue(settings, (int?)16, null);
                PropertyInfo types = settingsType.GetProperty("TypeNameHandling");
                types.SetValue(settings, Enum.Parse(types.PropertyType, "None"), null);
                PropertyInfo metadata = settingsType.GetProperty("MetadataPropertyHandling");
                metadata.SetValue(settings, Enum.Parse(metadata.PropertyType, "Ignore"), null);
                MethodInfo read = converter.GetMethod("DeserializeObject", new Type[] { typeof(string), typeof(Type), settingsType });
                object result = read.Invoke(null, new object[] { json, typeof(T), settings });
                if (result == null) throw new FetchFailure("Twitch returned an empty JSON document.");
                return (T)result;
            }
            catch (FetchFailure) { throw; }
            catch (Exception)
            {
                // Never include parser errors: they can contain response bodies.
                throw new FetchFailure("Cannot read Twitch JSON; check response format and bundled Newtonsoft.Json availability.");
            }
        }
    }

    public sealed class ChattersClient
    {
        private const string RequiredScope = "moderator:read:chatters";
        private readonly ITransport transport;
        private readonly IClock clock;
        private readonly int maxPages;
        private readonly long started;
        public ChattersClient(ITransport transport, IClock clock, int maxPages = 10000)
        {
            this.transport = transport;
            this.clock = clock;
            this.maxPages = maxPages;
            started = clock.ElapsedMilliseconds;
        }
        public FetchResult Fetch(string broadcasterId, string token, string clientId)
        {
            if (!IsId(broadcasterId)) throw new FetchFailure("Broadcaster identity is missing or invalid.");
            token = (token ?? "").Trim();
            if (token.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)) token = token.Substring(6);
            if (!IsCredential(token) || !IsCredential(clientId))
                throw new FetchFailure("Broadcaster credentials are missing or invalid; reconnect the broadcaster account.");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers["Authorization"] = "Bearer " + token;
            headers["Client-Id"] = clientId;
            TokenInfo identity = JsonAdapter.Read<TokenInfo>(Get("https://id.twitch.tv/oauth2/validate", headers));
            if (identity.user_id != broadcasterId || identity.client_id != clientId)
                throw new FetchFailure("Connected token does not match the broadcaster/client; reconnect the broadcaster account.");
            if (!identity.expires_in.HasValue || identity.expires_in.Value <= 0)
                throw new FetchFailure("Broadcaster token is expired or malformed; reconnect the broadcaster account.");
            bool permitted = false;
            if (identity.scopes != null)
                foreach (string scope in identity.scopes) if (scope == RequiredScope) permitted = true;
            if (!permitted)
                throw new FetchFailure("Broadcaster connection lacks moderator:read:chatters. Reauthorize it and verify this permission is granted.");

            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            var cursors = new Dictionary<string, bool>(StringComparer.Ordinal);
            string cursor = null;
            int pages = 0;
            do
            {
                if (pages >= maxPages) throw new FetchFailure("Chat roster page limit reached; no partial list will be exported.");
                string url = "https://api.twitch.tv/helix/chat/chatters?broadcaster_id=" + broadcasterId
                    + "&moderator_id=" + broadcasterId + "&first=1000";
                if (cursor != null) url += "&after=" + EncodeQuery(cursor);
                ChattersPage page = JsonAdapter.Read<ChattersPage>(Get(url, headers));
                if (page.data == null || page.pagination == null || !page.total.HasValue || page.total.Value < 0)
                    throw new FetchFailure("Twitch returned an incomplete chat roster response; no partial list will be exported.");
                foreach (Chatter user in page.data)
                {
                    if (user == null || !IsId(user.user_id) || !IsLogin(user.user_login))
                        throw new FetchFailure("Twitch returned a malformed chat user; no partial list will be exported.");
                    // First occurrence wins if an account changes login mid-pagination.
                    if (!ids.ContainsKey(user.user_id)) ids.Add(user.user_id, user.user_login.ToLowerInvariant());
                }
                pages++;
                cursor = page.pagination.cursor;
                if (!string.IsNullOrEmpty(cursor))
                {
                    if (cursor.Length > 4096 || string.IsNullOrWhiteSpace(cursor) || cursors.ContainsKey(cursor))
                        throw new FetchFailure("Invalid or repeated Twitch pagination cursor; no partial list will be exported.");
                    cursors.Add(cursor, true);
                }
                else cursor = null;
                Remaining();
            } while (cursor != null);

            var logins = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string login in ids.Values) logins[login] = true;
            var names = new List<string>(logins.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return new FetchResult { Logins = names.ToArray(), Pages = pages };
        }
        private string Get(string url, Dictionary<string, string> headers)
        {
            for (int attempt = 0; ; attempt++)
            {
                HttpResult response = null;
                try
                {
                    response = transport.Send(new RequestSpec { Url = url, Headers = headers }, Math.Min(15000, Remaining()));
                    if (response == null) throw new FetchFailure("Twitch transport returned no response.");
                }
                catch (TransportFailure)
                {
                    if (attempt >= 2) throw new FetchFailure("Twitch network request failed after bounded retries; no list was exported.");
                }
                Remaining();
                if (response != null)
                {
                    if (response.Status == 200)
                    {
                        if (string.IsNullOrWhiteSpace(response.Body)) throw new FetchFailure("Twitch returned an empty response.");
                        return response.Body;
                    }
                    if (response.Status == 401)
                        throw new FetchFailure("Twitch authentication failed (HTTP 401); reconnect the broadcaster account and verify its scopes.");
                    if (response.Status == 403)
                        throw new FetchFailure("Twitch denied chat roster access (HTTP 403); verify broadcaster identity and moderator:read:chatters.");
                    if ((response.Status != 429 && (response.Status < 500 || response.Status > 599)) || attempt >= 2)
                        throw new FetchFailure("Twitch request failed (HTTP " + response.Status + "); no partial list was exported.");
                }
                int wait = 250 * (attempt + 1);
                if (response != null) wait = Math.Max(wait, ServerWait(response.Headers, response.Status == 429));
                if (wait >= Remaining()) throw new FetchFailure("Twitch retry wait exceeds the operation deadline; try again later.");
                clock.Delay(wait);
            }
        }
        private int ServerWait(Dictionary<string, string> headers, bool rateLimited)
        {
            string value;
            long seconds;
            DateTime date;
            double delay = 0;
            if (headers != null && headers.TryGetValue("Retry-After", out value))
            {
                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds >= 0)
                    delay = Math.Max(delay, seconds * 1000.0);
                else if (DateTime.TryParseExact(value, "r", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date))
                    delay = Math.Max(delay, (date - clock.UtcNow).TotalMilliseconds);
                else throw new FetchFailure("Twitch returned an invalid retry delay; try again later.");
            }
            if (rateLimited && headers != null && headers.TryGetValue("Ratelimit-Reset", out value))
            {
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
                    throw new FetchFailure("Twitch returned an invalid rate-limit reset; try again later.");
                try { delay = Math.Max(delay, (new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds) - clock.UtcNow).TotalMilliseconds); }
                catch (ArgumentOutOfRangeException) { throw new FetchFailure("Twitch rate-limit reset is out of range."); }
            }
            if (delay >= int.MaxValue) throw new FetchFailure("Twitch retry wait exceeds the operation deadline; try again later.");
            return Math.Max(0, (int)Math.Ceiling(delay));
        }
        private int Remaining()
        {
            long left = 60000 - (clock.ElapsedMilliseconds - started);
            if (left <= 0) throw new FetchFailure("Twitch roster collection exceeded its 60-second deadline; no list was exported.");
            return (int)left;
        }
        public static bool IsId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32) return false;
            foreach (char c in value) if (c < '0' || c > '9') return false;
            return true;
        }
        public static bool IsLogin(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 25) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_') return false;
            return true;
        }
        private static bool IsCredential(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char c in value) if (c <= ' ' || c >= 127) return false;
            return true;
        }
        public static string EncodeQuery(string value)
        {
            var result = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(value))
            {
                if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') || b == '-' || b == '_' || b == '.' || b == '~')
                    result.Append((char)b);
                else result.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
    }

    // Keeping these runtime dependencies behind reflection lets the native inline
    // compiler use references:[] and its mscorlib fallback on every installation.
    public sealed class FrameworkTransport : ITransport
    {
        public HttpResult Send(RequestSpec spec, int timeoutMilliseconds)
        {
            object response = null;
            object request = null;
            var clock = new TickClock();
            try
            {
                // Framework's GAC requires the full assembly identity; a partial
                // "System" lookup works on .NET 8 but fails on Framework 4.8.1.
                Type factory = Type.GetType("System.Net.WebRequest, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
                request = factory.GetMethod("Create", new Type[] { typeof(string) }).Invoke(null, new object[] { spec.Url });
                Type requestType = request.GetType();
                requestType.GetProperty("Method").SetValue(request, "GET", null);
                requestType.GetProperty("Timeout").SetValue(request, timeoutMilliseconds, null);
                requestType.GetProperty("ReadWriteTimeout").SetValue(request, timeoutMilliseconds, null);
                requestType.GetProperty("AllowAutoRedirect").SetValue(request, false, null);
                object headers = requestType.GetProperty("Headers").GetValue(request, null);
                PropertyInfo item = headers.GetType().GetProperty("Item", new Type[] { typeof(string) });
                foreach (var pair in spec.Headers) item.SetValue(headers, pair.Value, new object[] { pair.Key });
                try { response = requestType.GetMethod("GetResponse", Type.EmptyTypes).Invoke(request, null); }
                catch (TargetInvocationException exception)
                {
                    Exception cause = exception.InnerException;
                    PropertyInfo errorResponse = cause == null ? null : cause.GetType().GetProperty("Response");
                    if (errorResponse == null) throw new TransportFailure();
                    response = errorResponse.GetValue(cause, null);
                    if (response == null) throw new TransportFailure();
                }
                Type responseType = response.GetType();
                int status = Convert.ToInt32(responseType.GetProperty("StatusCode").GetValue(response, null), CultureInfo.InvariantCulture);
                object responseHeaders = responseType.GetProperty("Headers").GetValue(response, null);
                PropertyInfo headerItem = responseHeaders.GetType().GetProperty("Item", new Type[] { typeof(string) });
                var result = new HttpResult { Status = status };
                foreach (string key in new string[] { "Retry-After", "Ratelimit-Reset" })
                {
                    string value = (string)headerItem.GetValue(responseHeaders, new object[] { key });
                    if (value != null) result.Headers[key] = value;
                }
                using (var body = new MemoryStream())
                using (var stream = (Stream)responseType.GetMethod("GetResponseStream", Type.EmptyTypes).Invoke(response, null))
                {
                    byte[] buffer = new byte[4096];
                    while (true)
                    {
                        int remaining = timeoutMilliseconds - (int)clock.ElapsedMilliseconds;
                        if (remaining <= 0) throw new TransportFailure();
                        if (stream.CanTimeout) stream.ReadTimeout = remaining;
                        int count = stream.Read(buffer, 0, buffer.Length);
                        if (count == 0) break;
                        if (body.Length + count > 2 * 1024 * 1024) throw new FetchFailure("Twitch response exceeded the safe response-size limit.");
                        body.Write(buffer, 0, count);
                    }
                    result.Body = new UTF8Encoding(false, true).GetString(body.ToArray());
                }
                return result;
            }
            catch (FetchFailure) { throw; }
            catch (Exception) { throw new TransportFailure(); }
            finally
            {
                if (response != null) ((IDisposable)response).Dispose();
                if (request != null)
                {
                    try { request.GetType().GetMethod("Abort", Type.EmptyTypes).Invoke(request, null); }
                    catch (Exception) { }
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using GiveawayRuntime;

public sealed class FakeClock : IClock
{
    public long Now;
    public readonly List<int> Delays = new List<int>();
    public long ElapsedMilliseconds { get { return Now; } }
    public DateTime UtcNow { get { return new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(Now); } }
    public void Delay(int milliseconds) { Delays.Add(milliseconds); Now += milliseconds; }
}
public sealed class QueuedTransport : ITransport
{
    public readonly Queue<Func<RequestSpec, int, HttpResult>> Responses = new Queue<Func<RequestSpec, int, HttpResult>>();
    public readonly List<RequestSpec> Requests = new List<RequestSpec>();
    public readonly List<int> Timeouts = new List<int>();
    public void Add(int status, string body, string retryAfter = null)
    {
        Responses.Enqueue((r, t) =>
        {
            var result = new HttpResult { Status = status, Body = body };
            if (retryAfter != null) result.Headers["Retry-After"] = retryAfter;
            return result;
        });
    }
    public HttpResult Send(RequestSpec request, int timeoutMilliseconds)
    {
        Requests.Add(request); Timeouts.Add(timeoutMilliseconds);
        if (Responses.Count == 0) throw new Exception("Unexpected HTTP request");
        return Responses.Dequeue()(request, timeoutMilliseconds);
    }
}
public sealed class TestInline : CPHInline
{
    public ChattersClient Client;
    public int FactoryCalls;
    protected override ChattersClient CreateChattersClient() { FactoryCalls++; return Client; }
}
public static class CollectorTests
{
    private static int checks;
    private const string Secret = "test-only-secret-token";
    private const string ValidToken = "{\"client_id\":\"test-client\",\"user_id\":\"123\",\"scopes\":[\"moderator:read:chatters\"],\"expires_in\":3600}";
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL collector: " + label);
        checks++;
    }
    private static string Page(string users, string cursor = null, long total = 1)
    {
        return "{\"data\":[" + users + "],\"pagination\":{" + (cursor == null ? "" : "\"cursor\":\"" + cursor + "\"") + "},\"total\":" + total + "}";
    }
    private static string User(int id, string login)
    { return "{\"user_id\":\"" + id + "\",\"user_login\":\"" + login + "\",\"user_name\":\"Ignored Display\"}"; }
    private static QueuedTransport Auth()
    { var transport = new QueuedTransport(); transport.Add(200, ValidToken); return transport; }
    private static string[] Fetch(QueuedTransport transport, FakeClock clock = null, int maxPages = 10000)
    { return new ChattersClient(transport, clock ?? new FakeClock(), maxPages).Fetch("123", Secret, "test-client").Logins; }
    private static void Fails(QueuedTransport transport, string expected, FakeClock clock = null, int maxPages = 10000)
    {
        try { Fetch(transport, clock, maxPages); throw new Exception("Expected failure: " + expected); }
        catch (FetchFailure exception)
        {
            Check(exception.Message.Contains(expected), "controlled failure category: " + expected);
            Check(!exception.ToString().Contains(Secret), "failure does not expose token");
        }
    }
    private static FakeCph Context(string directory)
    {
        var cph = new FakeCph();
        cph.Arguments["commandSource"] = "twitch"; cph.Arguments["userId"] = "123";
        cph.Arguments["broadcastUserId"] = "123";
        cph.Globals["giveawayListOutputPath"] = Path.Combine(directory, "names.txt");
        return cph;
    }
    private static void NoSecret(FakeCph cph)
    {
        foreach (string text in cph.Logs) Check(!text.Contains(Secret), "logs exclude credentials");
        foreach (object value in cph.Arguments.Values)
        {
            Check(!Convert.ToString(value).Contains(Secret), "arguments exclude credentials");
            var array = value as string[];
            if (array != null) foreach (string item in array) Check(!item.Contains(Secret), "roster excludes credentials");
        }
    }
    public static void Run()
    {
        var transport = Auth();
        var large = new StringBuilder();
        for (int i = 0; i < 1000; i++) { if (i > 0) large.Append(','); large.Append(User(1000 + i, "user_" + i)); }
        transport.Add(200, Page(large.ToString(), "a+/=é", 1));
        transport.Add(200, Page(User(9999, "silent_lurker") + "," + User(1000, "changed_login") + "," + User(9998, "USER_1"), null, 2000));
        string[] names = Fetch(transport);
        Check(names.Length == 1001, "all pages over 1000 users, ID and login dedupe");
        Check(Array.IndexOf(names, "silent_lurker") >= 0, "silent account included without activity metadata");
        Check(Array.IndexOf(names, "changed_login") < 0, "first login wins for repeated ID");
        Check(transport.Requests.Count == 3, "validation plus every page, ignoring changing total");
        Check(transport.Requests[2].Url.EndsWith("&after=a%2B%2F%3D%C3%A9"), "opaque cursor URL-encoded");
        Check(transport.Requests[1].Url.Contains("broadcaster_id=123&moderator_id=123&first=1000"), "correct IDs and maximum page size");
        Check(transport.Requests[1].Headers["Authorization"] == "Bearer " + Secret && transport.Requests[1].Headers["Client-Id"] == "test-client", "matching auth headers");
        foreach (int timeout in transport.Timeouts) Check(timeout > 0 && timeout <= 15000, "bounded request timeout");

        transport = Auth(); transport.Add(200, Page("", null, 0));
        Check(Fetch(transport).Length == 0, "successful empty roster");
        transport = new QueuedTransport(); transport.Add(401, "secret body " + Secret); Fails(transport, "401");
        Check(transport.Requests.Count == 1, "401 is not retried");
        transport = Auth(); transport.Add(403, "denied"); Fails(transport, "403");
        foreach (string token in new string[] {
            ValidToken.Replace("moderator:read:chatters", "chat:read"),
            ValidToken.Replace("\"123\"", "\"999\""),
            ValidToken.Replace("test-client", "other-client"),
            ValidToken.Replace("3600", "0") })
        {
            transport = new QueuedTransport(); transport.Add(200, token);
            try { Fetch(transport); throw new Exception("Expected invalid token metadata failure"); }
            catch (FetchFailure) { Check(transport.Requests.Count == 1, "invalid token metadata stops before roster"); }
        }
        transport = new QueuedTransport();
        try { new ChattersClient(transport, new FakeClock()).Fetch("123", "", "test-client"); throw new Exception("Expected empty credential failure"); }
        catch (FetchFailure) { Check(transport.Requests.Count == 0, "blank credentials make no HTTP requests"); }
        transport = new QueuedTransport();
        try { new ChattersClient(transport, new FakeClock()).Fetch("bad&id", Secret, "test-client"); throw new Exception("Expected invalid ID failure"); }
        catch (FetchFailure) { Check(transport.Requests.Count == 0, "invalid broadcaster ID makes no requests"); }

        foreach (string malformed in new string[] { "bad json " + Secret, "null", "{}", "{\"data\":null}", Page("{}"), Page(User(1, "bad-name")), Page(User(1, "bad\\nextra")) })
        {
            transport = Auth(); transport.Add(200, malformed);
            try { Fetch(transport); throw new Exception("Expected schema failure"); }
            catch (FetchFailure exception) { Check(!exception.ToString().Contains(Secret), "malformed JSON/schema failure is sanitized"); }
        }
        transport = Auth(); transport.Add(200, Page(User(1, "one"), "repeat")); transport.Add(200, Page(User(2, "two"), "repeat")); Fails(transport, "repeated");
        transport = Auth(); transport.Add(200, Page(User(1, "one"), "next")); Fails(transport, "page limit", null, 1);

        var clock = new FakeClock(); transport = Auth(); transport.Add(429, "rate limit", "1"); transport.Add(200, Page(User(1, "one")));
        Check(Fetch(transport, clock).Length == 1 && clock.Delays[0] >= 1000, "429 wait honored before retry");
        transport = Auth(); transport.Add(429, "rate limit", "120"); Fails(transport, "deadline");
        transport = Auth(); for (int i = 0; i < 3; i++) transport.Add(503, "unavailable"); Fails(transport, "503");
        Check(transport.Requests.Count == 4, "at most two retries per request");
        clock = new FakeClock(); transport = Auth();
        transport.Responses.Enqueue((r, timeout) => { clock.Now += 60000; return new HttpResult { Status = 200, Body = Page(User(1, "one")) }; });
        Fails(transport, "60-second", clock);
        clock = new FakeClock(); transport = Auth();
        transport.Responses.Enqueue((r, timeout) => { throw new TransportFailure(); }); transport.Add(200, Page(User(1, "one")));
        Check(Fetch(transport, clock).Length == 1 && clock.Delays.Count == 1, "transient transport failure retried");
        clock = new FakeClock(); transport = Auth(); transport.Add(503, "unavailable"); transport.Add(200, Page(User(1, "one")));
        Check(Fetch(transport, clock).Length == 1 && clock.Delays.Count == 1, "transient HTTP 503 recovers");
        transport = Auth(); transport.Responses.Enqueue((r, timeout) => null); Fails(transport, "no response");
        transport = Auth(); transport.Add(429, "limit", "nonsense"); Fails(transport, "invalid retry");
        clock = new FakeClock(); transport = Auth();
        transport.Responses.Enqueue((r, timeout) => {
            var result = new HttpResult { Status = 429, Body = "rate limit" };
            long epoch = (long)(clock.UtcNow.AddSeconds(2) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            result.Headers["Ratelimit-Reset"] = epoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return result;
        });
        transport.Add(200, Page(User(1, "one")));
        Check(Fetch(transport, clock).Length == 1 && clock.Delays[0] >= 2000, "429 rate-limit reset honored");

        string directory = Path.Combine(Path.GetTempPath(), "giveaway-fetch-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            // Distinct instances model independently compiled native code actions.
            var cph = Context(directory); cph.Arguments["userId"] = "456"; cph.Arguments["isModerator"] = true;
            transport = Auth(); transport.Add(200, Page(User(1, "silent_lurker")));
            var fetch = new TestInline { CPH = cph, Client = new ChattersClient(transport, new FakeClock()) };
            Check(fetch.FetchChatters(), "moderator fetch phase proceeds");
            Check(Directory.GetFiles(directory).Length == 0 && cph.Messages.Count == 0, "fetch phase never writes files or chat");
            Check(transport.Requests[1].Url.Contains("moderator_id=123"), "caller moderator ID is never used for broadcaster-token request");
            Check(new CPHInline { CPH = cph }.SaveChatters(), "separate save instance consumes roster");
            string[] files = Directory.GetFiles(directory, "names-*.txt");
            Check(files.Length == 1 && File.ReadAllText(files[0]).Trim() == "silent_lurker", "complete silent roster exported");
            Check(!(bool)cph.Arguments["giveawayListFetchSucceeded"] && ((string[])cph.Arguments["giveawayListFetchedLogins"]).Length == 0, "handoff consumed");
            NoSecret(cph);
            Check(new CPHInline { CPH = cph }.SaveChatters() && Directory.GetFiles(directory).Length == 1, "repeat save cannot reuse consumed roster");

            // Reuse old args after a success; page two failure must not leak page one or stale users.
            transport = Auth(); transport.Add(200, Page(User(2, "partial_user"), "next")); transport.Add(401, Secret);
            fetch = new TestInline { CPH = cph, Client = new ChattersClient(transport, new FakeClock()) };
            Check(fetch.FetchChatters(), "fetch failure continues only to generic status");
            Check(!(bool)cph.Arguments["giveawayListFetchSucceeded"] && ((string[])cph.Arguments["giveawayListFetchedLogins"]).Length == 0, "failed second page publishes no partial roster");
            new CPHInline { CPH = cph }.SaveChatters();
            Check(Directory.GetFiles(directory).Length == 1, "failed fetch creates no file and preserves history");
            string error = (string)cph.Arguments["giveawayListChatMessage"];
            Check(!error.Contains("partial_user") && !error.Contains("silent_lurker") && !error.Contains(directory), "failure chat has no partial/stale names or file path");
            NoSecret(cph);

            transport = new QueuedTransport(); transport.Responses.Enqueue((r, timeout) => { throw new Exception(Secret); });
            fetch = new TestInline { CPH = cph, Client = new ChattersClient(transport, new FakeClock()) };
            Check(fetch.FetchChatters() && !(bool)cph.Arguments["giveawayListFetchSucceeded"], "unexpected network exception fails closed");
            NoSecret(cph);

            foreach (string mode in new string[] { "viewer", "platform", "identity" })
            {
                cph = Context(directory);
                if (mode == "viewer") cph.Arguments["userId"] = "456";
                if (mode == "platform") cph.Arguments["commandSource"] = "youtube";
                if (mode == "identity") cph.Arguments.Remove("userId");
                transport = new QueuedTransport();
                fetch = new TestInline { CPH = cph, Client = new ChattersClient(transport, new FakeClock()) };
                Check(!fetch.FetchChatters() && transport.Requests.Count == 0 && cph.CredentialReads == 0 && fetch.FactoryCalls == 0, "unauthorized phase has no HTTP/credential access: " + mode);
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("Passed " + checks + " collector checks.");
    }
}

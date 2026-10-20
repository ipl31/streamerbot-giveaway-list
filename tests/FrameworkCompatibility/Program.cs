using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GiveawayRuntime;

public static class Program
{
    private static int checks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL framework: " + name); checks++; }
    private static HttpResult Request(string response, int timeout = 3000, int bodyDelay = 0)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Exception serverError = null;
        string received = null;
        var server = new Thread(() =>
        {
            try
            {
                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 3000; stream.WriteTimeout = 3000;
                    var request = new StringBuilder();
                    while (!request.ToString().EndsWith("\r\n\r\n"))
                    {
                        int b = stream.ReadByte(); if (b < 0) break; request.Append((char)b);
                        if (request.Length > 8192) throw new Exception("Oversized fixture request");
                    }
                    received = request.ToString();
                    int boundary = response.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
                    byte[] headers = Encoding.ASCII.GetBytes(response.Substring(0, boundary));
                    stream.Write(headers, 0, headers.Length); stream.Flush();
                    if (bodyDelay > 0) Thread.Sleep(bodyDelay);
                    byte[] body = Encoding.UTF8.GetBytes(response.Substring(boundary));
                    stream.Write(body, 0, body.Length);
                }
            }
            catch (IOException) { if (bodyDelay == 0) serverError = new Exception("Fixture IO failure"); }
            catch (Exception error) { serverError = error; }
            finally { listener.Stop(); }
        });
        server.IsBackground = true; server.Start();
        HttpResult result = null;
        Exception failure = null;
        try
        {
            result = new FrameworkTransport().Send(new RequestSpec {
                Url = "http://127.0.0.1:" + port + "/fixture",
                Headers = new System.Collections.Generic.Dictionary<string, string> {
                    { "Authorization", "Bearer synthetic-only-token" }, { "Client-Id", "synthetic-client" }
                }
            }, timeout);
        }
        catch (Exception error) { failure = error; }
        if (!server.Join(5000)) { listener.Stop(); throw new Exception("Fixture did not finish"); }
        if (serverError != null) throw serverError;
        Check(received != null && received.Contains("Authorization: Bearer synthetic-only-token"), "real adapter attaches authorization header");
        Check(received.Contains("Client-Id: synthetic-client"), "real adapter attaches matching client header");
        if (failure != null) throw failure;
        return result;
    }
    private static string Response(int status, string body, string extra = "")
    { return "HTTP/1.1 " + status + " Fixture\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n" + extra + "\r\n" + body; }
    public static void Main()
    {
        TokenInfo identity = JsonAdapter.Read<TokenInfo>("{\"client_id\":\"client\",\"user_id\":\"123\",\"scopes\":[\"moderator:read:chatters\"],\"expires_in\":99}");
        Check(identity.user_id == "123" && identity.scopes.Length == 1 && identity.expires_in == 99, "actual bundled JSON adapter parses DTO fields");
        HttpResult result = Request(Response(200, "{\"data\":[],\"pagination\":{},\"total\":0}"));
        Check(result.Status == 200 && JsonAdapter.Read<ChattersPage>(result.Body).data.Length == 0, "framework HTTP success and real JSON parse");
        result = Request(Response(503, "unavailable", "Retry-After: 2\r\n"));
        Check(result.Status == 503 && result.Headers["Retry-After"] == "2" && result.Body == "unavailable", "HTTP error response retained for retry policy");
        result = Request(Response(302, "", "Location: http://127.0.0.1:1/never-follow\r\n"));
        Check(result.Status == 302, "redirect not followed");
        try { Request(Response(200, "slow"), 200, 500); throw new Exception("Expected body timeout"); }
        catch (TransportFailure) { Check(true, "response-body read deadline enforced"); }
        Console.WriteLine("Passed " + checks + " .NET Framework adapter checks against official 1.0.4 DLLs.");
    }
}

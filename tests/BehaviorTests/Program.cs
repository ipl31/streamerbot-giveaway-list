using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Streamer.bot supplies CPH at runtime. The unchanged extension source runs
// against controlled arguments/files/chat here; this is not a native import test.
public partial class CPHInline { public FakeCph CPH; }

public class FakeCph
{
    private string token = "test-only-secret-token";
    private string clientId = "test-client";
    public int CredentialReads;
    public string TwitchOAuthToken { get { CredentialReads++; return token; } set { token = value; } }
    public string TwitchClientId { get { CredentialReads++; return clientId; } set { clientId = value; } }
    public readonly Dictionary<string, object> Arguments = new Dictionary<string, object>();
    public readonly Dictionary<string, object> Globals = new Dictionary<string, object>();
    public readonly List<string> Logs = new List<string>();
    public readonly List<string> Messages = new List<string>();
    public bool FailChat;
    public Action BeforeSend;
    public bool TryGetArg<T>(string name, out T value)
    {
        object found;
        if (Arguments.TryGetValue(name, out found) && found is T)
        { value = (T)found; return true; }
        value = default(T); return false;
    }
    public T GetGlobalVar<T>(string name, bool persisted)
    {
        object value;
        return Globals.TryGetValue(name, out value) ? (T)value : default(T);
    }
    public void LogWarn(string message) { Logs.Add(message); }
    public void LogInfo(string message) { Logs.Add(message); }
    public void SetArgument(string name, object value) { Arguments[name] = value; }
    public void SetGlobalVar(string name, object value, bool persisted) { Globals[name] = value; }
    public void SendMessage(string message, bool useBot, bool fallback)
    {
        if (BeforeSend != null) BeforeSend();
        if (FailChat) throw new IOException("Simulated chat failure");
        if (!useBot || !fallback) throw new Exception("Expected bot with broadcaster fallback");
        Messages.Add(message);
    }
}

public static class Program
{
    private static int checks;
    private const string LongMessage = "User list is too long for chat. Mods: please ask the streamer to check the text file.";
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        checks++;
    }
    private static FakeCph Setup(string path)
    {
        var cph = new FakeCph();
        cph.Arguments["commandSource"] = "twitch";
        cph.Arguments["userId"] = "123";
        cph.Arguments["broadcastUserId"] = "123";
        cph.Globals["giveawayListOutputPath"] = path;
        cph.Arguments["giveawayListFetchSucceeded"] = true;
        cph.Arguments["giveawayListFetchedLogins"] = new string[0];
        return cph;
    }
    private static void User(FakeCph cph, int index, string name)
    {
        // Output tests unit-test the save phase with a complete framework-array
        // handoff. CollectorTests exercise real parsing and HTTP pagination.
        string[] names = (string[])cph.Arguments["giveawayListFetchedLogins"];
        if (names.Length <= index) Array.Resize(ref names, index + 1);
        names[index] = name;
        cph.Arguments["giveawayListFetchedLogins"] = names;
    }
    private static bool Run(FakeCph cph)
    {
        var code = new CPHInline { CPH = cph };
        if (!code.Execute()) return false;
        // Model the native If/Else + Twitch Send Message leaves. Package tests
        // separately verify the actual exported branches, operands and text.
        bool fits, exported;
        string message;
        if (!cph.TryGetArg("giveawayListFitsChat", out fits)
            || !cph.TryGetArg("giveawayListChatMessage", out message)
            || !cph.TryGetArg("giveawayListExportSucceeded", out exported))
            throw new Exception("Missing prepared chat arguments");
        try { cph.SendMessage(fits ? message : LongMessage, true, true); }
        catch (IOException) { return false; }
        // Return export status to assertions; native message delivery is tested
        // only through this stub, not through a live Streamer.bot connection.
        return exported;
    }
    private static string[] Exports(string directory) { return Directory.GetFiles(directory, "names-*.txt"); }
    private static void BoundaryUsers(FakeCph cph, int messageLength)
    {
        int index = 0;
        while (messageLength > 25)
        {
            User(cph, index, "u" + index.ToString("D3") + new string('a', 20));
            index++;
            messageLength -= 25; // 24-character login plus one separator.
        }
        User(cph, index, "z" + new string('b', messageLength - 1));
    }
    private static string LatestExport(FakeCph cph)
    {
        string log = cph.Logs.Find(x => x.Contains("exported "));
        return log.Substring(log.IndexOf(" names to ", StringComparison.Ordinal) + " names to ".Length);
    }

    public static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "giveaway-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "names.txt");
            File.WriteAllText(path, "legacy export");
            var cph = Setup(path);
            User(cph, 0, " Zebra "); User(cph, 1, "alpha"); User(cph, 2, "ALPHA");
            User(cph, 3, " "); User(cph, 4, "");
            User(cph, 5, null); User(cph, 6, "bad\nextra");
            User(cph, 7, "BotAccount"); User(cph, 8, "ordinary_lurker");
            cph.Globals["giveawayListExcludedLogins"] = " BOTACCOUNT, another_bot ";
            cph.BeforeSend = () => Check(Exports(directory).Length == 1, "file is saved before chat");
            Check(Run(cph), "broadcaster export succeeds");
            string first = LatestExport(cph);
            Check(string.Join(",", File.ReadAllLines(first)) == "alpha,ordinary_lurker,zebra",
                "sorted unique Twitch logins including lurker; exclusions/invalid names filtered");
            Check(Regex.IsMatch(Path.GetFileName(first), @"^names-\d{8}T\d{9}Z-[a-f0-9]{32}\.txt$"),
                "filename contains UTC timestamp and unique ID");
            Check(File.ReadAllText(path) == "legacy export", "legacy base filename untouched");
            byte[] bytes = File.ReadAllBytes(first);
            Check(!(bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191), "no UTF-8 BOM");
            Check(cph.Messages.Count == 1 && cph.Messages[0] == "alpha ordinary_lurker zebra",
                "default chat contains only space-separated usernames");
            Check((string)cph.Globals["giveawayListChatDelimiter"] == "space", "missing delimiter global initialized to space");

            var saveOnly = Setup(Path.Combine(directory, "save-only.txt"));
            User(saveOnly, 0, "prepared_viewer");
            Check(new CPHInline { CPH = saveOnly }.Execute(), "file preparation succeeds separately");
            Check(saveOnly.Messages.Count == 0 && File.Exists(LatestExport(saveOnly)),
                "save step writes a file without sending any chat message");
            Check((bool)saveOnly.Arguments["giveawayListFitsChat"]
                && (string)saveOnly.Arguments["giveawayListChatMessage"] == "prepared_viewer",
                "save step exposes short message and fits flag for visible native chat step");

            cph = Setup(path); cph.Arguments["userId"] = "456";
            cph.Arguments["isModerator"] = true; User(cph, 0, "replacement");
            Check(Run(cph), "moderator can export");
            string second = LatestExport(cph);
            Check(first != second && Exports(directory).Length == 2, "rapid consecutive runs create distinct files");
            Check(File.ReadAllText(second).Trim() == "replacement", "new file has current list");
            Check(File.ReadAllText(first).Contains("ordinary_lurker"), "first history file remains intact");

            cph = Setup(path); cph.Arguments["userId"] = "456"; User(cph, 0, "unauthorized");
            Check(!Run(cph) && cph.Messages.Count == 0, "ordinary viewer cannot export or post");
            Check(Exports(directory).Length == 2, "unauthorized run creates no history");
            cph = Setup(path); cph.Arguments["commandSource"] = "youtube"; User(cph, 0, "wrong_source");
            Check(!Run(cph) && cph.Messages.Count == 0, "other platforms cannot export or post");
            cph = Setup(path); cph.Arguments.Remove("userId"); User(cph, 0, "missing_identity");
            Check(!Run(cph), "missing identity rejected");

            cph = Setup(path);
            Check(!Run(cph) && Exports(directory).Length == 2, "empty list creates no new file");
            Check(cph.Messages.Count == 1 && cph.Messages[0].StartsWith("No eligible users"), "empty result status posted");
            cph = Setup(path); User(cph, 0, "excluded"); cph.Globals["giveawayListExcludedLogins"] = "excluded";
            Check(!Run(cph), "fully excluded list fails");

            cph = Setup(Path.Combine(directory, "missing-directory", "names.txt")); User(cph, 0, "viewer");
            Check(!Run(cph), "write failure handled");
            Check(cph.Logs.Exists(x => x.Contains("export failed")), "write failure logged");
            Check(cph.Messages.Count == 1 && cph.Messages[0].Contains("could not be saved")
                && !cph.Messages[0].Contains(directory) && !cph.Messages[0].Contains("viewer"),
                "failed save posts generic error without names or path");
            cph = Setup("relative.txt"); User(cph, 0, "viewer");
            Check(!Run(cph), "relative destination rejected");

            foreach (string setting in new string[] { "space", "comma", " COMMA ", ",", "", "invalid", "\n" })
            {
                cph = Setup(Path.Combine(directory, "format.txt"));
                cph.Globals["giveawayListChatDelimiter"] = setting;
                User(cph, 0, "bob"); User(cph, 1, "alice");
                bool comma = setting.Trim().Equals("comma", StringComparison.OrdinalIgnoreCase) || setting == ",";
                Check(Run(cph), "delimiter setting exports: " + setting);
                Check(cph.Messages.Count == 1 && cph.Messages[0] == (comma ? "alice,bob" : "alice bob"),
                    "configured delimiter with no prefix, commands or trailing separator");
                Check(string.Join("|", File.ReadAllLines(LatestExport(cph))) == "alice|bob",
                    "history stays one username per line for either chat delimiter");
            }

            foreach (string setting in new string[] { "space", "comma" })
            {
                cph = Setup(path); cph.Globals["giveawayListChatDelimiter"] = setting; BoundaryUsers(cph, 500);
                Check(Run(cph), "500-character list succeeds: " + setting);
                Check(cph.Messages.Count == 1 && cph.Messages[0].Length == 500, "exactly 500 including separators is sent");
                Check((bool)cph.Arguments["giveawayListFitsChat"], "500 characters selects native true branch");
                Check(File.ReadAllLines(LatestExport(cph)).Length == 20, "boundary list saved one name per line");
                cph = Setup(path); cph.Globals["giveawayListChatDelimiter"] = setting; BoundaryUsers(cph, 501);
                Check(Run(cph), "501-character list still saves: " + setting);
                Check(cph.Messages.Count == 1 && cph.Messages[0] == LongMessage, "over-limit list posts only moderator fallback");
                Check(!(bool)cph.Arguments["giveawayListFitsChat"], "501 characters selects native else branch");
                Check(!cph.Messages[0].Contains("names-") && !cph.Messages[0].Contains(directory)
                    && !cph.Messages[0].Contains("u00"), "fallback exposes no filename, path or usernames");
                Check(File.ReadAllLines(LatestExport(cph)).Length == 21, "full over-limit list saved");
            }

            cph = Setup(path); User(cph, 0, "chat_failure"); cph.FailChat = true;
            Check(!Run(cph), "simulated native chat failure reported");
            Check(File.Exists(LatestExport(cph)), "saved history survives chat failure");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "temporary files cleaned up");
            Console.WriteLine("Passed " + checks + " behavior checks.");
            CollectorTests.Run();
        }
        finally { Directory.Delete(directory, true); }
    }
}

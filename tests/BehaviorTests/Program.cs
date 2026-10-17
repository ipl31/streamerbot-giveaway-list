using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

// Streamer.bot supplies CPH at runtime. This stub executes the unchanged source
// against controlled arguments and files; it does not simulate native imports.
public partial class CPHInline
{
    public FakeCph CPH;
}

public class FakeCph
{
    public readonly Dictionary<string, object> Arguments = new Dictionary<string, object>();
    public readonly Dictionary<string, object> Globals = new Dictionary<string, object>();
    public readonly List<string> Logs = new List<string>();
    public bool TryGetArg<T>(string name, out T value)
    {
        object found;
        if (Arguments.TryGetValue(name, out found) && found is T)
        { value = (T)found; return true; }
        value = default(T);
        return false;
    }
    public T GetGlobalVar<T>(string name, bool persisted)
    {
        object value;
        return Globals.TryGetValue(name, out value) ? (T)value : default(T);
    }
    public void LogWarn(string message) { Logs.Add(message); }
    public void LogInfo(string message) { Logs.Add(message); }
}

public static class Program
{
    private static int checks;
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
        return cph;
    }
    private static void User(FakeCph cph, int index, string name, string platform = "twitch")
    {
        cph.Arguments["randomUserName" + index] = name;
        if (platform != null) cph.Arguments["randomUserType" + index] = platform;
    }
    private static bool Run(FakeCph cph) { return new CPHInline { CPH = cph }.Execute(); }

    public static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "giveaway-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "names.txt");
            var cph = Setup(path);
            User(cph, 0, " Zebra "); User(cph, 1, "alpha"); User(cph, 2, "ALPHA");
            User(cph, 3, " "); User(cph, 4, "other", "youtube");
            User(cph, 5, "missing_platform", null); User(cph, 6, "bad\nextra");
            User(cph, 7, "BotAccount"); User(cph, 8, "ordinary_lurker");
            cph.Globals["giveawayListExcludedLogins"] = " BOTACCOUNT, another_bot ";
            Check(Run(cph), "broadcaster export succeeds");
            Check(string.Join(",", File.ReadAllLines(path)) == "alpha,ordinary_lurker,zebra",
                "sorted, unique Twitch logins including lurker; exclusions and invalid names filtered");
            byte[] bytes = File.ReadAllBytes(path);
            Check(!(bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191),
                "UTF-8 has no BOM");
            Check(cph.Logs.Exists(x => x.Contains("exported 3 names")), "success count logged");

            cph = Setup(path); cph.Arguments["userId"] = "456";
            cph.Arguments["isModerator"] = true; User(cph, 0, "replacement");
            Check(Run(cph), "moderator can export");
            Check(File.ReadAllText(path).Trim() == "replacement", "existing file replaced without appending");

            cph = Setup(path); cph.Arguments["userId"] = "456"; User(cph, 0, "unauthorized");
            Check(!Run(cph), "ordinary viewer rejected");
            Check(File.ReadAllText(path).Trim() == "replacement", "unauthorized run preserves file");

            cph = Setup(path); cph.Arguments["commandSource"] = "youtube";
            User(cph, 0, "wrong_source"); Check(!Run(cph), "other platforms rejected");
            cph = Setup(path); cph.Arguments.Remove("userId");
            User(cph, 0, "missing_identity"); Check(!Run(cph), "missing identity rejected");

            cph = Setup(path);
            Check(!Run(cph), "empty list fails");
            Check(File.ReadAllText(path).Trim() == "replacement", "empty list preserves previous file");
            Check(cph.Logs.Exists(x => x.Contains("do not use it")), "empty list warns of stale file");

            cph = Setup(path); User(cph, 0, "excluded");
            cph.Globals["giveawayListExcludedLogins"] = "excluded";
            Check(!Run(cph), "fully excluded list fails");

            cph = Setup(Path.Combine(directory, "missing-directory", "names.txt"));
            User(cph, 0, "viewer"); Check(!Run(cph), "write failure handled");
            Check(cph.Logs.Exists(x => x.Contains("export failed")), "write failure logged");
            cph = Setup("relative.txt"); User(cph, 0, "viewer");
            Check(!Run(cph), "relative destination rejected");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "temporary files cleaned up");
            Console.WriteLine("Passed " + checks + " behavior checks.");
        }
        finally { Directory.Delete(directory, true); }
    }
}

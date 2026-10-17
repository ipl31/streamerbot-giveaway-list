/*
STREAMER.BOT TWITCH GIVEAWAY LIST

Install the .sb file from this repository's Releases page through Streamer.bot's
Import dialog. Enable the imported !giveawaylist command. In Platforms > Twitch
> Settings > Present Viewers, enable Live Update and choose a 1-minute interval.
Wait for a refresh, then type !giveawaylist as the broadcaster or a moderator.

The file giveaway-names.txt appears on the Desktop of the account running
Streamer.bot. Copy its contents into your wheel: one login name per line.
The latest presence refresh includes lurkers connected to chat, not anonymous
video viewers. Connected bot accounts and the broadcaster are included unless
excluded in configuration. The collection sub-action is capped at 100000.

For manual installation: create the command and Command Triggered action,
add Twitch > User > Add Random Users (Count 100000, Present Only checked),
then Core > C# Code > Execute C# Code containing this entire file.

Optional persisted string globals (Global Variable Viewer):
giveawayListOutputPath: absolute output filename; blank uses the Desktop.
giveawayListExcludedLogins: comma-separated Twitch logins; blank includes all.
Parent directories must already exist. No chat message can choose a destination.
Check the Streamer.bot log for success/failure.
An empty result or failed write leaves the previous export untouched: do not
use that older file for a new giveaway. Each successful run replaces the file.

Package and behavior tests are automated. A real Streamer.bot import and live
Twitch presence check are still required; see docs/SMOKE-TEST.md.
*/

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

public partial class CPHInline
{
    private const int MaximumUsers = 100000;

    public bool Execute()
    {
        string source, userId, broadcasterId;
        bool isModerator;
        bool broadcaster = CPH.TryGetArg("userId", out userId)
            && CPH.TryGetArg("broadcastUserId", out broadcasterId)
            && !string.IsNullOrWhiteSpace(userId)
            && string.Equals(userId, broadcasterId, StringComparison.Ordinal);
        bool moderator = CPH.TryGetArg("isModerator", out isModerator) && isModerator;

        if (!CPH.TryGetArg("commandSource", out source)
            || !string.Equals(source, "twitch", StringComparison.OrdinalIgnoreCase)
            || !(broadcaster || moderator))
        {
            CPH.LogWarn("Giveaway List: only a Twitch broadcaster or moderator can export.");
            return false;
        }

        string configuredExclusions = CPH.GetGlobalVar<string>("giveawayListExcludedLogins", true) ?? "";
        var excluded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string login in configuredExclusions.Split(','))
            if (!string.IsNullOrWhiteSpace(login)) excluded[login.Trim()] = true;
        var uniqueNames = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < MaximumUsers; i++)
        {
            string name, platform;
            if (!CPH.TryGetArg("randomUserName" + i, out name))
                break;
            // Fail closed if platform information is missing.
            if (!CPH.TryGetArg("randomUserType" + i, out platform)
                || !string.Equals(platform, "twitch", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            name = name.Trim().ToLowerInvariant();
            if (IsLogin(name) && !excluded.ContainsKey(name))
                uniqueNames[name] = true;
        }

        var names = new List<string>(uniqueNames.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);

        if (names.Count == 0)
        {
            CPH.LogWarn("Giveaway List: no eligible Twitch users found. "
                + "Previous export left unchanged; do not use it for this giveaway.");
            return false;
        }

        string temporaryPath = null;
        try
        {
            string configuredPath = CPH.GetGlobalVar<string>("giveawayListOutputPath", true);
            string path;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop))
                    throw new IOException("Desktop could not be resolved; set giveawayListOutputPath.");
                path = Path.Combine(desktop, "giveaway-names.txt");
            }
            else path = configuredPath;
            if (!Path.IsPathRooted(path))
                throw new IOException("giveawayListOutputPath must be an absolute filename.");
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllLines(temporaryPath, names, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
            CPH.LogInfo("Giveaway List: exported " + names.Count + " names to " + path);
            return true;
        }
        catch (Exception exception)
        {
            CPH.LogWarn("Giveaway List: export failed; previous export left unchanged. "
                + "Do not use an older file for this giveaway. " + exception.Message);
            return false;
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (Exception exception)
                { CPH.LogWarn("Giveaway List: temporary file cleanup failed. " + exception.Message); }
            }
        }
    }

    private static bool IsLogin(string name)
    {
        // Reject embedded line breaks so one account cannot create extra wheel entries.
        foreach (char c in name)
            if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                return false;
        return name.Length > 0;
    }
}

/*
STREAMER.BOT TWITCH GIVEAWAY LIST

Install the .sb file from this repository's Releases page through Streamer.bot's
Import dialog. Enable the imported !giveawaylist command. In Platforms > Twitch
> Settings > Present Viewers, enable Live Update and choose a 1-minute interval.
Wait for a refresh, then type !giveawaylist as the broadcaster or a moderator.

Each run saves a new timestamped giveaway-names-<UTC timestamp>-<unique id>.txt
on the Desktop of the account running Streamer.bot, one login name per line.
The names are also sent to chat in one comma-separated message if the entire
message fits Twitch's 500-character limit. Otherwise chat tells moderators to
ask the streamer to check the text file; no filename or path is posted.
The latest presence refresh includes lurkers connected to chat, not anonymous
video viewers. Connected bot accounts and the broadcaster are included unless
excluded in configuration. The collection sub-action is capped at 100000.

The imported action has three labelled groups: Get all present Twitch users,
Save timestamped giveaway file, and Post users to chat. The collection group
is collapsed; its built-in collector requests all available present users.
For manual installation: create the command and Command Triggered action;
add Twitch > User > Add Random Users (Count 100000, Present Only checked);
then Execute C# Code containing this file, named Save timestamped giveaway file;
then an If/Else on %giveawayListFitsChat%, with a Twitch Send Message sub-action
in each branch (the prepared message when true, the generic notice when false).
The save step prepares a message; only the final post step sends it to Twitch.

Optional persisted string globals (Global Variable Viewer):
giveawayListOutputPath: absolute base filename; a timestamp/id is added each run.
Blank uses Desktop/giveaway-names as the base. Existing files are never replaced.
giveawayListExcludedLogins: comma-separated Twitch logins; blank includes all.
Parent directories must already exist. No chat message can choose a destination.
Check the Streamer.bot log for success/failure.
An empty result or failed write creates no new export and posts a short status
without names, filenames or paths. Historical files remain untouched.

Package and behavior tests are automated. A real Streamer.bot import and live
Twitch presence check are still required; see docs/SMOKE-TEST.md.
*/

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

public partial class CPHInline
{
    private const int MaximumUsers = 100000;
    private const int ChatCharacterLimit = 500;
    private const string ChatPrefix = "Giveaway users: ";
    private const string ChatMessageArgument = "giveawayListChatMessage";
    private const string ExportSucceededArgument = "giveawayListExportSucceeded";
    private const string FitsChatArgument = "giveawayListFitsChat";

    public bool Execute()
    {
        CPH.SetArgument(ChatMessageArgument, "");
        CPH.SetArgument(ExportSucceededArgument, false);
        CPH.SetArgument(FitsChatArgument, false);
        if (!IsAuthorized())
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
            return PrepareChat("No eligible users found. Mods: please ask the streamer to check the chat presence settings.", false);
        }

        return SaveAndPrepareChat(names);
    }

    private bool IsAuthorized()
    {
        string source, userId, broadcasterId;
        bool isModerator;
        bool broadcaster = CPH.TryGetArg("userId", out userId)
            && CPH.TryGetArg("broadcastUserId", out broadcasterId)
            && !string.IsNullOrWhiteSpace(userId)
            && string.Equals(userId, broadcasterId, StringComparison.Ordinal);
        bool moderator = CPH.TryGetArg("isModerator", out isModerator) && isModerator;

        return CPH.TryGetArg("commandSource", out source)
            && string.Equals(source, "twitch", StringComparison.OrdinalIgnoreCase)
            && (broadcaster || moderator);
    }

    private bool SaveAndPrepareChat(List<string> names)
    {
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
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string uniqueId = Guid.NewGuid().ToString("N");
            path = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + "-" + stamp + "-" + uniqueId
                + Path.GetExtension(path));
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllLines(temporaryPath, names, new UTF8Encoding(false));
            // Move refuses to overwrite, even in the unlikely event of a collision.
            File.Move(temporaryPath, path);
            CPH.LogInfo("Giveaway List: exported " + names.Count + " names to " + path);
        }
        catch (Exception exception)
        {
            CPH.LogWarn("Giveaway List: export failed; previous export left unchanged. "
                + "Do not use an older file for this giveaway. " + exception.Message);
            return PrepareChat("User list could not be saved. Mods: please ask the streamer to check Streamer.bot's log.", false);
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

        string message = ChatPrefix + string.Join(", ", names);
        return PrepareChat(message, true);
    }

    private bool PrepareChat(string message, bool exportSucceeded)
    {
        CPH.SetArgument(ChatMessageArgument, message);
        CPH.SetArgument(ExportSucceededArgument, exportSucceeded);
        CPH.SetArgument(FitsChatArgument, message.Length <= ChatCharacterLimit);
        // Continue to the visible native If/Else and Twitch Send Message steps.
        // Error statuses are short enough for the message branch, without names.
        return true;
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

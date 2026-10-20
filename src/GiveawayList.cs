/*
STREAMER.BOT TWITCH GIVEAWAY LIST

Install the .sb file from this repository's Releases page through Streamer.bot's
Import dialog. Enable !giveawaylist and connect the Twitch broadcaster account.
The connection must grant moderator:read:chatters. No Live Update setting is
needed: each command fetches Twitch's current connected-chat roster directly.

Each run saves a new timestamped giveaway-names-<UTC timestamp>-<unique id>.txt
on the Desktop of the account running Streamer.bot, one login name per line.
The names are also sent to chat as plain usernames, space-separated by default
or comma-separated when configured, if the message fits Twitch's 500-character
limit. No heading, commands or extra text is added to the username list.
Otherwise chat tells moderators to
ask the streamer to check the text file; no filename or path is posted.
The roster includes silent chat-connected accounts, not anonymous video viewers.
Bots and the broadcaster are included unless excluded in configuration. Twitch's
membership updates may lag. Every page must succeed before any file is written.

The imported action has three labelled groups: Get all connected Twitch users,
Save timestamped giveaway file, and Post users to chat. The collection group
contains the direct fetch code. Both C# sub-actions embed this file and the shared
ChattersClient.cs helpers. The fetch variant prepends #define GIVEAWAY_FETCH;
the save variant does not. They exchange only framework string[]/bool arguments.
For manual installation, use the release import rather than a cached-user action;
then an If/Else on %giveawayListFitsChat%, with a Twitch Send Message sub-action
in each branch (the prepared message when true, the generic notice when false).
The save step prepares a message; only the final post step sends it to Twitch.

Optional persisted string globals (Global Variable Viewer):
giveawayListOutputPath: absolute base filename; a timestamp/id is added each run.
Blank uses Desktop/giveaway-names as the base. Existing files are never replaced.
giveawayListExcludedLogins: comma-separated Twitch logins; blank includes all.
giveawayListChatDelimiter: space (default) or comma. Only affects chat; files
always have one username per line. Changes take effect on the next command.
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
    private const int ChatCharacterLimit = 500;
    private const string ChatMessageArgument = "giveawayListChatMessage";
    private const string ExportSucceededArgument = "giveawayListExportSucceeded";
    private const string FitsChatArgument = "giveawayListFitsChat";
    private const string FetchSucceededArgument = "giveawayListFetchSucceeded";
    private const string FetchedLoginsArgument = "giveawayListFetchedLogins";
    private const string FetchErrorArgument = "giveawayListFetchError";
    private const string FetchErrorMessage = "Unable to retrieve the complete user list. Mods: please ask the streamer to check the Twitch connection and Streamer.bot log.";

    public bool Execute()
    {
#if GIVEAWAY_FETCH
        return FetchChatters();
#else
        return SaveChatters();
#endif
    }

    protected virtual GiveawayRuntime.ChattersClient CreateChattersClient()
    {
        return new GiveawayRuntime.ChattersClient(new GiveawayRuntime.FrameworkTransport(), new GiveawayRuntime.TickClock());
    }

    public bool FetchChatters()
    {
        CPH.SetArgument(ChatMessageArgument, "");
        CPH.SetArgument(ExportSucceededArgument, false);
        CPH.SetArgument(FitsChatArgument, false);
        CPH.SetArgument(FetchSucceededArgument, false);
        CPH.SetArgument(FetchedLoginsArgument, new string[0]);
        CPH.SetArgument(FetchErrorArgument, "");
        if (!IsAuthorized())
        {
            CPH.LogWarn("Giveaway List: only a Twitch broadcaster or moderator can export.");
            return false;
        }
        try
        {
            string broadcasterId;
            CPH.TryGetArg("broadcastUserId", out broadcasterId);
            // Credentials remain local. Never store them in action arguments.
            GiveawayRuntime.FetchResult result = CreateChattersClient().Fetch(broadcasterId, CPH.TwitchOAuthToken, CPH.TwitchClientId);
            CPH.SetArgument(FetchedLoginsArgument, result.Logins);
            CPH.SetArgument(FetchSucceededArgument, true);
            CPH.LogInfo("Giveaway List: fetched " + result.Logins.Length + " users across " + result.Pages + " Twitch roster pages.");
            return true;
        }
        catch (GiveawayRuntime.FetchFailure failure)
        {
            CPH.LogWarn("Giveaway List: complete Twitch roster fetch failed. " + failure.Message);
        }
        catch (Exception)
        {
            // Do not expose credentials via unrestricted exception messages.
            CPH.LogWarn("Giveaway List: unexpected Twitch collection failure; no partial list will be exported.");
        }
        CPH.SetArgument(FetchSucceededArgument, false);
        CPH.SetArgument(FetchedLoginsArgument, new string[0]);
        CPH.SetArgument(FetchErrorArgument, FetchErrorMessage);
        return PrepareChat(FetchErrorMessage, false);
    }

    public bool SaveChatters()
    {
        if (!IsAuthorized()) return false;
        bool complete;
        string[] fetched;
        if (!CPH.TryGetArg(FetchSucceededArgument, out complete) || !complete
            || !CPH.TryGetArg(FetchedLoginsArgument, out fetched) || fetched == null)
        {
            string error;
            if (!CPH.TryGetArg(FetchErrorArgument, out error) || string.IsNullOrWhiteSpace(error))
                error = FetchErrorMessage;
            return PrepareChat(error, false);
        }
        // Consume the handoff before file I/O so an accidental second save cannot
        // reuse a successful roster from an earlier invocation.
        CPH.SetArgument(FetchSucceededArgument, false);
        CPH.SetArgument(FetchedLoginsArgument, new string[0]);

        string configuredExclusions = CPH.GetGlobalVar<string>("giveawayListExcludedLogins", true) ?? "";
        var excluded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string login in configuredExclusions.Split(','))
            if (!string.IsNullOrWhiteSpace(login)) excluded[login.Trim()] = true;
        var uniqueNames = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string fetchedLogin in fetched)
        {
            if (string.IsNullOrWhiteSpace(fetchedLogin))
                continue;
            string name = fetchedLogin.Trim().ToLowerInvariant();
            if (IsLogin(name) && !excluded.ContainsKey(name))
                uniqueNames[name] = true;
        }

        var names = new List<string>(uniqueNames.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);

        if (names.Count == 0)
        {
            CPH.LogWarn("Giveaway List: no eligible Twitch users found. "
                + "Previous export left unchanged; do not use it for this giveaway.");
            return PrepareChat("No eligible users found in Twitch's connected-chat list after exclusions.", false);
        }

        return SaveAndPrepareChat(names);
    }

    private bool IsAuthorized()
    {
        string source, userId, broadcasterId;
        bool isModerator;
        if (!CPH.TryGetArg("userId", out userId) || !GiveawayRuntime.ChattersClient.IsId(userId)
            || !CPH.TryGetArg("broadcastUserId", out broadcasterId) || !GiveawayRuntime.ChattersClient.IsId(broadcasterId)) return false;
        bool broadcaster = string.Equals(userId, broadcasterId, StringComparison.Ordinal);
        bool moderator = CPH.TryGetArg("isModerator", out isModerator) && isModerator;

        return CPH.TryGetArg("commandSource", out source)
            && string.Equals(source, "twitch", StringComparison.OrdinalIgnoreCase)
            && (broadcaster || moderator);
    }

    private bool SaveAndPrepareChat(List<string> names)
    {
        string delimiter = GetChatDelimiter();
        string formattedList = string.Join(delimiter, names);
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

        return PrepareChat(formattedList, true);
    }

    private string GetChatDelimiter()
    {
        string configured = CPH.GetGlobalVar<string>("giveawayListChatDelimiter", true);
        if (configured == null)
        {
            configured = "space";
            CPH.SetGlobalVar("giveawayListChatDelimiter", configured, true);
        }
        configured = configured.Trim();
        if (configured == "," || string.Equals(configured, "comma", StringComparison.OrdinalIgnoreCase))
            return ",";
        if (configured.Length != 0 && !string.Equals(configured, "space", StringComparison.OrdinalIgnoreCase))
            CPH.LogWarn("Giveaway List: unrecognized chat delimiter; using space. Set giveawayListChatDelimiter to space or comma.");
        return " ";
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

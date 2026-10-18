Make collection and chat posting explicit in the Streamer.bot action list.

The action now has three clearly labelled groups:

1. **Get all present Twitch users** — collapsed by default. It wraps Streamer.bot's
   built-in collector, requesting all available present users up to 100,000.
   Expanding it still reveals the application's fixed "Add Random Users" label.
2. **Save timestamped giveaway file** — saves the full list and prepares chat text
   plus a flag indicating whether it fits Twitch's 500-character message limit.
3. **Post users to chat (if the list fits)** — a visible native If/Else with a
   Twitch Send Message sub-action in each branch:
   - **True:** post the full prepared user list.
   - **False:** post only "User list is too long for chat. Mods: please ask the
     streamer to check the text file."

The C# save step no longer sends messages itself. Timestamped file history,
moderator/broadcaster permissions, exclusions and the 1.0.4 minimum are retained.
No filename or path is posted in chat. Short generic error statuses use the
message branch when there is no list to post.

Download the `.sb` asset and re-import it over the existing action and command.
Check that the command is enabled, and expand the final group to see both native
Twitch message sub-actions. Keep Twitch Present Viewers Live Update enabled.

Automated checks cover nested import structure, enabled branches, linked parents,
500/501-character routing, no chat calls in the file-save code, history and error
statuses. Native Streamer.bot import/live Twitch chat testing remains outstanding.

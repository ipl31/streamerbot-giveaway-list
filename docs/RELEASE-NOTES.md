Fetch the Twitch chat roster directly on every command, including silent accounts
connected to chat. Streamer.bot Live Update and its presence cache are no longer
used. The confusing Add Random Users sub-action is removed.

- Reads every Get Chatters page, deduplicates user IDs/logins, and sorts names.
- Requires the connected broadcaster token with moderator:read:chatters. If access
  fails, reconnect/reauthorize the broadcaster and inspect the safe log diagnostic.
- No partial or stale export when authentication, a later page, or retries fail.
- Preserves timestamped file history (one username per line), exclusions, space/
  comma chat settings, and the visible native chat-post branches and length limit.
- Adds collector unit coverage, real 1.0.4 API compilation checks for both code
  variants including native namespace wrapping, and Windows .NET Framework 4.8.1
  HTTP/JSON adapter checks against the bundled JSON assembly.

Download the .sb asset, re-import over the existing action/command, and ensure the
command is enabled. Requires Streamer.bot 1.0.4 or newer. Live Update is optional.
Twitch membership updates may lag; anonymous video viewers cannot be listed.
A real Streamer.bot GUI import and live Twitch smoke test remain outstanding.

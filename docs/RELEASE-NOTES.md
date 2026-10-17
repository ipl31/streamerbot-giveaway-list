Twitch giveaway-list export for Streamer.bot: one present username per line,
ready to paste into a wheel. Broadcaster/moderator access, sorted unique names,
optional excluded logins and output path.

Download the `.sb` asset and import it into Streamer.bot. Enable the imported
command and Twitch Present Viewers **Live Update**, wait for a presence refresh,
then type `!giveawaylist`. Output defaults to Desktop/giveaway-names.txt.

Preview release: package validation and C# behavior tests run on Linux and
Windows. Native Streamer.bot import and live Twitch presence have not yet been
smoke-tested. Follow docs/SMOKE-TEST.md before a real giveaway.

The `.sb.sha256` asset records the package checksum. The source ZIP is not the
installable extension.

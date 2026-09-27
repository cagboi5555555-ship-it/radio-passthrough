# Changelog

## 1.1.0

**Install and share**
- One file to share: running it outside the install folder opens an installer. No admin rights, Start menu entry, optional desktop shortcut, entry in Settings → Apps.
- Running a newer file updates in place. The running copy is closed and the new one started.
- `--install [--desktop-shortcut]` installs silently for scripted roll-outs. `--uninstall --quiet` removes silently.
- Uninstall from Settings → Apps puts TeamSpeak back on your normal mic, removes the capture profile, and deletes the app, its settings and logs. VB-CABLE stays.

**One-click setup**
- VB-CABLE installs from the Setup tab. It's downloaded from VB-Audio, its signature is verified, and it installs silently with one Windows prompt.
- TeamSpeak setup works while TeamSpeak is open. It closes, gets switched, and starts again by itself.
- Finds TeamSpeak's settings for any install type (per-user, all users, or settings in the install folder).

**Windows sound devices**
- New default-device guard. If Windows or the VB-CABLE installer makes the cable your default speakers or mic, your own devices are put back straight away. This covers normal, communications and recording defaults, and is remembered across restarts.
- Hides VB-CABLE's unused extra device (for example "CABLE In 16ch") so nothing picks it by mistake.

**Reliability**
- Radio keys are read by polling instead of a global keyboard hook. Windows can't silently drop it, it never delays input, and antivirus is less likely to flag it.
- Audio reopens by itself after sleep, device changes and errors. The mixer can never stop the output stream.
- If the app ever crashes it restarts itself in the tray (up to 3 times in 10 minutes), so TeamSpeak keeps your mic.
- Logs in `%LOCALAPPDATA%\RadioPassthrough\logs` (14 days). **Copy diagnostics** puts a support report on the clipboard.
- Damaged settings are set aside and defaults used. Settings from 1.0 carry over.
- No native DLLs: SQLite comes from Windows itself, and the tray icon uses the Windows shell directly.

**Everyday use**
- *Send game audio over radio* switch on the Live tab and in the tray menu.
- Quitting from the tray asks first, because TeamSpeak can't hear you while the app is closed.
- Checks for a newer release on GitHub once a day when releases are reachable.

## 1.0.0

- First version. Mic plus Arma's own audio into VB-CABLE while an ACRE2 radio key is held. Doc 1:1 mix (plain sum) and a Custom mix. A Test tab with TeamSpeak Opus and an ACRE2 radio-effect preview. TeamSpeak capture profile setup.

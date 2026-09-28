# Changelog

## 1.3.0

- **Cleaner radio: mic background noise is no longer sent.** Hum and fan noise from your mic got boosted by ACRE's radio effect into extra hiss on top of ACRE's own static. A noise gate on your mic now turns it down (by 30 dB) while you're not talking. It follows your mic's own noise level, opens instantly when you speak, and never touches game audio. Setup → Microphone → *Hide background noise between words* (on by default).

## 1.2.2

**Safer by default**
- Pressing Enter on *Quit*, *Remove* or *Switch TeamSpeak back* now keeps things as they are; the risky choice needs a click.
- Updating keeps your *Start with Windows* choice and your desktop shortcut.

**Lighter on your PC and on anti-cheat**
- Finding Arma and TeamSpeak now reads Windows' process list only. Arma itself is looked at once per launch instead of every 2 seconds.
- The app never asks Windows for access to another program's memory; app names on the Test tab come from the exe file on disk.
- Less background work and memory churn while the app sits in the tray.

**Fixes**
- Test tab recordings replay with exactly the timing they were sent with, so *Doc 1:1* vs *Custom* comparisons are sample-accurate.
- A settings save that fails (for example, file locked by a backup tool) is retried instead of being able to stop the app.
- If TeamSpeak is minimized to its tray icon, TeamSpeak setup tells you straight away to quit it, instead of waiting 15 seconds.
- Security reports now go through GitHub's private *Report a vulnerability* form.

## 1.2.1

- Releases are now built by GitHub from the public source, never on a personal PC, and each one comes with a signed build record you can check. See *Verifying a download* in `SECURITY.md`.
- No changes to the app itself.

## 1.2.0

**Offline by design**
- Removed every network feature. There's no update check, and the app no longer downloads VB-CABLE. *Get VB-CABLE* opens VB-Audio's official page; once you've installed it, the app notices and finishes setup by itself (hides the unused cable device, keeps your default devices). A test and the release self-test fail if networking code is ever added back.
- Uninstall no longer starts a helper command to delete itself. It moves its own exe to Temp and deletes the install folder directly.
- Diagnostics remove your Windows user name before you paste them anywhere.

**Polish**
- A one-time tray tip when you close the window, so it's clear the app keeps running.
- Test tab explains when playback can't start (no default speakers).
- Quit always exits, even if something fails while shutting down.
- Capture callbacks are fully guarded, so a bad audio buffer can't take the app down.
- Device tests never put test tones into a live TeamSpeak mic.
- New `SECURITY.md` and a *What it does on your PC* section in the README.

## 1.1.0

**Install and share**
- One file to share: running it outside the install folder opens an installer. No admin rights, Start menu entry, optional desktop shortcut, entry in Settings → Apps.
- Running a newer file updates in place. The running copy is closed and the new one started.
- `--install [--desktop-shortcut]` installs without the installer window, for admins. `--uninstall --quiet` removes the same way.
- Uninstall from Settings → Apps puts TeamSpeak back on your normal mic, removes the capture profile, and deletes the app, its settings and logs. VB-CABLE stays.

**Setup**
- TeamSpeak setup works while TeamSpeak is open. It closes, gets switched, and starts again by itself.
- Finds TeamSpeak's settings for any install type (per-user, all users, or settings in the install folder).

**Windows sound devices**
- New default-device guard. If Windows or the VB-CABLE installer makes the cable your default speakers or mic, your own devices are put back straight away. This covers normal, communications and recording defaults, and is remembered across restarts.
- Hides VB-CABLE's unused extra device (for example "CABLE In 16ch") so nothing picks it by mistake.

**Reliability**
- Radio keys are read by polling instead of a global keyboard hook. Windows can't silently drop it and it never delays input.
- Audio reopens by itself after sleep, device changes and errors. The mixer can never stop the output stream.
- If the app ever crashes it restarts itself in the tray (up to 3 times in 10 minutes), so TeamSpeak keeps your mic.
- Logs in `%LOCALAPPDATA%\RadioPassthrough\logs` (14 days). **Copy diagnostics** puts a support report on the clipboard.
- Damaged settings are set aside and defaults used. Settings from 1.0 carry over.
- Smaller file: no WinForms and no bundled SQLite (it uses the copy built into Windows). The tray icon uses the Windows shell directly.

**Everyday use**
- *Send game audio over radio* switch on the Live tab and in the tray menu.
- Quitting from the tray asks first, because TeamSpeak can't hear you while the app is closed.

## 1.0.0

- First version. Mic plus Arma's own audio into VB-CABLE while an ACRE2 radio key is held. Doc 1:1 mix (plain sum) and a Custom mix. A Test tab with TeamSpeak Opus and an ACRE2 radio-effect preview. TeamSpeak capture profile setup.

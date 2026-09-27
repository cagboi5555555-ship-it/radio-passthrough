# Radio Passthrough

When you talk on an ACRE2 radio in Arma 3, your teammates hear the fight around you (gunfire, engines, explosions) behind your voice. It does what the unit's "Game over Radio" guide does, without Voicemeeter, MacroButtons or AutoHotkey, and it sets itself up.

```
Your mic ─────────────────────────────┐
Arma 3's own sound ── radio key held ─┴─► mix ─► VB-CABLE ─► TeamSpeak's mic
```

- Direct speech stays voice only. Game sound is added **only while you hold a radio key** in Arma (ACRE2 defaults: Caps Lock, and Shift, Ctrl or Alt + Caps Lock).
- Your headset keeps playing Arma and TeamSpeak exactly as before. Nothing gets rerouted.
- The default **Doc 1:1** mix is the guide's sound: voice plus game, nothing added. TeamSpeak keeps your usual noise removal and volume settings.

## Install (about two minutes)

1. Download **RadioPassthrough-Setup-x.y.z.exe** and run it.
   - If Windows says *"Windows protected your PC"*, click **More info → Run anyway**. The file isn't code-signed; that's all the warning means.
2. Click **Install**. No admin rights needed. Radio Passthrough opens on its checklist.
3. Click the buttons in the checklist, top to bottom:
   - **VB-CABLE → Install.** It's downloaded from VB-Audio, checked, and installed. Approve the one Windows prompt.
   - **TeamSpeak microphone → Set up.** If TeamSpeak is open it closes for a moment and comes back.
4. Done. When the top line says **Ready**, join your server and use your radio as normal.

The app starts with Windows and sits in the tray (the radio-wave icon). Closing the window keeps it running. It has to be running for TeamSpeak to hear you.

## Check how you sound

On the **Test** tab:

1. Play a video with gunfire in your browser and pick it under *Take it from*.
2. Press **Record 10 seconds**. Talk, then hold your radio key (or *Hold here to talk on the radio*) and keep talking.
3. Play it back as **What TeamSpeak gets**, or as **What a teammate hears**. The second one uses TeamSpeak's Opus codec plus ACRE2's own radio filter, noise and distortion.

Nothing on the Test tab changes your settings.

## Everyday controls

- **Live tab:** see when you're on the radio and your levels. Switch *Send game audio over radio* off to go voice-only.
- **Custom mix:** adjust game level, dip the game while you talk, even out your voice, or catch loud peaks. Compare against Doc 1:1 on the Test tab first.
- **Radio keys:** add or remove keys (mouse side buttons work) if you changed ACRE's keybinds.
- **Tray menu:** open the app, switch game audio on or off, or quit.

## If something's wrong

| What happens | Try this |
|---|---|
| Nobody hears me at all | Is Radio Passthrough running (tray icon)? Open it and check the top line. |
| Teammates hear me but no game sound | Hold the radio key **in Arma** (it only counts while Arma is the active window). The Live tab should say *On the radio*. |
| Setup says Arma runs as administrator | Click **Restart as admin**, or stop running Arma as admin. |
| My speakers or mic switched to "CABLE" | The app switches them back by itself (Setup → *Keep my speakers and mic as the defaults*). |
| Game too loud or quiet on the radio | Live → Mix → **Custom** → *Game level*. Test it on the Test tab. |
| Still stuck | Setup → **Copy diagnostics**, then paste it to whoever helps you. |

## Uninstall

Go to **Settings → Apps → Radio Passthrough → Uninstall**. TeamSpeak goes back to your normal mic and the app, its settings and logs are removed. VB-CABLE stays; remove it there too if you want.

## Safety and privacy

- Nothing is injected into Arma. The app never touches the game process. It hears the game's sound through Windows (per-app audio capture) and reads the state of your radio keys only.
- It changes TeamSpeak's capture profile (after backing up `settings.db`) and, if needed, puts your Windows default devices back.
- Network: it downloads VB-CABLE from vb-audio.com when you press Install, and checks GitHub once a day for a newer release. Nothing else is sent.

## For developers

Requires the .NET 10 SDK on Windows.

```
dotnet test
powershell -ExecutionPolicy Bypass -File .\build.ps1      # → dist\RadioPassthrough-Setup-<version>.exe
```

- `src/RadioPassthrough.Core`: audio engine, mixer, ACRE2 effect, radio keys, TeamSpeak settings, installer, device guard.
- `src/RadioPassthrough`: WPF app (Live / Test / Setup, tray, installer window).
- `tests`: unit tests plus silent device tests (tones only ever go into VB-CABLE).
- `RadioPassthrough.exe --snapshot <dir> [--theme light|dark]` renders every screen to PNG off-screen. `--portable` runs without installing.
- Scripted roll-out: `RadioPassthrough-Setup-x.y.z.exe --install [--desktop-shortcut]` installs or updates silently and starts the app in the tray. Uninstall silently with `"%LOCALAPPDATA%\Programs\Radio Passthrough\RadioPassthrough.exe" --uninstall --quiet`.
- `--selftest <file>` checks the parts that need real Windows (tray, SQLite, key polling, device guard, windows) without showing or changing anything. `build.ps1` runs it on the finished file.
- Pushing a `v*` tag builds and publishes a GitHub release (`.github/workflows/build.yml`).

GPL-3.0. See `LICENSE` and `THIRD-PARTY-NOTICES.md`.

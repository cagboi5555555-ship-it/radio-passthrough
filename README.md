# Radio Passthrough

Sends your Arma 3 game audio over ACRE2 radio together with your voice, so people on the radio hear the gunfire around you. It does what the unit's "Game over Radio" guide does, without Voicemeeter, MacroButtons or AutoHotkey.

## How it works

```
Mic ───────────────────────────────┐
Arma 3's own audio ── radio key ───┴─► mix ─► VB-CABLE ─► TeamSpeak mic ("Radio Passthrough" profile)
```

- Your mic always goes to TeamSpeak.
- Arma's audio is added **only while you hold a radio key** (ACRE2 defaults: Caps Lock, Shift/Ctrl/Alt + Caps Lock) and Arma is the active window. Direct speech stays voice only.
- Arma and TeamSpeak keep playing to your headset exactly as before. The app records Arma straight from the game, so nothing is rerouted.
- **Doc 1:1** (default) is the guide's sound: voice plus game summed, nothing added. TeamSpeak's own processing (noise removal, auto volume) is copied unchanged from your current profile.

## Setup

1. Install [VB-CABLE](https://vb-audio.com/Cable/): run `VBCABLE_Setup_x64.exe` as administrator → Install Driver, then restart.
2. Run `publish\RadioPassthrough.exe`. It opens on **Setup**.
3. Close TeamSpeak, then press **Set up** next to *TeamSpeak microphone*. This adds a "Radio Passthrough" capture profile and makes it the default. A backup of TeamSpeak's settings is saved next to the original. **Restore normal mic** undoes it.
4. Leave **Start with Windows** on. TeamSpeak's mic is the cable on every server, so the app needs to be running.

## Testing the sound

On the **Test** tab:

- Play a video in your browser, pick it under *Take it from*, press **Record 10 seconds**, talk, then hold your radio key (or the on-screen button) and keep talking.
- Play back **What TeamSpeak gets**, or **What a teammate hears**. The second option runs TeamSpeak's Opus codec at your channel quality and then ACRE2's own receive effect: the filters, noise, ring modulation and sample-hold distortion, ported from ACRE2's source.
- Switch between **Doc 1:1** and **Custom** on the same recording to compare.
- **Through TeamSpeak** records TeamSpeak's own *Begin Test* playback, so you hear its real noise removal and auto volume on the mix.

Nothing on the Test tab changes your settings.

## Known limits (same as the guide)

- While you hold a radio key, players standing next to you also hear the game audio in your voice. ACRE sends one voice stream for both.
- The mix follows your keys, not ACRE's internal state. Pressing a radio key with no radio still adds game audio.
- If Arma runs as administrator, the app must too. The Setup tab offers a restart as admin.

## Build

Requires the .NET 10 SDK.

```
dotnet test
dotnet publish src/RadioPassthrough -c Release -o publish
```

`RadioPassthrough.exe --snapshot <dir> [--theme light|dark]` renders each tab to PNG off-screen. It's for design review.

The ACRE2 receive effect in `src/RadioPassthrough.Core/Dsp/AcreRadioEffect.cs` follows IDI-Systems/acre2 (GPL-3.0).

# Security

## What to expect from Radio Passthrough

- **No network access.** The app contains no networking code. A unit test and the release self-test fail if any is added.
- **No admin rights, no services, no drivers.** It installs per-user and runs as you.
- **No code injection.** It never opens Arma's or TeamSpeak's process memory. Game audio comes from Windows' per-app audio capture.
- **Keys:** only the radio keys you bound (plus Shift/Ctrl/Alt) are read, and only while Arma is the active window. While you add a new key on the Live tab, it waits for the next key you press. Nothing is logged or stored.
- **Changes it makes:** TeamSpeak's capture profile (backed up first), Windows default audio devices (only to undo a switch to VB-CABLE), hiding VB-CABLE's unused endpoints, and a per-user autostart entry. All of these can be undone from the app, and uninstalling reverts TeamSpeak.

## Verifying a download

Each release lists the SHA-256 of `RadioPassthrough-Setup-x.y.z.exe`. In PowerShell:

```
Get-FileHash .\RadioPassthrough-Setup-x.y.z.exe -Algorithm SHA256
```

Or build it yourself from this repository with `build.ps1`.

## Reporting a problem

If you find a security issue, please don't open a public issue. Contact the maintainer privately through GitHub (the repository owner's profile), with steps to reproduce.

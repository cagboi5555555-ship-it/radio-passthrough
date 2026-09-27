# Security

## What to expect from Radio Passthrough

- **No network access.** The app contains no networking code. A unit test and the release self-test fail if any is added.
- **No admin rights, no services, no drivers.** It installs per-user and runs as you.
- **No code injection.** It never opens Arma's or TeamSpeak's process memory. Game audio comes from Windows' per-app audio capture.
- **Keys:** only the radio keys you bound (plus Shift/Ctrl/Alt) are read, and only while Arma is the active window. While you add a new key on the Live tab, it waits for the next key you press. Nothing is logged or stored.
- **Changes it makes:** TeamSpeak's capture profile (backed up first), Windows default audio devices (only to undo a switch to VB-CABLE), hiding VB-CABLE's unused endpoints, and a per-user autostart entry. All of these can be undone from the app, and uninstalling reverts TeamSpeak.

## Verifying a download

Since 1.2.1, every release is built by GitHub from the tagged source in this repository, never on anyone's PC. The build runs the tests and the self-test, then GitHub signs a **build record** (an [artifact attestation](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds)) for the exe. It proves the file came from this repository's code and hasn't been changed since.

**Quick check (no tools):**

1. In PowerShell, get your file's fingerprint:
   ```
   Get-FileHash .\RadioPassthrough-Setup-x.y.z.exe -Algorithm SHA256
   ```
2. Open the repository's [build records](https://github.com/cagboi5555555-ship-it/radio-passthrough/attestations), open the one for your version, and compare the SHA-256 digest. It must match exactly.

**Full check (GitHub CLI):**

```
gh attestation verify .\RadioPassthrough-Setup-x.y.z.exe -R cagboi5555555-ship-it/radio-passthrough
```

This checks the signature too, and shows the exact commit and workflow run that built the file.

Or build it yourself from this repository with `build.ps1`.

## Reporting a problem

If you find a security issue, please don't open a public issue. Contact the maintainer privately through GitHub (the repository owner's profile), with steps to reproduce.

# Privacy Screen

Launch `dist\PrivacyScreen.exe` to immediately cover all monitors with the same black screen and small centered **type unlock** caption as the Remote Desktop app.

Type **unlock** locally or through a remote desktop connection to restore the desktop and close the app. No Enter is needed; capitalization does not matter. Type the letters consecutively, with less than five seconds between letters. Other local and remote keyboard and mouse input is suppressed, and standard Windows cursors are hidden until privacy ends. Remote virtual key events and Unicode text input are both supported.

The executable is self-contained for Windows x64: copy just `PrivacyScreen.exe` to another PC; no .NET installation is needed. It requires Windows 10 version 2004 or newer, or Windows 11. A second launch exits silently if privacy is already running.

The app adapts to monitor changes and exits when Windows locks or logs off. A hidden helper process, launched from the same executable, restores the cursor scheme if the main process crashes. No networking, remote desktop setup, installation, or startup registration is involved.

The black screen and caption are included in desktop capture, so remote desktop viewers see the privacy screen until they type **unlock**. The Remote Desktop app's input tag does not grant access to the covered desktop. This is a visual privacy cover, not a password lock: secure desktops such as Ctrl+Alt+Del and UAC, other topmost windows, separate Windows sessions, and applications using raw input are outside its normal desktop coverage or input hooks.

## Build

With the .NET 10 SDK installed on Windows:

```powershell
./build.ps1
```

The only published file is `dist\PrivacyScreen.exe`. The privacy overlay, input policy, input hooks, and cursor restoration implementation were adapted from `remote-desktop\windows\RemoteDesktopWin`.

The executable includes a monitor-and-lock icon for Windows Explorer and shortcuts. To regenerate the icon, run `./tools/generate-icon.ps1`, then rebuild with `./build.ps1`.

## Verify

```powershell
dotnet run --project tests/PrivacyScreen.Tests
dotnet run --project tests/PrivacyScreen.Tests -- --native-smoke
dotnet run --project tests/PrivacyScreen.Tests -- --published-smoke dist/PrivacyScreen.exe
```

The first command checks local and remote Unicode unlock recognition without covering the desktop. The optional native check briefly covers the monitors, suppresses input, and hides the cursor. It verifies that capture shows the privacy screen, all tagged and untagged input is blocked, local and remote unlock work, and cursors are restored after normal or abrupt exits. The published check launches the actual single-file executable, verifies captured monitor coverage and duplicate-launch rejection, terminates it to check the bundled cursor recovery helper, then launches it again and types **unlock** using the same Unicode input format as the Remote Desktop app.

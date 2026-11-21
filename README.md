# Tray Password

A small, portable Windows system tray application for generating random passwords.

## Features

- Starts in the system tray. Click the icon to open the compact password window.
- Password lengths from 4 to 128 characters.
- Select letters (A–Z, a–z), numbers (0–9), and your own list of symbols.
- Every password includes at least one character from each selected category.
- Optional capital first character (A–Z), included in the chosen total length.
- Copy to the clipboard or generate another password.
- Preferences persist between launches; generated passwords are not saved to disk.
- No application network calls, telemetry, update checker, scripting, or external packages.

## Build

Requires Windows with the .NET Framework 4.x and its C# compiler. No NuGet restore or additional SDK is needed.

From PowerShell in the repository directory:

```powershell
.\build.ps1
```

This creates `TrayPassword.exe` beside the source. The included `TrayPassword.ico` is embedded in the executable and used for the window and tray. To customize it, replace that file and rebuild. Exit a running copy before replacing its executable.

## Use

Run `TrayPassword.exe`. Find its icon in the system tray or the overflow menu. Left-click to open the window and generate a fresh password; right-click for **Open** and **Exit**. Closing, minimizing, or pressing Escape hides the window. Launching again opens the existing instance.

Symbols must be visible ASCII punctuation, such as `!@#$%`, without spaces, letters, or digits. Duplicate symbols count once. The Letters category guarantees a letter, rather than both uppercase and lowercase letters.

**First character A–Z** works even when Letters is unchecked: the first character is uppercase, and the remaining positions use the selected categories. All selected categories are still guaranteed.

Preferences are saved immediately when changed to:

```text
%LOCALAPPDATA%\TrayPassword\settings.xml
```

The app does not automatically start with Windows. To opt in, put a shortcut in your Windows Startup folder.

For a visible launch or isolated settings:

```powershell
.\TrayPassword.exe --show
.\TrayPassword.exe --show --settings "C:\path\settings.xml"
```

## Clipboard and randomness

Copied passwords are cleared after 30 seconds if the clipboard has not changed. The app requests exclusion from Windows clipboard history and cloud synchronization. Those requests cannot prevent other software from reading or retaining clipboard contents.

Randomness comes from .NET's Windows cryptographic provider. Provider failures stop generation. Rejection sampling avoids character-selection bias and produces a uniform distribution over passwords satisfying the selected rules.

Displayed passwords are discarded when the window is hidden. Managed strings cannot be guaranteed to be wiped immediately from process memory. This project has not undergone an independent security audit.

## Tests

```powershell
.\build.ps1
.\run-tests.ps1
```

The test suite generates 16,800 passwords across every nonempty category combination, both capital-prefix settings, and lengths 4, 8, 20, and 128. It checks length, permitted characters, required categories, prefix behavior, validation errors, settings persistence, compatibility with older settings, and rejection of XML external entities.

The verified build passed 726,012 checks. The application was also manually accepted by its original user; these automated tests do not cover interactive tray or clipboard behavior.

## Files

| File | Purpose |
|---|---|
| `TrayPassword.cs` | Complete application source |
| `TrayPassword.ico` | Application and system tray icon |
| `build.ps1` | Compile the Windows application |
| `Tests.cs` | Generator and preferences tests |
| `run-tests.ps1` | Compile and run the tests |

Compiled executables and local settings are excluded from this source repository.

# NativeHub

A packaged, Windows-native WinUI 3 utility for instant file search, Windows clipboard history, quick notes, detailed hardware telemetry, weather, twelve-city clocks, and the fictional NativOS desktop.

## Requirements

- Windows 11 21H2 (build 22000) or newer, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Everything 1.4](https://www.voidtools.com/downloads/) running with IPC enabled for indexed search

## Build

Run `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1`. The script performs a locked restore, zero-warning Release build, focused tests, payload publish, and signed MSIX packaging with an ephemeral development certificate.

Preferred test path: open `build\package`, run `Install.ps1`, read its certificate warning, type `INSTALL`, then launch NativeHub from Start. Output also includes `NativeHub.msix`, public `NativeHub.cer`, checksums, and a loose `build\payload\NativeHub.exe`. Keep the full payload folder together; it includes the Windows App SDK runtime but requires the .NET 10 Desktop Runtime. Package-only features such as notifications, jump lists, and startup registration require the MSIX install.

Everything search needs the regular x64 Everything build, not Lite; start Everything with IPC enabled before opening Search. Clipboard history requires Windows clipboard history enabled (`Win+V`). Some hardware sensors remain unavailable without elevation by design.

NativOS starts only after its on-screen power button completes a fictional POST and disk-check sequence. Its powered-on desktop survives normal sidebar navigation until **Shut down** is selected from the NativOS Start menu. F11 toggles its borderless full-screen desktop. Minefield and Falling Blocks keep local best scores; BlockWorld persists its finite five-block world and offers Indev-inspired type, shape, size, and theme generation controls. The default world generates 90 logical chunks—10× the previous 9-chunk area. The desktop wallpaper is NASA Scientific Visualization Studio Blue Marble imagery, used under NASA's media guidelines with no endorsement implied.

## Privacy

Notes, settings, NativOS scores, and the BlockWorld snapshot stay in local app data (`%LOCALAPPDATA%\NativeHub` for the loose build). Clipboard contents are read from Windows history and never persisted by NativeHub. Weather queries go directly to Open-Meteo. No telemetry is collected.

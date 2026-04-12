# RST Game Translation

## Overview
WPF desktop app (.NET 9, C#) for real-time game screen OCR and translation. Captures a region of the screen, runs OCR (via Python socket server or Windows OCR), translates the detected text using various AI/translation APIs, and displays the result as an overlay, in a chatbox, or both.

## Build & Run

### Prerequisites
- .NET 9 SDK (target: `net9.0-windows10.0.19041.0`, runtime: `win-x64`)
- Visual Studio 2022 (17.x) with ".NET desktop development" workload, OR `dotnet` CLI
- Windows 10 19041+ required (uses Windows.Graphics.Capture APIs)

### Commands
```bash
# Restore + build (Debug)
dotnet build RST.sln

# Build Release
dotnet build RST.sln -c Release

# Publish self-contained single-file
dotnet publish RST.csproj -c Release

# Run (Debug binary outputs as rst_debug.exe, Release as rst.exe)
dotnet run
```

Output goes to `app/` (no framework/RID subdirectories thanks to csproj settings).

### Opening in Visual Studio
Open `RST.sln`. Build/run with F5 (Debug) or Ctrl+F5 (no debugger).

## Project Structure
```
RST.sln              # Solution file
RST.csproj            # Single project, .NET 9 WPF + WinForms
src/                  # All source code
  App.xaml(.cs)       # WPF Application entry point
  MainWindow.xaml(.cs)        # Main UI window
  Logic.cs                    # Core translation pipeline (OCR -> translate -> display)
  ConfigManager.cs            # Settings persistence (key-value config file)
  MonitorWindow.xaml(.cs)     # Transparent overlay window
  ChatBoxWindow.xaml(.cs)     # Translation history chat window
  OverlayOptionsWindow.xaml(.cs)  # Overlay settings
  ChatBoxOptionsWindow.xaml(.cs)  # Chatbox settings
  TextObject.cs               # Detected text region (position, original/translated text)
  DpiHelper.cs                # DPI scaling utilities
  LocalizationManager.cs      # UI string localization
  *TranslationService.cs      # Translation backends (Gemini, ChatGPT, Google, Ollama, etc.)
  OcrServerManager.cs         # Python OCR server communication
  OneOCRManager.cs            # OneOCR (local) integration
  WindowsOCRManager.cs        # Windows built-in OCR
  GraphicsCaptureService.cs   # Screen capture via Windows.Graphics.Capture
  KeyboardShortcuts.cs        # Global hotkey registration
  ThemeManager.cs             # Light/dark theme support
app/                  # Build output + runtime assets (OCR models, webserver, configs)
media/                # Icons and images
```

## Architecture Notes
- **Namespace**: `RSTGameTranslation` (despite csproj `RootNamespace` being `WPFScreenCapture` — all source files use `RSTGameTranslation`)
- **Singleton pattern**: Most windows and managers use `static Instance` property
- **Translation pipeline**: Screen capture -> OCR (socket/Windows/OneOCR) -> text grouping -> translation API -> display (overlay/chatbox)
- **Settings**: `ConfigManager` stores key-value pairs in a text config file. Add new settings by: (1) adding a const key, (2) setting default in `InitDefaults()`, (3) adding getter/setter methods
- **Overlay**: `MonitorWindow` is a transparent, click-through, always-on-top window with `AllowsTransparency="True"` (cannot be changed at runtime in WPF)
- **Multi-area**: App supports multiple capture areas, cycling through them via `currentAreaIndex`

## Coding Conventions
- C# with nullable enabled, implicit usings
- WPF XAML for UI, code-behind pattern (no MVVM framework)
- Settings UI: `CheckBox` + event handler pattern (Checked/Unchecked -> ConfigManager setter)
- New settings should default to `false`/disabled to preserve backward compatibility
- Console.WriteLine for debug logging
- UI labels can be hardcoded initially; localize via `LocalizationManager` later

## Key Dependencies
- Hardcodet.NotifyIcon.Wpf — system tray icon
- Newtonsoft.Json — JSON parsing
- NAudio — audio processing
- Whisper.net — local speech recognition
- Microsoft.Windows.CsWinRT — Windows Runtime interop

## Testing
No automated test suite. Testing is manual:
1. Build and run the app
2. Select a screen region or window
3. Start translation and verify overlay/chatbox output
4. Test settings toggles in the options windows

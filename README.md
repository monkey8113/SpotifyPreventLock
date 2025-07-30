# Spotify Prevent Lock 🎵🔒

A lightweight Windows utility that keeps your PC awake when Spotify is playing music.

## Features ✨
- 🚫 Prevents PC from sleeping during Spotify playback
- 🖥️ Stops screen from turning off automatically
- ⚡ Configurable check interval (100ms to 100 seconds)
- 📌 Smart startup management (auto-updates paths when moved)
- 🎨 Dynamic tray icon (green when active, red when idle)
- ⚡ Ultra-lightweight (typically < 1MB RAM, 0% CPU when idle)
- 🔒 No installation needed - runs from system tray

## Performance & Resource Usage 📊
The application is designed to be extremely lightweight:
- **Memory Usage**: Typically under 1MB RAM
- **CPU Usage**: 0% when idle, negligible spikes during checks
- **Disk Usage**: Minimal (only stores small settings file)

For optimal performance:
- **Recommended check interval**: 2000ms (2 seconds)
  - Provides responsive detection while minimizing resource usage
- Can be increased to 5000ms+ for even lower resource impact
- Even at 100ms checks, impact remains minimal on modern systems

## Smart Startup Management 🔄
The application automatically handles version and location changes:
- Automatically updates startup entry when:
  - You upgrade to a new version
  - You move the program to a new location
- Just launch the program once after moving/updating
- No manual configuration needed

## How It Works ⚙️
The program efficiently monitors Spotify's activity:
1. Checks Spotify process every configured interval
2. When music is detected:
   - Sets system execution state to prevent sleep
   - Tray icon turns green
3. When no music is detected:
   - Returns to minimal resource usage
   - Tray icon turns red

## Installation 💾
1. Download the latest release
2. Run `SpotifyPreventLock.exe`
3. The app will launch in system tray (near clock)

## Configuration ⚙️
Right-click the tray icon to:
- **Set Check Interval**: Balance responsiveness vs resource usage
- **Start with Windows**: Toggle automatic startup
- **Exit**: Close the application

## Technical Details 🔧
- **Settings**: Stores only check interval in `%APPDATA%\SpotifyPreventLock\settings.json`
- **Requirements**: 
  - .NET 8.0 Runtime
  - Spotify Desktop App (web player not supported)

## FAQ ❓
### Will this work with Spotify web player?
❌ No, only works with the **Spotify desktop app**.

### Can I still lock my PC manually?
✅ Yes! `Win + L` will always work.

### How often does it check Spotify?
🔧 Default: Every 5 minutes (fully adjustable in settings)

### Does this use my internet?
🌐 No internet connection needed - works 100% offline.

## Privacy Policy 🔒
This app:
- Never collects your data
- Only looks at Spotify's window title
- Doesn't connect to the internet
- Stores all data locally
- Doesn't modify Spotify in any way
- Might try to summon Pikachu

## For Developers 💻
```bash
git clone https://github.com/yourusername/SpotifyPreventLock.git
cd SpotifyPreventLock
dotnet build

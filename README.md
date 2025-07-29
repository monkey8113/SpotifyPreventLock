# Spotify Prevent Lock 🎵🔒

A lightweight Windows utility that keeps your PC awake when Spotify is playing music.

## Features ✨
- 🚫 Prevents PC from sleeping during Spotify playback
- 🖥️ Stops screen from turning off automatically
- ⏲️ Adjustable check frequency (1 second to 60 minutes)
- 📌 Optional startup with Windows
- 🎨 Tray icon changes color when active
- 🔒 No installation needed - runs from system tray

## How It Works ⚙️
The program quietly checks Spotify every few minutes:
1. Looks for song titles in Spotify's window
2. When music is playing:
   - Tells Windows "I'm still using the PC!" (keeps system awake)
   - Makes tiny invisible mouse movements (prevents lock screen)
3. Stops automatically when:
   - You pause or close Spotify
   - You manually lock your PC (`Win + L` still works)

## Download & Run 🚀
1. Get the latest `.exe` from [Releases](https://github.com/yourusername/SpotifyPreventLock/releases)
2. Double-click `SpotifyPreventLock.exe`
3. Find the icon in your system tray (near the clock)

## Configuration ⚙️
Right-click the tray icon to:
- **Set check interval** (1 second to 60 minutes)
- **Toggle "Start with Windows"** (launch automatically)
- **Exit** the application

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
- Doesn't modify Spotify in any way

## For Developers 💻
```bash
git clone https://github.com/yourusername/SpotifyPreventLock.git
cd SpotifyPreventLock
dotnet build

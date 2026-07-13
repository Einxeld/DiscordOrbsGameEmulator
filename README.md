# Game Emulator — WPF (.NET 8)

The application copies itself to the paths listed in [games.txt](https://raw.githubusercontent.com/Einxeld/DiscordOrbsGameEmulator/refs/heads/main/games.txt), emulating the presence of games to obtain Discord Orbs.

<img width="806" height="643" alt="1" src="https://github.com/user-attachments/assets/02b680e7-7311-4a1d-b440-f89e3d73f59a" />

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## How to Use

1. Extract the program to a separate folder.
2. Copy the **raw** link: `https://raw.githubusercontent.com/Einxeld/DiscordOrbsGameEmulator/refs/heads/main/games.txt`
3. Paste it into the URL field and click **"Load List"**.
3.1. If loading the list does not work - copy it and use **Paste Clipboard** button.
4. For each game:
   - **Install** — copies the program to the specified path, renaming the `.exe` accordingly.
   - **Launch** — runs the copied `.exe` with the `--emulate "GameName"` argument.
   - **Delete** — removes the folder (will warn if it does not contain `.orb_emulation`).

You can find game paths here: https://tithen-firion.github.io/discord-games/  
or here: https://cdn.discordapp.com/detectables/games.json

## games.txt Format

```
# Lines starting with # are comments
# Each line: Game Name | Full Path
Neverness to Everness | C:\Program Files (x86)\Steam\steamapps\common\Neverness to Everness\Win64\HTGame.exe
# Or for games with .acf file: Game Name | Full Path | SteamId | Steam App Name
GOALS | C:\Program Files (x86)\Steam\steamapps\common\GOALS\Game\Binaries\Win64\Goals.exe | 2753000 | GOALS
```

## Building from Source

```bash
cd GameEmulator
dotnet build
dotnet run
```

Or compile the app via Visual Studio 2022: open GameEmulator.csproj and press F5.

This app uses the only one analytic on app lauch using anonymous token to the PostHog API. You can check the source for yourself (SendDailyActiveUserAsync() in the MainWindow.xaml.cs).

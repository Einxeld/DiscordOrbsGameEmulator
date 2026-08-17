# Discord Orbs Game Emulator — WPF (.NET 8)

The application copies itself to the paths listed in [games.txt](https://raw.githubusercontent.com/Einxeld/DiscordOrbsGameEmulator/refs/heads/main/games.txt) or added from the [official discord game list](https://cdn.discordapp.com/detectables/games.json), emulating the presence of games to obtain Discord Orbs.

| | |
|:---:|:---:|
| <img width="800" height="600" alt="изображение" src="https://github.com/user-attachments/assets/efbaf813-3b45-4cd4-b2aa-49a2f9a3a7d2" /> | <img width="800" height="600" alt="изображение" src="https://github.com/user-attachments/assets/5ebe5d24-1762-474e-ad82-cda4e0c231ce" /> |
| <img width="720" height="580" alt="изображение" src="https://github.com/user-attachments/assets/3c38a9cb-aa29-4a8f-8c60-54b965664dd9" /> | <img width="720" height="580" alt="изображение" src="https://github.com/user-attachments/assets/384b016f-f5d2-4af2-b019-ac3f209656d0" /> |

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## How to Use

1. Extract the program to a separate folder.
2. Press Install and Run on the game you want to emulate.
3. If the needed game is not in the list, press **Add / Search Web** button to find or create it.

It is possible to pass a custom game list: paste the link into the field and press **Load List**. Default link is `https://raw.githubusercontent.com/Einxeld/DiscordOrbsGameEmulator/refs/heads/main/games.txt`
It is also possible to add a list using clipboard - copy it and use **Paste Clipboard** button.

For each game:
   - **Install** — copies the program to the specified path, renaming the `.exe` accordingly.
   - **Launch** — runs the copied `.exe` with the `--emulate "GameName"` argument.
   - **Delete** — removes the folder (will warn if it does not contain `.orb_emulation`).
   - **Browse** — opens the generated folder for the game.
   - **ACF** — opens the steam folder with the corresponding .acf file highlighted.
   - **Remove** — removes custom added game from the list.

You can find all game paths here: https://tithen-firion.github.io/discord-games/  
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

This app sends only one analytic request on app launch using anonymous token to the PostHog API to understand monthly user count. This way I will understand when the program is no longer needed to users. You can check the source yourself (SendDailyActiveUserAsync() in the MainWindow.xaml.cs).

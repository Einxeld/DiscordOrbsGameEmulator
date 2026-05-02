# Game Emulator — WPF (.NET 8)

Приложение копирует себя по путям из [games.txt](https://raw.githubusercontent.com/Einxeld/DiscordOrbsGameEmulator/refs/heads/main/games.txt), эмулируя наличие игр для получения Discord Orbs.

## Требования

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Как пользоваться

1. Распаковать программу в отдельную папку
2. Скопировать **raw** ссылку: `https://raw.githubusercontent.com/Einxeld/DiscordOrbsGameEmulator/refs/heads/main/games.txt`
3. Вставить в поле URL и нажать **«Загрузить список»**
4. Для каждой игры:
   - **Установить** — копирует программу по указанному пути с переименованием exe
   - **Запустить** — запускает скопированный exe с агрументом --emulate "GameName"
   - **Удалить** — удаляет папку (предупредит, если в ней нет .orb_emulation)

## Формат games.txt
```
# Комментарии начинаются с #
# На каждой строке: Имя игры | Полный путь
Neverness to Everness | C:\Program Files (x86)\Steam\steamapps\common\Neverness to Everness\Win64\HTGame.exe
```

## Для самостоятельной сборки

```bash
cd GameEmulator
dotnet build
dotnet run
```

Или через Visual Studio 2022: открыть `GameEmulator.csproj`, нажать F5.

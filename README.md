# Game Emulator — WPF (.NET 8)

Приложение копирует `calc.exe` по указанным путям, эмулируя наличие игр для получения Discord Orbs.

## Требования

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Сборка и запуск

```bash
cd GameEmulator
dotnet build
dotnet run
```

Или через Visual Studio 2022: открыть `GameEmulator.csproj`, нажать F5.

## Формат games.txt на GitHub

```
# Комментарии начинаются с #
C:\Program Files\Steam\steamapps\common\GameName\game.exe
C:\Games\AnotherGame\AnotherGame.exe
```

- Одна игра на строку
- Полный путь к exe
- Если строка не заканчивается на `.exe` — расширение добавляется автоматически

## Как пользоваться

1. Разместить `games.txt` в GitHub репозитории
2. Скопировать **raw** ссылку: `https://raw.githubusercontent.com/Einxeld/DiscordOrbsGameEmulator/refs/heads/main/games.txt`
3. Вставить в поле URL и нажать **«Загрузить список»**
4. Для каждой игры:
   - **Установить** — копирует `calc.exe` по указанному пути с нужным именем
   - **▶ Запустить** — запускает скопированный exe
   - **Удалить** — удаляет файл (с предупреждением если в папке есть другие файлы)

## Примечание

Если в папке с удаляемым файлом есть другие файлы или папки, перед удалением появится предупреждение — возможно, там установлена настоящая игра.

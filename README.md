# EduTools (Yandex Uchebnik Solver + Урок Цифры)

`by zxchomezxc`

Десктопное приложение для полуавтоматического и автоматического прохождения заданий в Яндекс Учебнике через Edge CDP и внешние/локальные LLM API.

## Состав
- `SolverCore/` — нативная C++20 DLL (`WinHTTP`), выполняющая запросы к OpenAI-совместимым API (OpenRouter, Groq, OpenAI, WusRouter, Ollama, Custom) и Google Gemini.
- `YandexSolver/` — C# WPF (.NET Framework 4.8) интерфейс и автоматизация браузера Edge по Chrome DevTools Protocol (`ClientWebSocket`).
- `build.ps1` — скрипт сборки `SolverCore.dll` и `YandexSolver.exe` в `build/Release/`.

## Сборка
Требуются: Windows 10/11, .NET Framework 4.8 (`MSBuild.exe`) и MinGW-w64 `g++` (например, `BrechtSanders.WinLibs.POSIX.UCRT`).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## Использование
1. Запустите `YandexSolver.exe` (рядом должен лежать `SolverCore.dll`).
2. Во вкладке **Настройки** выберите провайдера, укажите модель и API-ключ (или выберите `Ollama (локально)`), при необходимости нажмите **Проверить API** и **Сохранить настройки**.
3. Во вкладке **Работа** нажмите **Открыть Edge**, войдите в `education.yandex.ru/kids/`, откройте нужное задание и нажмите **Подключить**.
4. Нажмите **Решить текущий шаг** или **Автопрохождение**.

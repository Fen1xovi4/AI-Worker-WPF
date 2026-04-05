# Инструкция по переезду на новый сервер / ПК

## Требования к новой машине

| Компонент | Версия / Примечание |
|-----------|---------------------|
| Windows | 10 / 11 / Server 2019+ |
| .NET Runtime | **8.0 (Desktop Runtime — WPF)** |
| Google Chrome | Последняя стабильная версия |
| RAM | Минимум 4 ГБ (рекомендуется 8 ГБ+) |
| Права | Локальный администратор (для Selenium) |

Скачать .NET 8 Desktop Runtime:
https://dotnet.microsoft.com/en-us/download/dotnet/8.0
→ раздел **.NET Desktop Runtime 8.x.x** → Windows x64

---

## Шаг 1 — Клонировать репозиторий

```bash
git clone https://github.com/Fen1xovi4/AI-Worker-WPF.git
cd AI-Worker-WPF
```

---

## Шаг 2 — Перенести базу данных

База данных SQLite хранится рядом с `.exe` файлом (по умолчанию `worker.db`).

**На старой машине:**
- Найти файл: `bin\Release\net8.0-windows\worker.db`
- Скопировать на новую машину в ту же папку (рядом с `.exe`)

> Если базу не переносить — приложение создаст чистую БД автоматически.
> В этом случае аккаунты, профили и Telegram-боты нужно будет настроить заново.

---

## Шаг 3 — Перенести конфигурацию

Файл: `src/WpfBrowserWorker/appsettings.json`

Заполнить или проверить перед запуском:

```json
{
  "Worker": {
    "WorkerId": "",             // Оставить пустым — сгенерируется автоматически
    "ApiPort": 5000,            // Порт Kestrel API (открыть в фаерволе если нужен внешний доступ)
    "ApiKey": "ВАШ_СЕКРЕТНЫЙ_КЛЮЧ",  // Защита API — задать сложный ключ!
    "MaxBrowsers": 3,           // Кол-во одновременных Chrome (по RAM: 2–4 ГБ на браузер)
    "ChromiumPath": "",         // Оставить пустым — Chrome найдётся автоматически
    "ScreenshotsPath": "./screenshots/",
    "HumanMode": true,
    "LogLevel": "Information",
    "DatabasePath": "./worker.db"
  },
  "Ai": {
    "Provider": "openai",       // "openai" или "deepseek"
    "OpenAiKey": "sk-...",      // Ключ OpenAI
    "OpenAiModel": "gpt-4o-mini",
    "DeepSeekKey": "",
    "DeepSeekModel": "deepseek-chat"
  }
}
```

---

## Шаг 4 — Перенести профили браузера (хромиум-профили)

Профили хранятся на рабочем столе: `%USERPROFILE%\Desktop\ChromiumProfiles\`

**На старой машине:**
- Папка: `C:\Users\<user>\Desktop\ChromiumProfiles\`
- Скопировать папку целиком на новую машину в то же расположение

> Если профили не переносить — нужно будет заново авторизоваться в Instagram/Threads
> и заново привязать профили к аккаунтам в интерфейсе приложения.

---

## Шаг 5 — Сборка и запуск

```bash
dotnet build -c Release
dotnet run --project src/WpfBrowserWorker/WpfBrowserWorker.csproj -c Release
```

Или собрать `.exe`:

```bash
dotnet publish src/WpfBrowserWorker/WpfBrowserWorker.csproj -c Release -r win-x64 --self-contained false
```

Готовый файл появится в: `src/WpfBrowserWorker/bin/Release/net8.0-windows/publish/`

---

## Шаг 6 — Настройка фаервола (если сервер)

Если другие сервисы должны обращаться к API воркера по сети:

```powershell
# Открыть порт 5000 (или тот что задан в ApiPort)
netsh advfirewall firewall add rule name="AI Worker API" dir=in action=allow protocol=TCP localport=5000
```

API принимает запросы с заголовком:
```
X-Api-Key: ВАШ_СЕКРЕТНЫЙ_КЛЮЧ
```

---

## Шаг 7 — Telegram боты

Telegram-боты хранятся в базе данных (`TelegramBotConfigs`).
Если база перенесена — все боты запустятся автоматически при старте приложения.

Если база не перенесена — в интерфейсе (вкладка Accounts) для каждого аккаунта
добавить токен бота заново.

---

## Контрольный список перед запуском

- [ ] Установлен .NET 8 Desktop Runtime
- [ ] Установлен Google Chrome
- [ ] `appsettings.json` заполнен (ApiKey, OpenAiKey/DeepSeekKey)
- [ ] `worker.db` скопирован рядом с `.exe` (или создастся новая БД)
- [ ] Папка `ChromiumProfiles` скопирована на рабочий стол (или готов к переавторизации)
- [ ] Порт 5000 открыт в фаерволе (если нужен внешний доступ)

---

## Что создаётся автоматически при первом запуске

- `worker.db` — база данных SQLite
- `logs/worker-YYYY-MM-DD.log` — лог-файлы
- `screenshots/` — папка для скриншотов
- WorkerId вида `worker-SERVERNAME-xxxxxxxx`

---

## Полезные пути

| Что | Путь |
|-----|------|
| Конфиг | `appsettings.json` рядом с `.exe` |
| База данных | `worker.db` рядом с `.exe` |
| Логи | `logs/` рядом с `.exe` |
| Профили Chrome | `%USERPROFILE%\Desktop\ChromiumProfiles\` |
| Скриншоты | `screenshots/` рядом с `.exe` |

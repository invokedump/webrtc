# Connect — WebRTC Screen Streaming

Трансляция экрана через WebRTC. C# `.exe` захватывает экран и стримит на сайт в реальном времени.

## Архитектура

```
[C# .exe] ──WebSocket──► [Сигнальный сервер Railway] ◄──WebSocket── [Браузер Vercel]
                                     │
                          WebRTC (P2P после handshake)
```

## Деплой: 3 шага

### 1. Сигнальный сервер → Railway

1. Зайди на [railway.app](https://railway.app) и создай аккаунт
2. New Project → Deploy from GitHub repo (или drag & drop папку `server/`)
3. Railway автоматически запустит `node index.js`
4. Скопируй URL вида `https://connect-production-xxxx.railway.app`

### 2. Вставь URL сервера в два файла

В `site/viewer.html` строка:
```js
const SIGNAL_SERVER = 'https://YOUR-SERVER.railway.app';
```

В `exe/Program.cs` строка:
```csharp
private const string SIGNAL_SERVER = "https://YOUR-SERVER.railway.app";
```

### 3. Фронтенд → Vercel

1. Зайди на [vercel.com](https://vercel.com)
2. New Project → импортируй репозиторий
3. Vercel подхватит `vercel.json` и задеплоит папку `site/`
4. Сайт будет доступен по адресу `https://your-project.vercel.app`

## Сборка .exe

Нужен [.NET 6 SDK](https://dotnet.microsoft.com/download) и Windows.

```bash
cd exe
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Файл появится в `exe/bin/Release/net6.0-windows/win-x64/publish/Connect.exe`

> **Важно:** WebView2 Runtime должен быть установлен на машине пользователя.
> Скачать: https://developer.microsoft.com/microsoft-edge/webview2/
> Или добавь `-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` для бандлинга.

## Использование

1. Запусти `Connect.exe` на компьютере, экран которого нужно показать
2. Нажми **"Начать трансляцию"** — появится 6-значный код
3. Открой сайт на другом устройстве, введи код
4. Трансляция начнётся автоматически

## Как работает WebRTC часть

```
C# exe
  └── WebView2 (скрытый браузер)
        └── canvas.captureStream(20fps)  ← JPEG кадры от GDI+
              └── RTCPeerConnection
                    └── H.264/VP8 видео поток → Браузер зрителя
```

C# использует скрытый WebView2 чтобы получить нативный WebRTC с аппаратным кодированием H.264 — без сторонних нативных библиотек.

## Структура файлов

```
connect/
├── server/
│   ├── index.js          # Socket.IO сигнальный сервер
│   ├── package.json
│   └── railway.json      # Railway конфиг
├── site/
│   ├── index.html        # Страница входа (код сессии)
│   └── viewer.html       # Страница просмотра (WebRTC viewer)
├── exe/
│   ├── Connect.csproj
│   └── Program.cs        # WinForms + WebView2 + захват экрана
└── vercel.json           # Vercel конфиг
```

## Зависимости

| Компонент | Пакет |
|-----------|-------|
| WebRTC в C# | Microsoft.Web.WebView2 |
| Socket.IO клиент C# | SocketIOClient |
| Сигнальный сервер | socket.io + express |
| STUN серверы | Google STUN (бесплатно) |

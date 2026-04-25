# ElyProxy — VLESS Client для Windows

Десктопное приложение для управления VLESS-подписками через Xray-core.
Поднимает локальный SOCKS5-прокси (аналог ShadowSocks).

## Возможности

- Мульти-подписки: добавление, удаление, обновление нескольких подписок
- Парсинг VLESS (base64 и plain-text)
- Профили: свои наборы серверов с экспортом/импортом `.elyproxy`
- Ручное добавление серверов через `vless://`
- Пинг серверов с сортировкой по задержке
- Генерация конфига Xray «на лету»
- SOCKS5 прокси на `127.0.0.1:1080`
- Поддержка: TCP+REALITY, WS+TLS, gRPC, H2, SOCKS outbound
- Полностью async — без зависаний UI
- Автосохранение подписок, профилей, настроек
- Сворачивание в системный трей
- Тёмная тема, вкладки (Серверы / Подписки / Профили)
- Ссылки на Telegram и Discord

## Требования

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Xray-core](https://github.com/XTLS/Xray-core/releases)

## Установка Xray

1. Скачайте `Xray-windows-64.zip` из [релизов Xray-core](https://github.com/XTLS/Xray-core/releases)
2. Распакуйте в папку `bin/xray/`:
   ```
   bin/xray/
   ├── xray.exe
   ├── geoip.dat
   └── geosite.dat
   ```

## Сборка и запуск

```bash
dotnet restore
dotnet run

# Release
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

## Использование

1. Запустите ElyProxy
2. Вкладка **Подписки** → введите название и URL → **Добавить**
3. Вкладка **Серверы** → выберите сервер → **Подключить**
4. SOCKS5 прокси: `socks5://127.0.0.1:1080`

## Структура проекта

```
ElyProxy/
├── Core/
│   ├── ConfigBuilder.cs        # VLESS + SOCKS outbound конфиг
│   ├── ProcessManager.cs       # Async управление процессом
│   └── XrayManager.cs          # Async Xray lifecycle
├── Models/
│   ├── VlessNode.cs
│   ├── SocksNode.cs
│   ├── Subscription.cs
│   ├── ProxyProfile.cs
│   └── AppSettings.cs
├── Services/
│   ├── SubscriptionService.cs
│   ├── ParserService.cs
│   ├── StorageService.cs       # Персистентность (%AppData%/ElyProxy/)
│   └── ImportExportService.cs  # Экспорт/импорт .elyproxy
├── ViewModels/
│   ├── ViewModelBase.cs
│   ├── RelayCommand.cs
│   └── MainViewModel.cs
├── Views/
│   └── MainWindow.xaml
├── Utils/
│   └── JsonHelper.cs
└── bin/xray/
```

## Хранение данных

Путь: `%AppData%/ElyProxy/`

- `subscriptions.json` — подписки и их серверы
- `profiles.json` — пользовательские профили
- `settings.json` — настройки, ручные серверы

## Ссылки

- Telegram: https://t.me/ProxyCheckXBot
- Discord: https://discord.gg/sxjV3S7J2k

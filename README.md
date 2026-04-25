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
- Автосохранение подписок, профилей, настроек
- Сворачивание в системный трей

## Установка

### Скачать готовую сборку

1. Перейдите в [Releases](https://github.com/CrackKO/ElyProxy/releases)
2. Скачайте `ElyProxy-v1.0.0-win-x64.zip`
3. Распакуйте в любую папку
4. Скачайте [Xray-core](https://github.com/XTLS/Xray-core/releases) (`Xray-windows-64.zip`)
5. Из архива Xray поместите `xray.exe`, `geoip.dat`, `geosite.dat` в папку `xray/` рядом с `ElyProxy.exe`
6. Запустите `ElyProxy.exe`

> Требуется [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) (если не установлен)

### Собрать из исходников

```bash
git clone https://github.com/CrackKO/ElyProxy.git
cd ElyProxy
dotnet restore
dotnet run
```

Release-сборка:

```bash
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
│   ├── ConfigBuilder.cs
│   ├── ProcessManager.cs
│   └── XrayManager.cs
├── Models/
│   ├── VlessNode.cs
│   ├── SocksNode.cs
│   ├── Subscription.cs
│   ├── ProxyProfile.cs
│   └── AppSettings.cs
├── Services/
│   ├── SubscriptionService.cs
│   ├── ParserService.cs
│   ├── StorageService.cs
│   └── ImportExportService.cs
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

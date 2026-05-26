# ElyProxy

Windows-клиент для VLESS-подписок на базе Xray-core. Приложение поднимает локальный SOCKS5-прокси, умеет подключаться к VLESS-серверам, работать с подписками и включать системный прокси Windows с разделением трафика.

## Возможности

- Подключение к VLESS-серверам через Xray-core
- Поддержка VLESS-ссылок из подписок и ручного ввода `vless://`
- Локальный SOCKS5-прокси: `127.0.0.1:1080`
- Системный прокси Windows с PAC-правилами
- Разделение трафика: выбранные домены идут напрямую, остальное через прокси
- Редактируемые правила разделения трафика в настройках
- Автозапуск вместе с Windows
- Автоподключение к последнему выбранному серверу
- Автопереподключение при обрыве соединения
- Автообновление подписок по выбранному интервалу
- Пинг серверов и отображение задержки
- Профили серверов с импортом и экспортом `.elyproxy`
- Экспорт встроенного файла правил `OmegaRules_auto_switch.sorl` для ZeroOmega
- Быстрые ссылки на расширения ZeroOmega для Firefox и Chrome
- Возможность скрыть или показать панель логов
- Сохранение подписок, профилей, ручных серверов и настроек

## Системный Прокси

Режим **Системный прокси** включает PAC-конфигурацию в настройках Windows. Это позволяет направлять через прокси только нужный трафик, а часть доменов открывать напрямую.

По умолчанию напрямую идут домены:

```text
*.ru
*.рф
*.xn--p1ai
*.su
*.com.ru
*.edu.ru
```

Правила можно изменить во вкладке **Настройки** → **Разделение трафика**. Каждое правило указывается с новой строки.

Важно:

- системный прокси активируется только после подключения к серверу;
- при отключении от сервера системный прокси выключается;
- если режим был включён до отключения, он автоматически активируется снова после следующего подключения.

## Расширения Браузера

Во вкладке **Расширения** есть ссылки на ZeroOmega:

- Firefox: <https://addons.mozilla.org/ru/firefox/addon/zeroomega/>
- Chrome: <https://chromewebstore.google.com/detail/proxy-switchyomega-3-zero/pfnededegaaopdmhkdmcofjmoldfiped?pli=1>

Также из приложения можно сохранить встроенный файл `OmegaRules_auto_switch.sorl`. Он нужен для ZeroOmega, чтобы часть трафика открывалась напрямую.

Расширение стоит использовать только в случае, если режим **Системный прокси** не работает или не подходит для конкретного браузера.

## Установка

### Готовая Сборка

1. Перейдите в [Releases](https://github.com/CrackKO/ElyProxy/releases)
2. Скачайте архив `ElyProxy-v1.1.0-win-x64.zip`
3. Распакуйте в любую папку
4. Если в архиве нет папки `xray`, выполните пункты 5-6.
5. Скачайте [Xray-core](https://github.com/XTLS/Xray-core/releases) (`Xray-windows-64.zip`)
6. Из архива Xray поместите `xray.exe`, `geoip.dat`, `geosite.dat` в папку `xray/` рядом с `ElyProxy.exe`
7. Запустите `ElyProxy.exe`

Если Xray-core не входит в сборку, скачайте `Xray-windows-64.zip` из релизов Xray-core и поместите рядом с приложением:

```text
xray/xray.exe
xray/geoip.dat
xray/geosite.dat
```

Требуется Windows и [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime), если приложение собрано не как self-contained.

### Сборка Из Исходников

```bash
git clone https://github.com/CrackKO/ElyProxy.git
cd ElyProxy
dotnet restore
dotnet build
dotnet run
```

Release-сборка:

```bash
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

## Использование

1. Откройте ElyProxy.
2. Перейдите во вкладку **Подписки**.
3. Введите название и URL подписки.
4. Нажмите **Добавить**.
5. Перейдите во вкладку **Серверы**.
6. Выберите сервер и нажмите **Подключить**.
7. При необходимости включите **Системный прокси** на вкладке **Серверы**.

Для приложений, где прокси нужно указать вручную:

```text
socks5://127.0.0.1:1080
```

## Настройки

Во вкладке **Настройки** доступны:

- запуск вместе с Windows;
- подключение к последнему серверу;
- переподключение при обрыве;
- показ или скрытие логов;
- интервал автообновления подписок;
- правила разделения трафика для системного прокси.

## Хранение Данных

Данные хранятся в:

```text
%AppData%/ElyProxy/
```

Основные файлы:

- `subscriptions.json` — подписки и серверы из подписок;
- `profiles.json` — пользовательские профили;
- `settings.json` — настройки, ручные серверы, правила системного прокси.

## Структура Проекта

```text
ElyProxy/
├── Core/
│   ├── ConfigBuilder.cs
│   ├── ProcessManager.cs
│   └── XrayManager.cs
├── Models/
│   ├── AppSettings.cs
│   ├── ProxyProfile.cs
│   ├── SocksNode.cs
│   ├── Subscription.cs
│   └── VlessNode.cs
├── Services/
│   ├── AutoStartService.cs
│   ├── ImportExportService.cs
│   ├── PacServerService.cs
│   ├── ParserService.cs
│   ├── StorageService.cs
│   ├── SubscriptionService.cs
│   └── WindowsProxyService.cs
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── RelayCommand.cs
│   └── ViewModelBase.cs
├── Views/
│   └── MainWindow.xaml
├── Utils/
│   └── JsonHelper.cs
├── OmegaRules_auto_switch.sorl
├── ElyProxy.csproj
└── icon.ico
```

## Примечания

- Для работы системного прокси приложение меняет пользовательские настройки Windows Internet Settings.
- При выключении системного прокси приложение пытается восстановить предыдущее значение `AutoConfigURL`.
- Если приложение было завершено аварийно, системный прокси можно отключить вручную в настройках Windows.

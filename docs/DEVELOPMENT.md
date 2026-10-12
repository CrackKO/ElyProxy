# Разработка ElyProxy

[← Главная](../README.md) · [Руководство пользователя](USER-GUIDE.md) · [Тестирование](../TESTING.md)

## Среда и сборка

Нужны **Windows x64**, **.NET 8 SDK** и Git. Приложение использует WPF (`net8.0-windows`); для проверки интерфейса требуется Windows.

```powershell
git clone https://github.com/CrackKO/ElyProxy.git
cd ElyProxy
dotnet restore ElyProxy.sln
dotnet build ElyProxy.sln -c Release
dotnet run --project ElyProxy.csproj
```

Сборка приложения и наличие рабочих прокси-ядер — разные этапы. Для подключения через Xray поместите официальный Windows-бинарник и geo-данные в `xray/`. Эта папка исключена из Git. sing-box включён в репозиторий; tun2proxy и Hysteria при необходимости размещаются отдельно или устанавливаются через приложение.

| Компонент | Путь относительно приложения/проекта | Источник |
| :-- | :-- | :-- |
| Xray | `xray/xray.exe`, `xray/geoip.dat`, `xray/geosite.dat` | [XTLS/Xray-core](https://github.com/XTLS/Xray-core/releases) |
| sing-box | `sing-box/sing-box.exe` | [SagerNet/sing-box](https://github.com/SagerNet/sing-box/releases) |
| NaiveProxy runtime | `sing-box/libcronet.dll` рядом с sing-box | Совместимый пакет sing-box с поддержкой NaiveProxy |
| tun2proxy | `tun2proxy/tun2proxy-bin.exe` | [tun2proxy/tun2proxy](https://github.com/tun2proxy/tun2proxy/releases) |
| Hysteria 2 | Папка `hysteria/`, управляется обновлением ядер | [apernet/hysteria](https://github.com/apernet/hysteria/releases) |

`CoreLocator` сначала ищет установленное ядро в `%LocalAppData%\ElyProxy\cores\<id>\`, затем рядом с программой. Пользовательское обновление может поэтому перекрывать встроенную версию.

## Проверки

```powershell
dotnet test tests/ElyProxy.Tests/ElyProxy.Tests.csproj -c Release
Invoke-Pester -Path tools
```

Контрактные проверки sing-box используют Windows-бинарник из `sing-box/` и локальные rule-set-файлы. Проверки Xray-конфигураций требуют локального Xray. Полный план и ручной чек-лист — в [TESTING.md](../TESTING.md).

CI в [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) собирает solution, запускает xUnit и Pester, публикует приложение и проверяет состав пакета на Windows. Публикационная проверка CI не заменяет проверку полного релизного пакета: локальные Xray/tun2proxy не хранятся в Git.

## Публикация

```powershell
dotnet publish ElyProxy.csproj -c Release -r win-x64 --self-contained false -o publish
```

Это framework-dependent сборка: пользователю нужен .NET 8 Desktop Runtime x64. MSBuild также публикует `ElyProxy.Updater` в `updater/` и `ElyProxy.Helper` в `helper/`. Ядра и `Assets/RoscomVPN/` копируются из соответствующих папок проекта, если присутствуют.

Для официального обновления измените версии в `ElyProxy.csproj` и используйте `tools/New-ElyProxyUpdate.ps1`. Полный порядок упаковки и размещения manifest — в [tools/README.md](../tools/README.md). Не публикуйте пакеты только из одного `ElyProxy.exe`.

## Архитектура

| Каталог | Назначение |
| :-- | :-- |
| `Core/` | Генерация конфигов, совместимость протоколов, запуск и остановка ядер, TUN и helper-клиент |
| `Services/` | Подписки и парсеры, хранение данных, системный прокси/PAC, обновления, диагностика, экспорт, мониторинг и оформление |
| `Models/` | Серверы, подписки, профили и настройки; универсальный узел сохраняет имя `VlessNode` для совместимости |
| `ViewModels/` | Поведение приложения и команды; `MainViewModel` разделён на основной и UI partial-файлы |
| `Views/Sections/` | Серверы, подписки, профили и настройки |
| `Views/Sheets/` | Формы добавления, редактирования и обмена |
| `Controls/`, `Theme/` | Собственные контролы, анимации, цветовые токены и стили |
| `Interop/` | Интеграция окна с Windows |
| `ElyProxy.Updater/` | Замена файлов и перезапуск клиента |
| `ElyProxy.Helper/` | Необязательная повышенная служба для запуска ядер |
| `Assets/RoscomVPN/` | Локальный пресет маршрутизации и 22 rule-set-файла |
| `tests/ElyProxy.Tests/`, `tools/` | Тесты приложения и инструменты релиза |

`dist/` содержит старую сборку и не служит источником актуального релиза. `deployment/elyaxis-staging/` не относится к сборке C#-клиента.

Перед изменениями прочитайте [AGENTS.md](../AGENTS.md): там описаны правила потоков WPF, запуска процессов, темизации, повышения прав и обновлений. Пользовательские инструкции должны соответствовать реальным названиям элементов в `Views/` и поведению в `ViewModels/`/`Services/`.

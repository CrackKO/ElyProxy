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

sing-box входит в репозиторий и копируется в сборку из `sing-box/`. Активный Xray по-прежнему размещается локально в `xray/` либо устанавливается в `%LocalAppData%\ElyProxy\cores\xray\`; этот рабочий каталог не отслеживается Git. Ранее скачанный Xray сохранён в `vendor/xray/` как архивный runtime bundle и не активируется автоматически. tun2proxy и Hysteria при необходимости устанавливаются через приложение либо размещаются локально.

| Компонент | Путь | Источник |
| :-- | :-- | :-- |
| Xray | активный: `xray/` или `%LocalAppData%\ElyProxy\cores\xray\`; сохранённая копия: `vendor/xray/` | [XTLS/Xray-core](https://github.com/XTLS/Xray-core/releases) |
| sing-box | `sing-box/sing-box.exe` | [SagerNet/sing-box](https://github.com/SagerNet/sing-box/releases) |
| NaiveProxy runtime | `sing-box/libcronet.dll` рядом с sing-box, если используется | Совместимый пакет sing-box с поддержкой NaiveProxy |
| tun2proxy | `tun2proxy/tun2proxy-bin.exe` | [tun2proxy/tun2proxy](https://github.com/tun2proxy/tun2proxy/releases) |
| Hysteria 2 | `hysteria/`, управляется обновлением ядер | [apernet/hysteria](https://github.com/apernet/hysteria/releases) |

`CoreLocator` сначала ищет установленное ядро в `%LocalAppData%\ElyProxy\cores\<id>\`, затем рядом с программой. Содержимое `vendor/xray/` само по себе в этот поиск не входит.

## Проверки

```powershell
dotnet test tests/ElyProxy.Tests/ElyProxy.Tests.csproj -c Release
Invoke-Pester -Path tools
```

Контрактные проверки используют реальные runtime-ядра, когда они доступны в ожидаемом рабочем расположении. Полный план и ручной чек-лист — в [TESTING.md](../TESTING.md).

CI в [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) собирает solution, запускает xUnit и Pester, публикует приложение и проверяет обязательный состав пакета на Windows.

## Публикация

```powershell
dotnet publish ElyProxy.csproj -c Release -r win-x64 --self-contained false -o publish
```

Это framework-dependent сборка: пользователю нужен .NET 8 Desktop Runtime x64. MSBuild также публикует `ElyProxy.Updater` в `updater/` и `ElyProxy.Helper` в `helper/`. Локально установленный Xray копируется при наличии; sing-box и `Assets/RoscomVPN/` входят в отслеживаемые ресурсы проекта.

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
| `sing-box/` | Отслеживаемое runtime-ядро sing-box |
| `vendor/xray/` | Сохранённый ранее скачанный Xray bundle; не активируется автоматически |
| `Assets/RoscomVPN/` | Локальный пресет маршрутизации и 22 rule-set-файла |
| `tests/ElyProxy.Tests/`, `tools/` | Тесты приложения и инструменты релиза |

Не добавляйте в репозиторий локальные сборки, временные staging-проекты, кэши IDE/AI-инструментов и прочие артефакты разработки.

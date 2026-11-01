# Выпуск обновления ElyProxy

1. В `ElyProxy.csproj` увеличьте `Version`, `AssemblyVersion` и `FileVersion`.
2. Запустите из корня проекта:

```powershell
.\tools\New-ElyProxyUpdate.ps1 -Version 1.3.8 -ReleaseNotes 'Кратко опишите изменения.'
```

Скрипт создаёт новую папку сборки, ZIP и `deployment/elyproxy-update/update.json`.
Он не перезаписывает существующую версию и проверяет, что сборка содержит клиент,
updater, Xray, sing-box и все 22 rule-set файла RoscomVPN DEFAULT.

Проверки вынесены в `ElyProxyUpdateValidation.ps1` и покрыты Pester-тестами
(11 кейсов: версия csproj, запрет перезаписи, обязательные файлы, 22 rule-set):

```powershell
Invoke-Pester -Path tools
```

На сервер сначала загрузите ZIP в:

```text
/var/www/elysium/public/updates/elyproxy/
```

Затем последним загрузите `update.json` в ту же папку. Этот порядок гарантирует,
что manifest никогда не укажет пользователям на ещё отсутствующий пакет.

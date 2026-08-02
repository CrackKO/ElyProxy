# Выпуск обновления ElyProxy

1. В `ElyProxy.csproj` увеличьте `Version`, `AssemblyVersion` и `FileVersion`.
2. Запустите из корня проекта:

```powershell
.\tools\New-ElyProxyUpdate.ps1 -Version 1.3.6 -ReleaseNotes 'Кратко опишите изменения.'
```

Скрипт создаёт новую папку сборки, ZIP и `deployment/elyproxy-update/update.json`.
Он не перезаписывает существующую версию и проверяет, что в архиве есть основной
клиент и отдельный updater.

На сервер сначала загрузите ZIP в:

```text
/var/www/elysium/public/updates/elyproxy/
```

Затем последним загрузите `update.json` в ту же папку. Этот порядок гарантирует,
что manifest никогда не укажет пользователям на ещё отсутствующий пакет.

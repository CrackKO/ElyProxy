# ElyProxy update endpoint

Deploy `update.json` as:

```text
https://elysiumhub.org/updates/elyproxy/update.json
```

Deploy the matching ZIP from `release-build/ElyProxy-<version>-win-x64.zip` as:

```text
https://elysiumhub.org/updates/elyproxy/ElyProxy-<version>-win-x64.zip
```

The SHA-256 in the manifest must be regenerated after every ZIP build. Publish
the ZIP first and the manifest last, so clients never receive a manifest that
points to a missing package.

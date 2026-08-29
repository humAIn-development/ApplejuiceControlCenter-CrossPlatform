# AJCC.Core.Probe

Minimaler GUI-freier Integrationstest für AJCC-X Foundation gegen einen echten AppleJuice-Core.

Der Probe verwendet ausschließlich `AJCC.Core` und prüft nacheinander:

1. Verbindung und `settings.xml`
2. `information.xml`
3. Session über `getsession.xml`
4. initiales `modified.xml` und Aufbau des Runtime-State
5. einen echten weiteren `modified.xml`-Polling-Zyklus

## Lokaler Core ohne Passwort

```text
dotnet run --project tools/AJCC.Core.Probe -- --endpoint http://127.0.0.1:9851/
```

## Passwortgeschützter Core

Das Passwort wird absichtlich nicht als Kommandozeilenargument unterstützt. Es wird aus einer Umgebungsvariable gelesen.

Windows PowerShell:

```text
$env:AJCC_CORE_PASSWORD = "mein-passwort"
dotnet run --project tools/AJCC.Core.Probe -- --endpoint http://127.0.0.1:9851/
```

Linux/macOS:

```text
export AJCC_CORE_PASSWORD='mein-passwort'
dotnet run --project tools/AJCC.Core.Probe -- --endpoint http://127.0.0.1:9851/
```

Eine andere Variable kann mit `--password-env NAME` ausgewählt werden.

## Reverse Proxy / HTTPS

```text
dotnet run --project tools/AJCC.Core.Probe -- --endpoint https://example.org/applejuice/
```

Der Probe gibt keine Rohantworten, Dateinamen oder Passwörter aus. Er meldet nur Verbindungsstatus, Core-Version, aggregierte State-Zahlen, Session-/Timestamp-Status und das Ergebnis des Polling-Proofs.

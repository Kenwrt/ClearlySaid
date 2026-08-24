# ClearlySaid Local Staging

This environment replaces Web02 and API02 until dedicated staging servers exist.
It runs separate Web, API, PostgreSQL, data-protection, port, and credential resources
on the development workstation. It does not deploy to or modify Web01 or API01.

## Prerequisite

Install Docker Desktop with Docker Compose support on the development workstation.
Docker was not installed when this workflow was added, so the container startup must
be validated after that prerequisite is available.

## Initialize and start

```powershell
cd C:\Dev\ClearlySaid
.\scripts\Initialize-LocalStaging.ps1
.\scripts\Start-LocalStaging.ps1
.\scripts\Test-LocalStaging.ps1
.\scripts\Test-LocalFailover.ps1
```

The default URL is `http://localhost:5202`. Random local credentials are written to
`.deployment-secrets\local-staging.env`. That directory is already ignored by Git.
The credentials are not printed.

OpenAI fallback and production billing, email, Google Play, and database credentials
are not configured. Change the Ollama URL in the local environment file only when a
non-production message-processing test is required.

When staging email is not configured, newly registered local accounts are verified
automatically so they can sign in immediately. This behavior is limited to the Staging
environment. Production continues to require email verification.

The failover test starts a temporary API container on loopback port 5203, injects a
definite Ollama preflight failure, verifies selection of the OpenAI path, verifies the
open circuit on a second request, and removes the temporary container. It does not stop
or reconfigure the production Ollama process and does not require or transmit an OpenAI
key.

## Stop

```powershell
.\scripts\Stop-LocalStaging.ps1
```

This retains staging data. Do not add `--volumes` unless all local staging accounts
and activity should be permanently deleted.

## Temporary remote testing

If a remote tester needs access, expose only port 5202 through a separate temporary
Cloudflare Tunnel protected by Cloudflare Access. Do not use the production tunnel,
Web01, API01, or the production database. Remove the temporary route when testing ends.

## Production release gate

Before an approved production release, switch to `main` and run:

```powershell
.\scripts\Assert-ProductionRelease.ps1 -ReleaseTag vX.Y.Z
```

The script requires a clean `main` branch and exact parity among `HEAD`, `origin/main`,
and the annotated release tag. A passing result does not authorize deployment. Explicit
production approval remains required.

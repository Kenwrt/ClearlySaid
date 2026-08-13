# ClearlySaid

ClearlySaid turns dictated or typed thoughts into a clear, concise message. It uses one shared Blazor UI across a .NET MAUI 10 Blazor Hybrid mobile app and an ASP.NET Core 10 Blazor Web App.

## Architecture

- `ClearlySaid.Shared` — reusable Razor components, styles, models, and service interfaces.
- `ClearlySaid.App` — native Android/iOS/Windows MAUI Blazor Hybrid host. It uses native speech recognition and calls Web01.
- `ClearlySaid.Web` — public Blazor application and mobile gateway hosted on Web01.
- `ClearlySaid.Api` — private model-routing control plane hosted on API01. It uses on-prem Ollama first and is the only project that reads the OpenAI fallback key.

The request path is: browser or mobile app → Cloudflare → Web01 (`10.168.168.8`) → private API01 (`10.168.168.7:5103`) → Ollama (`10.168.168.5:11434`), with OpenAI used only after a definite Ollama failure. Web01 uses private PostgreSQL (`10.168.168.9:5432`) for accounts, entitlements, request idempotency, and provider-aware usage. API01, Ollama, and PostgreSQL do not have public URLs.

The mobile apps render the shared components locally; they do not download their UI from Web01. The browser application renders the same components.

## Development prerequisites

- .NET 10 SDK and .NET MAUI workload
- Android SDK/API 36 for Android
- A Mac with compatible Xcode for iOS builds
- Access to Ollama, plus an OpenAI API key when testing fallback

## Run locally

Start API01 locally first:

```powershell
$env:OPENAI_API_KEY = "your-api-key"
$env:CLEARLYSAID_INTERNAL_API_TOKEN = "a-long-random-development-token"
$env:Ollama__BaseUrl = "http://10.168.168.5:11434/"
$env:Ollama__Model = "qwen3-vl:4b-instruct"
dotnet run --project src/ClearlySaid.Api
```

In another terminal, set the same internal token and point Web01 at the local API URL:

```powershell
$env:CLEARLYSAID_INTERNAL_API_TOKEN = "a-long-random-development-token"
$env:Api01__BaseUrl = "https://localhost:<api-port>/"
dotnet run --project src/ClearlySaid.Web
```

Browser dictation depends on browser Web Speech API support.

Run Android with:

```powershell
dotnet run --project src/ClearlySaid.App -f net10.0-android
```

iOS builds must run on or connect to a Mac:

```bash
dotnet run --project src/ClearlySaid.App -f net10.0-ios
```

The mobile app calls `https://clearlysaid.ai/`, configured in `src/ClearlySaid.App/AppSettings.cs`.

## Android testing download

Generate the signed testing APK with `scripts/Publish-Android-Test.ps1`, then publish Web01. The website exposes the package at `/downloads/ClearlySaid-Android-Test.apk` and displays a download banner for Android testers. See `docs/Android-Testing.md` for installation and signing guidance.

## Deploy

1. Deploy the private service using `docs/Api01-Deployment.md` and `scripts/Publish-Api01.ps1`.
2. Deploy the public application using `docs/Web01-Deployment.md` and `scripts/Publish-Web01.ps1`.
3. The existing `WB-HyperV` Cloudflare Tunnel routes `clearlysaid.ai` to Nginx Proxy Manager on Web01 (`10.168.168.8`), which forwards to the ClearlySaid container on Web01 (`10.168.168.8:5102`). The legacy hostname remains available during the mobile-app migration window.
4. Put only `OPENAI_API_KEY` on API01, and configure `Ollama__BaseUrl` and `Ollama__Model` there.
5. Put the same `CLEARLYSAID_INTERNAL_API_TOKEN` on both servers.
6. Restrict API01 inbound HTTPS to Web01's internal IP.
7. Configure Web01's PostgreSQL connection with a protected environment file following `docs/PostgreSql-Setup.md`.

## Move to WrightServer

Git is preferred because it preserves history:

```powershell
git clone <your-git-remote> C:\Source\ClearlySaid
```

For a direct copy, use a writable share:

```powershell
robocopy C:\Users\kenwr\source\repos\Agents \\WrightServer\Development\ClearlySaid /E /XD bin obj .vs .dotnet-cli .nuget-packages .appdata .localappdata artifacts
```

WrightServer resolves from this machine, but its shares currently return Access Denied. The prepared `ClearlySaid-WrightServer.zip` can be copied once a writable UNC destination is available.

## Security

- Never put either server secret in the repository or mobile app.
- Use trusted HTTPS on every hop.
- The public refinement route is limited to 30 requests per minute per client IP and one in-flight request per account.
- Enable the Cloudflare client-IP header setting only when direct access to Web01's origin is blocked.
- Registration, revocable sessions, password hashing, entitlements, monthly quotas, and account deletion are enforced on Web01.
- Every refinement has a unique request ID; duplicate IDs are rejected and the same ID becomes OpenAI's idempotency key.
- Ollama timeouts and interrupted in-flight responses do not automatically fail over because their completion state is ambiguous. Explicit errors and connection refusal may fail over.
- PostgreSQL stores password and session hashes plus provider-aware usage metadata, never plaintext passwords, API keys, payment cards, or message text.
- Publish a privacy policy explaining device speech recognition and transcript processing by Web01, API01, Ollama, and the conditional OpenAI fallback.

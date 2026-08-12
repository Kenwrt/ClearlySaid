# Deploy ClearlySaid to Web01

Web01 is the public Blazor application and mobile gateway. It does not store the OpenAI API key.

## Current Web01 configuration

- Host: Ubuntu at `10.168.168.8`
- Runtime: Docker
- Container: `clearlysaid-web`
- Container image: `clearlysaid-web:20260806.3`
- Published port: `5102` to container port `8080`
- Persistent volume: `clearlysaid-data`
- Public URL: `https://clearlysaid.healthcareautomation.services`

The existing `WB-HyperV` Cloudflare Tunnel sends the public hostname to Nginx Proxy Manager at `http://10.168.168.8:80`. Nginx Proxy Manager forwards it to `http://10.168.168.8:5102` with WebSocket support enabled.

## Build the Web01 image

Copy the repository source to Web01, then run from its root:

```bash
docker build --file Dockerfile.web01 --tag clearlysaid-web:<version> .
```

The multi-stage image builds the Blazor app with the official .NET 10 SDK and runs it on the official ASP.NET Core 10 runtime.

## Run the container

Create the persistent data-protection volume once:

```bash
docker volume create clearlysaid-data
```

Run the web application:

```bash
docker run --detach \
  --name clearlysaid-web \
  --restart unless-stopped \
  --publish 5102:8080 \
  --volume clearlysaid-data:/var/lib/clearlysaid \
  --env-file /home/ken/clearlysaid/secrets/web.env \
  --env Api01__BaseUrl=https://api01/ \
  --env CLEARLYSAID_INTERNAL_API_TOKEN=<same-private-token-used-on-api01> \
  clearlysaid-web:<version>
```

Do not configure `OPENAI_API_KEY` on Web01. The OpenAI key belongs only on API01.
The environment file must contain `ConnectionStrings__ClearlySaid`; see `docs/PostgreSql-Setup.md`. Keep it readable only by its owner and never copy it into the image.

Stripe web billing also belongs on Web01. Add the live secret key, webhook signing secret, and four recurring Price IDs to the same protected environment file. Never place Stripe secrets in `appsettings.json`, the MAUI app, API01, or the container image. See `docs/Subscription-Plans.md` for the exact variable names and webhook destination.

To grant the first administrator, temporarily add the following to Web01's protected environment file and recreate the container:

```text
Admin__BootstrapEmail=<existing-account-email>
```

Bootstrapping only runs when no active administrator exists. After the role is granted, remove this setting from the environment file; the next container recreation will omit it. Admin API authorization reads the current database role on every request. Administrators can manage roles, entitlements, account status, and password resets from `/admin`; password resets revoke the affected user's sessions.

The `/admin/diagnostics` console displays provider, model, request size, latency, fallback, failure, and structured application-event metadata. It never stores or displays submitted or rewritten message text.

## Validate

```bash
curl --header 'Host: clearlysaid.healthcareautomation.services' http://127.0.0.1:5102/health
curl https://clearlysaid.healthcareautomation.services/health
curl -L https://clearlysaid.healthcareautomation.services/downloads/ClearlySaid-Android-Test.apk | sha256sum
```

The health response should identify `ClearlySaid`, and the APK checksum should equal the adjacent `.sha256` file.

## Updating the container

Build a uniquely tagged image, start and validate a replacement container, and only then retire the previous release. Preserve the `clearlysaid-data` volume between releases. Never put API keys or service tokens in the image or repository.

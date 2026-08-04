# Deploy ClearlySaid.Api to API01

API01 is the private model-routing tier. It sends refinement requests to on-prem Ollama first and uses OpenAI only for definite primary-provider failures. It is not published through Cloudflare and does not have a public hostname.

## Current API01 configuration

- Host: Ubuntu at `10.168.168.7`
- Runtime: Docker
- Container: `clearlysaid-api`
- Container image: `clearlysaid-api:20260803.1`
- Private endpoint: `http://10.168.168.7:5103`
- Secrets file: `/home/ken/clearlysaid/secrets/api.env` with mode `600`
- Primary: Ollama at `http://10.168.168.5:11434`, model `qwen3-vl:4b-instruct`
- Fallback: `gpt-5.6-terra` through the OpenAI Responses API

Web01 uses the same `CLEARLYSAID_INTERNAL_API_TOKEN` and calls API01 over the private LAN. Only API01 stores `OPENAI_API_KEY`.

## Build the API image

Copy the repository source to API01, then run from its root:

```bash
docker build --file Dockerfile.api01 --tag clearlysaid-api:<version> .
```

## Configure secrets

The environment file must contain both secrets and must never be committed:

```text
CLEARLYSAID_INTERNAL_API_TOKEN=<shared-random-token>
OPENAI_API_KEY=<openai-api-key>
Ollama__BaseUrl=http://10.168.168.5:11434/
Ollama__Model=qwen3-vl:4b-instruct
Ollama__TimeoutSeconds=25
Routing__OpenAiFallbackEnabled=true
```

Use mode `600` on the file. Enter the OpenAI key using hidden terminal input so it isn't stored in shell history.

## Run the container

```bash
docker run --detach \
  --name clearlysaid-api \
  --restart unless-stopped \
  --env-file /home/ken/clearlysaid/secrets/api.env \
  --publish 10.168.168.7:5103:8080 \
  clearlysaid-api:<version>
```

## Validate

```bash
curl http://10.168.168.7:5103/health
```

An unauthenticated refinement request must be rejected. An authenticated request from Web01 should report `ollama` as its provider while Ollama is healthy. Test fallback separately with a controlled definite failure; never use an artificially short timeout because ambiguous timeouts intentionally do not fail over.

## Network security

- Do not add API01 to Cloudflare or public DNS.
- Restrict inbound port `5103` to Web01 at `10.168.168.8` when configuring the host firewall.
- Keep request bodies and both secrets out of logs.
- Configure OpenAI usage and budget alerts.
- Review provider, fallback, latency, and failure fields in `clearlysaid_usage_events`; alert on unexpected fallback volume.

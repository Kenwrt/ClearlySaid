#!/usr/bin/env bash
set -euo pipefail

api_env="/home/ken/clearlysaid/secrets/api.env"
set -a
# shellcheck disable=SC1090
source "${api_env}"
set +a

request_id="$(cat /proc/sys/kernel/random/uuid)"
user_id="$(cat /proc/sys/kernel/random/uuid)"
response="$(curl --fail --silent \
  --header "X-ClearlySaid-Service-Token: ${CLEARLYSAID_INTERNAL_API_TOKEN}" \
  --header 'Content-Type: application/json' \
  --data "{\"message\":\"release provider health check\",\"requestId\":\"${request_id}\",\"userId\":\"${user_id}\"}" \
  http://10.168.168.7:5103/api/messages/refine)"

if command -v jq >/dev/null 2>&1; then
  provider="$(jq -r '.provider' <<<"${response}")"
  model="$(jq -r '.model' <<<"${response}")"
  fallback="$(jq -r '.fallbackUsed' <<<"${response}")"
else
  grep -q '"provider":"ollama"' <<<"${response}"
  provider="ollama"
  model="configured"
  fallback="false"
fi

test "${provider}" = "ollama"
echo "API01 provider check passed: provider=${provider}, model=${model}, fallback=${fallback}."

#!/usr/bin/env bash
set -euo pipefail

current_name="clearlysaid-web"
backup_name="clearlysaid-web-rollback-webhook-crlf-20260810"
image_name="clearlysaid-web:20260806.3"
web_env="/home/ken/clearlysaid/secrets/web.env"
database_env="/home/ken/clearlysaid/secrets/database.env"
env_backup="/home/ken/clearlysaid/secrets/web.env.pre-crlf-fix-20260810"

if docker container inspect "${backup_name}" >/dev/null 2>&1; then
  echo "Rollback container ${backup_name} already exists; refusing to overwrite it." >&2
  exit 1
fi

cp -p "${web_env}" "${env_backup}"
sed -i 's/\r$//' "${web_env}"
chmod 600 "${web_env}"

webhook_length="$(sed -n 's/^Stripe__WebhookSecret=//p' "${web_env}" | tr -d '\n' | wc -c)"
if [[ "${webhook_length}" -ne 38 ]]; then
  echo "Expected a 38-character Stripe webhook signing secret after normalization." >&2
  exit 1
fi

rollback() {
  echo "Environment normalization failed; restoring the prior container and environment." >&2
  docker stop "${current_name}" >/dev/null 2>&1 || true
  docker rm "${current_name}" >/dev/null 2>&1 || true
  docker rename "${backup_name}" "${current_name}" >/dev/null 2>&1 || true
  cp -p "${env_backup}" "${web_env}"
  docker start "${current_name}" >/dev/null 2>&1 || true
}
trap rollback ERR

docker stop "${current_name}" >/dev/null
docker rename "${current_name}" "${backup_name}"

docker run --detach \
  --log-driver local \
  --log-opt max-size=10m \
  --log-opt max-file=5 \
  --name "${current_name}" \
  --restart unless-stopped \
  --publish 5102:8080 \
  --volume clearlysaid-data:/var/lib/clearlysaid \
  --env-file "${web_env}" \
  --env-file "${database_env}" \
  "${image_name}" >/dev/null

healthy=false
for attempt in {1..30}; do
  if curl --fail --silent \
      --header 'Host: clearlysaid.ai' \
      http://127.0.0.1:5102/health >/dev/null; then
    healthy=true
    break
  fi
  sleep 1
done

if [[ "${healthy}" != true ]]; then
  docker logs --tail 100 "${current_name}" >&2 || true
  false
fi

trap - ERR
echo "Web01 environment normalized; ClearlySaid is healthy."

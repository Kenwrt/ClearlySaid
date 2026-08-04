#!/usr/bin/env bash
set -euo pipefail

new_image="${1:?Usage: deploy-api01-release.sh <image> [backup-name]}"
current_name="clearlysaid-api"
backup_name="${2:-clearlysaid-api-rollback}"
api_env="/home/ken/clearlysaid/secrets/api.env"

if docker container inspect "${backup_name}" >/dev/null 2>&1; then
  echo "Rollback container ${backup_name} already exists; refusing to overwrite it." >&2
  exit 1
fi

docker stop "${current_name}" >/dev/null
docker rename "${current_name}" "${backup_name}"

rollback() {
  echo "New API01 release failed; restoring ${backup_name}." >&2
  docker stop "${current_name}" >/dev/null 2>&1 || true
  docker rm "${current_name}" >/dev/null 2>&1 || true
  docker rename "${backup_name}" "${current_name}" >/dev/null 2>&1 || true
  docker start "${current_name}" >/dev/null
}
trap rollback ERR

docker run --detach \
  --name "${current_name}" \
  --restart unless-stopped \
  --env-file "${api_env}" \
  --publish 10.168.168.7:5103:8080 \
  "${new_image}" >/dev/null

healthy=false
for attempt in {1..30}; do
  if curl --fail --silent http://10.168.168.7:5103/health >/dev/null; then
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
echo "ClearlySaid API01 release ${new_image} is healthy; rollback container retained as ${backup_name}."

#!/usr/bin/env bash
set -euo pipefail

new_image="${1:-clearlysaid-web:20260803.1}"
current_name="clearlysaid-web"
backup_name="${2:-clearlysaid-web-rollback}"
candidate_name="clearlysaid-web-candidate"
web_env="/home/ken/clearlysaid/secrets/web.env"
database_env="/home/ken/clearlysaid/secrets/database.env"
google_play_credential="/home/ken/clearlysaid/secrets/google-play-service-account.json"

if docker container inspect "${backup_name}" >/dev/null 2>&1; then
  echo "Rollback container ${backup_name} already exists; refusing to overwrite it." >&2
  exit 1
fi

docker stop "${candidate_name}" >/dev/null 2>&1 || true
docker rm "${candidate_name}" >/dev/null 2>&1 || true
docker stop "${current_name}" >/dev/null
docker rename "${current_name}" "${backup_name}"

rollback() {
  echo "New release failed; restoring ${backup_name}." >&2
  docker stop "${current_name}" >/dev/null 2>&1 || true
  docker rm "${current_name}" >/dev/null 2>&1 || true
  docker rename "${backup_name}" "${current_name}" >/dev/null 2>&1 || true
  docker start "${current_name}" >/dev/null
}
trap rollback ERR

google_play_args=()
if [[ -f "${google_play_credential}" ]]; then
  google_play_args+=(
    --volume "${google_play_credential}:/run/secrets/google-play-service-account.json:ro"
    --env "GooglePlay__ServiceAccountJsonPath=/run/secrets/google-play-service-account.json"
    --env "GooglePlay__PackageName=com.clearlysaid.app"
  )
fi

docker run --detach \
  --name "${current_name}" \
  --restart unless-stopped \
  --publish 5102:8080 \
  --volume clearlysaid-data:/var/lib/clearlysaid \
  --env-file "${web_env}" \
  --env-file "${database_env}" \
  "${google_play_args[@]}" \
  "${new_image}" >/dev/null

healthy=false
for attempt in {1..30}; do
  if curl --fail --silent \
      --header 'Host: clearlysaid.healthcareautomation.services' \
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
echo "ClearlySaid Web01 release ${new_image} is healthy; rollback container retained as ${backup_name}."

#!/usr/bin/env bash
set -euo pipefail

current_name="clearlysaid-web"
backup_name="clearlysaid-web-rollback-stripe-20260810"
image_name="clearlysaid-web:20260806.3"
web_env="/home/ken/clearlysaid/secrets/web.env"
database_env="/home/ken/clearlysaid/secrets/database.env"
stripe_fragment="/home/ken/clearlysaid/secrets/stripe.env.new"
env_backup="/home/ken/clearlysaid/secrets/web.env.pre-stripe-20260810"
merged_env="${web_env}.stripe-merge"

test -f "${web_env}"
test -f "${database_env}"
test -f "${stripe_fragment}"

if docker container inspect "${backup_name}" >/dev/null 2>&1; then
  echo "Rollback container ${backup_name} already exists; refusing to overwrite it." >&2
  exit 1
fi

cp -p "${web_env}" "${env_backup}"
awk '!/^Stripe__/' "${web_env}" > "${merged_env}"
cat "${stripe_fragment}" >> "${merged_env}"
chmod 600 "${merged_env}"

stripe_setting_count="$(grep -c '^Stripe__' "${merged_env}")"
if [[ "${stripe_setting_count}" -ne 7 ]]; then
  echo "Expected 7 Stripe settings, found ${stripe_setting_count}." >&2
  exit 1
fi

mv "${merged_env}" "${web_env}"

rollback() {
  echo "Stripe configuration failed; restoring the prior Web01 container and environment." >&2
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

rm -f "${stripe_fragment}"
trap - ERR
echo "ClearlySaid Stripe configuration is active and Web01 is healthy."

#!/usr/bin/env bash
set -euo pipefail

web_env=/home/ken/clearlysaid/secrets/web.env
key_file=/tmp/clearlysaid-messaging.key
test -s "$key_file"

if grep -q '^Messaging__ApplicationId=clearlysaid$' "$web_env"; then
  rm -f "$key_file"
  echo "ClearlySaid Web01 messaging settings already exist."
  exit 0
fi

cp "$web_env" "${web_env}.before-messaging"
umask 077
{
  printf '\nMessaging__BaseUrl=https://messaging.healthcareautomation.services/\n'
  printf 'Messaging__ApplicationId=clearlysaid\n'
  printf 'Messaging__ApplicationKey=%s\n' "$(cat "$key_file")"
} >> "$web_env"
chmod 600 "$web_env"
rm -f "$key_file"
echo "ClearlySaid Web01 messaging settings stored without displaying secrets."

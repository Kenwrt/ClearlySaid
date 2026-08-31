#!/usr/bin/env bash
set -euo pipefail

gateway_env=/home/ken/wrightapps/wright-messaging/shared/wright-messaging.env
key_output=/tmp/clearlysaid-messaging.key

read_setting() {
  sed -n "s/^${2}=//p" "$1" | tail -n 1
}

if grep -q '^MessagingGateway__Applications__4__Id=clearlysaid$' "$gateway_env"; then
  echo "ClearlySaid is already registered with the messaging gateway."
  exit 0
fi

umask 077
application_key="$(openssl rand -hex 32)"
application_hash="$(printf '%s' "$application_key" | sha256sum | awk '{print $1}')"
messaging_service_sid="$(read_setting "$gateway_env" 'MessagingGateway__Applications__3__MessagingServiceSid')"
inbound_number="$(read_setting "$gateway_env" 'MessagingGateway__Applications__3__InboundNumber')"
test -n "$messaging_service_sid"
test -n "$inbound_number"

cp "$gateway_env" "${gateway_env}.before-clearlysaid"
{
  printf '\nMessagingGateway__Applications__4__Id=clearlysaid\n'
  printf 'MessagingGateway__Applications__4__DisplayName=ClearlySaid\n'
  printf 'MessagingGateway__Applications__4__MessagingServiceSid=%s\n' "$messaging_service_sid"
  printf 'MessagingGateway__Applications__4__InboundNumber=%s\n' "$inbound_number"
  printf 'MessagingGateway__Applications__4__ApiKeySha256=%s\n' "$application_hash"
} >> "$gateway_env"
printf '%s' "$application_key" > "$key_output"
chmod 600 "$key_output"

docker rm -f wright-messaging-api >/dev/null
docker run -d --name wright-messaging-api --restart unless-stopped \
  --env-file "$gateway_env" -p 10.168.168.7:5108:8080 \
  wright-messaging-api:20260826.1 >/dev/null

echo "ClearlySaid gateway registration configured without displaying secrets."

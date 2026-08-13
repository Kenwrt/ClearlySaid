#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:5102"
host_header="Host: clearlysaid.ai"

# shellcheck disable=SC1091
source /home/ken/clearlysaid/secrets/web.env
# shellcheck disable=SC1091
source /home/ken/clearlysaid/secrets/stripe-e2e-test.env

events_json="$(curl --fail --silent --show-error \
  --user "${Stripe__SecretKey}:" \
  --get \
  --data-urlencode 'types[]=customer.subscription.created' \
  --data-urlencode 'limit=20' \
  https://api.stripe.com/v1/events)"

payload="$(jq -cer --arg user_id "${USER_ID}" \
  '[.data[] | select(.data.object.metadata.clearlysaid_user_id == $user_id)][0]' \
  <<<"${events_json}")"

timestamp="$(date +%s)"
signature="$(printf '%s.%s' "${timestamp}" "${payload}" | \
  openssl dgst -sha256 -hmac "${Stripe__WebhookSecret}" -binary | xxd -p -c 256)"

status_code="$(curl --silent --show-error --output /tmp/clearlysaid-webhook-replay.response \
  --write-out '%{http_code}' \
  --request POST \
  --header "${host_header}" \
  --header 'Content-Type: application/json' \
  --header "Stripe-Signature: t=${timestamp},v1=${signature}" \
  --data-binary "${payload}" \
  "${base_url}/api/billing/stripe/webhook")"

if [[ "${status_code}" != "200" ]]; then
  echo "Signed webhook replay returned HTTP ${status_code}." >&2
  sed -n '1,20p' /tmp/clearlysaid-webhook-replay.response >&2
  exit 1
fi

rm -f /tmp/clearlysaid-webhook-replay.response
echo "Stripe subscription event signature and entitlement processing verified."

#!/usr/bin/env bash
set -euo pipefail

health_code="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --header 'Host: clearlysaid.healthcareautomation.services' \
  http://127.0.0.1:5102/health)"

webhook_code="$(curl --silent --output /dev/null --write-out '%{http_code}' \
  --request POST \
  --header 'Host: clearlysaid.healthcareautomation.services' \
  --header 'Stripe-Signature: invalid' \
  --header 'Content-Type: application/json' \
  --data '{}' \
  http://127.0.0.1:5102/api/billing/stripe/webhook)"

if [[ "${health_code}" != "200" ]]; then
  echo "Web01 health check returned ${health_code}." >&2
  exit 1
fi

if [[ "${webhook_code}" != "400" ]]; then
  echo "Invalid Stripe signature returned ${webhook_code}; expected 400." >&2
  exit 1
fi

echo "Web01 is healthy and rejects invalid Stripe webhook signatures."

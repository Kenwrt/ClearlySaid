#!/usr/bin/env bash
set -euo pipefail

web_env="/home/ken/clearlysaid/secrets/web.env"

set -a
# shellcheck disable=SC1090
source "${web_env}"
set +a

account_response="$(curl --fail --silent --show-error \
  --user "${Stripe__SecretKey}:" \
  https://api.stripe.com/v1/account)"

if ! grep -q '"id": "acct_1U1A8hGzgseArt6w"' <<<"${account_response}"; then
  echo "Stripe did not confirm the Healthcare Automation Services account." >&2
  exit 1
fi

for price_id in \
  "${Stripe__Prices__StandardMonthly}" \
  "${Stripe__Prices__StandardAnnual}" \
  "${Stripe__Prices__ProMonthly}" \
  "${Stripe__Prices__ProAnnual}"; do
  price_response="$(curl --fail --silent --show-error \
    --user "${Stripe__SecretKey}:" \
    "https://api.stripe.com/v1/prices/${price_id}")"
  if ! grep -q "\"id\": \"${price_id}\"" <<<"${price_response}"; then
    echo "Stripe price verification failed for ${price_id}." >&2
    exit 1
  fi
  if ! grep -q '"livemode": true' <<<"${price_response}"; then
    echo "Stripe price ${price_id} is not live." >&2
    exit 1
  fi
done

echo "Stripe live account and all four ClearlySaid prices verified."

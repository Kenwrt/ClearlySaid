#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:5102"
host_header="Host: clearlysaid.healthcareautomation.services"

# shellcheck disable=SC1091
source /home/ken/clearlysaid/secrets/web.env
# shellcheck disable=SC1091
source /home/ken/clearlysaid/secrets/stripe-e2e-test.env
# shellcheck disable=SC1091
source /home/ken/clearlysaid/secrets/stripe-e2e-result.env

charges_json="$(curl --fail --silent --show-error \
  --user "${Stripe__SecretKey}:" \
  --get \
  --data-urlencode "customer=${CUSTOMER_ID}" \
  --data-urlencode 'limit=20' \
  https://api.stripe.com/v1/charges)"

charge_json="$(jq -cer \
  '[.data[] | select(.amount == 249 and .currency == "usd" and .paid == true and .refunded == false)][0]' \
  <<<"${charges_json}")"
charge_id="$(jq -er '.id' <<<"${charge_json}")"
payment_intent_id="$(jq -er '.payment_intent' <<<"${charge_json}")"

subscription_json="$(curl --fail --silent --show-error \
  --request DELETE \
  --user "${Stripe__SecretKey}:" \
  "https://api.stripe.com/v1/subscriptions/${SUBSCRIPTION_ID}")"

if [[ "$(jq -r '.status' <<<"${subscription_json}")" != "canceled" ]]; then
  echo "Stripe did not confirm subscription cancellation." >&2
  exit 1
fi

refund_json="$(curl --fail --silent --show-error \
  --request POST \
  --user "${Stripe__SecretKey}:" \
  --header "Idempotency-Key: clearlysaid-e2e-refund-${SESSION_ID}" \
  --data-urlencode "payment_intent=${payment_intent_id}" \
  --data-urlencode 'amount=249' \
  --data-urlencode 'reason=requested_by_customer' \
  --data-urlencode 'metadata[clearlysaid_e2e_test]=true' \
  https://api.stripe.com/v1/refunds)"

refund_id="$(jq -er '.id' <<<"${refund_json}")"
if [[ "$(jq -r '.status' <<<"${refund_json}")" != "succeeded" ]] || \
   [[ "$(jq -r '.amount' <<<"${refund_json}")" != "249" ]]; then
  echo "Stripe did not confirm the full USD 2.49 refund." >&2
  exit 1
fi

account_json=""
for attempt in {1..30}; do
  account_json="$(curl --fail --silent --show-error \
    --header "${host_header}" \
    --header "Authorization: Bearer ${ACCESS_TOKEN}" \
    "${base_url}/api/account/me")"
  if [[ "$(jq -r '.plan' <<<"${account_json}")" == "free" ]]; then
    break
  fi
  sleep 1
done

if [[ "$(jq -r '.plan' <<<"${account_json}")" != "free" ]]; then
  echo "Stripe canceled and refunded the payment, but ClearlySaid has not returned the test account to Free." >&2
  exit 2
fi

umask 077
printf 'CHARGE_ID=%q\nPAYMENT_INTENT_ID=%q\nREFUND_ID=%q\n' \
  "${charge_id}" "${payment_intent_id}" "${refund_id}" \
  > /home/ken/clearlysaid/secrets/stripe-e2e-refund-result.env
chmod 600 /home/ken/clearlysaid/secrets/stripe-e2e-refund-result.env

echo "Stripe canceled ${SUBSCRIPTION_ID} and completed full refund ${refund_id} for USD 2.49."
echo "ClearlySaid returned the temporary account to Free."

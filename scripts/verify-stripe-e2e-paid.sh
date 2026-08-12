#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:5102"
host_header="Host: clearlysaid.healthcareautomation.services"
state_file="/home/ken/clearlysaid/secrets/stripe-e2e-test.env"
result_file="/home/ken/clearlysaid/secrets/stripe-e2e-result.env"

# shellcheck disable=SC1090
source "${state_file}"
# shellcheck disable=SC1091
source /home/ken/clearlysaid/secrets/web.env

account_json=""
for attempt in {1..30}; do
  account_json="$(curl --fail --silent --show-error \
    --header "${host_header}" \
    --header "Authorization: Bearer ${ACCESS_TOKEN}" \
    "${base_url}/api/account/me")"
  if [[ "$(jq -r '.plan' <<<"${account_json}")" == "standard" ]] && \
     [[ "$(jq -r '.subscriptionProvider' <<<"${account_json}")" == "stripe" ]]; then
    break
  fi
  sleep 1
done

if [[ "$(jq -r '.plan' <<<"${account_json}")" != "standard" ]]; then
  echo "ClearlySaid did not upgrade the test account to Standard." >&2
  exit 1
fi

sessions_json="$(curl --fail --silent --show-error \
  --user "${Stripe__SecretKey}:" \
  --get \
  --data-urlencode 'limit=20' \
  https://api.stripe.com/v1/checkout/sessions)"

session_json="$(jq -cer --arg user_id "${USER_ID}" \
  '[.data[] | select(.client_reference_id == $user_id and .payment_status == "paid")][0]' \
  <<<"${sessions_json}")"

session_id="$(jq -er '.id' <<<"${session_json}")"
subscription_id="$(jq -er '.subscription' <<<"${session_json}")"
customer_id="$(jq -er '.customer' <<<"${session_json}")"
amount_total="$(jq -er '.amount_total' <<<"${session_json}")"
currency="$(jq -er '.currency' <<<"${session_json}")"

if [[ "${amount_total}" != "249" || "${currency}" != "usd" ]]; then
  echo "Expected a paid USD 2.49 checkout; received ${currency} ${amount_total}." >&2
  exit 1
fi

umask 077
printf 'SESSION_ID=%q\nSUBSCRIPTION_ID=%q\nCUSTOMER_ID=%q\n' \
  "${session_id}" "${subscription_id}" "${customer_id}" > "${result_file}"
chmod 600 "${result_file}"

echo "ClearlySaid upgraded the test account to Standard and Stripe recorded a paid USD 2.49 subscription."
echo "Checkout session: ${session_id}"
echo "Subscription: ${subscription_id}"

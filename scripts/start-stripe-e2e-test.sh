#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:5102"
host_header="Host: clearlysaid.ai"
test_email="stripe-e2e-20260810@clearlysaid.ai"
test_password="$(openssl rand -base64 24 | tr -d '\n')Aa1"
state_file="/home/ken/clearlysaid/secrets/stripe-e2e-test.env"
checkout_file="/home/ken/clearlysaid/secrets/stripe-e2e-checkout.url"

register_payload="$(jq -n \
  --arg email "${test_email}" \
  --arg password "${test_password}" \
  '{email: $email, password: $password}')"

register_response="$(curl --fail --silent --show-error \
  --request POST \
  --header "${host_header}" \
  --header 'Content-Type: application/json' \
  --data "${register_payload}" \
  "${base_url}/api/account/register")"

access_token="$(jq -er '.accessToken' <<<"${register_response}")"
user_id="$(jq -er '.account.id' <<<"${register_response}")"

checkout_response="$(curl --fail --silent --show-error \
  --request POST \
  --header "${host_header}" \
  --header 'Content-Type: application/json' \
  --header "Authorization: Bearer ${access_token}" \
  --data '{"plan":"standard","interval":"monthly"}' \
  "${base_url}/api/billing/stripe/checkout")"

checkout_url="$(jq -er '.url' <<<"${checkout_response}")"

umask 077
printf 'TEST_EMAIL=%q\nTEST_PASSWORD=%q\nACCESS_TOKEN=%q\nUSER_ID=%q\n' \
  "${test_email}" "${test_password}" "${access_token}" "${user_id}" > "${state_file}"
printf '%s\n' "${checkout_url}" > "${checkout_file}"
chmod 600 "${state_file}" "${checkout_file}"

echo "Created temporary free user ${test_email} and a Standard monthly checkout session."

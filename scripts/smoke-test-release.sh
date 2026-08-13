#!/usr/bin/env bash
set -euo pipefail

base_url="${1:-http://127.0.0.1:5104}"
host_header="${2:-clearlysaid.ai}"
email="release-smoke-$(date +%s)@example.invalid"
password="ReleaseSmoke-$(openssl rand -hex 12)-A1"
token=""

cleanup() {
  if [[ -n "${token}" ]]; then
    curl --silent --output /dev/null --request DELETE \
      --header "Host: ${host_header}" \
      --header "Authorization: Bearer ${token}" \
      "${base_url}/api/account" || true
  fi
}
trap cleanup EXIT

register_response="$(curl --fail-with-body --silent --show-error \
  --header "Host: ${host_header}" \
  --header 'Content-Type: application/json' \
  --data "{\"email\":\"${email}\",\"password\":\"${password}\"}" \
  "${base_url}/api/account/register")"
token="$(jq --raw-output '.accessToken // empty' <<<"${register_response}")"
[[ -n "${token}" ]]
[[ "$(jq --raw-output '.account.plan' <<<"${register_response}")" == "free" ]]
[[ "$(jq --raw-output '.account.monthlyAllowance' <<<"${register_response}")" == "20" ]]
[[ "$(jq --raw-output '.account.usedThisPeriod' <<<"${register_response}")" == "0" ]]

refine_response="$(curl --fail-with-body --silent --show-error \
  --header "Host: ${host_header}" \
  --header "Authorization: Bearer ${token}" \
  --header 'Content-Type: application/json' \
  --data '{"message":"hey just wanted to let you know i will arrive around three thanks"}' \
  "${base_url}/api/messages/refine")"
[[ -n "$(jq --raw-output '.message // empty' <<<"${refine_response}")" ]]

account_response="$(curl --fail-with-body --silent --show-error \
  --header "Host: ${host_header}" \
  --header "Authorization: Bearer ${token}" \
  "${base_url}/api/account/me")"
[[ "$(jq --raw-output '.usedThisPeriod' <<<"${account_response}")" == "1" ]]
[[ "$(jq --raw-output '.remaining' <<<"${account_response}")" == "19" ]]

delete_status="$(curl --silent --output /dev/null --write-out '%{http_code}' --request DELETE \
  --header "Host: ${host_header}" \
  --header "Authorization: Bearer ${token}" \
  "${base_url}/api/account")"
[[ "${delete_status}" == "204" ]]
token=""

echo "Registration, authentication, routed refinement, usage accounting, and account deletion passed."

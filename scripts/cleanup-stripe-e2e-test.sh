#!/usr/bin/env bash
set -euo pipefail

base_url="http://127.0.0.1:5102"
host_header="Host: clearlysaid.ai"
state_file="/home/ken/clearlysaid/secrets/stripe-e2e-test.env"

# shellcheck disable=SC1090
source "${state_file}"

status_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
  --request DELETE \
  --header "${host_header}" \
  --header "Authorization: Bearer ${ACCESS_TOKEN}" \
  "${base_url}/api/account")"

if [[ "${status_code}" != "204" ]]; then
  echo "Temporary account cleanup returned HTTP ${status_code}." >&2
  exit 1
fi

rm -f \
  /home/ken/clearlysaid/secrets/stripe-e2e-test.env \
  /home/ken/clearlysaid/secrets/stripe-e2e-checkout.url \
  /home/ken/clearlysaid/secrets/stripe-e2e-result.env \
  /home/ken/clearlysaid/secrets/stripe-e2e-refund-result.env

echo "Temporary ClearlySaid billing-test account deleted and test credentials removed."

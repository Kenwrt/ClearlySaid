#!/usr/bin/env bash
set -euo pipefail

env_file="/home/ken/clearlysaid/secrets/web.env"
test -f "${env_file}"

temp_file="$(mktemp /home/ken/clearlysaid/secrets/web.env.XXXXXX)"
trap 'rm -f "${temp_file}"' EXIT
grep -v '^Admin__BootstrapEmail=' "${env_file}" > "${temp_file}" || true
chmod --reference="${env_file}" "${temp_file}"
mv "${temp_file}" "${env_file}"
trap - EXIT

echo "One-time administrator bootstrap removed from the Web01 environment file."

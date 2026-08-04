#!/usr/bin/env bash
set -euo pipefail

admin_email="${1:?Usage: bootstrap-web01-admin.sh <existing-account-email> [backup-suffix]}"
backup_suffix="${2:-before-admin-bootstrap}"
env_file="/home/ken/clearlysaid/secrets/web.env"
backup_file="${env_file}.${backup_suffix}"

test -f "${env_file}"
if [[ -e "${backup_file}" ]]; then
  echo "Backup ${backup_file} already exists; refusing to overwrite it." >&2
  exit 1
fi

cp --preserve=mode,ownership,timestamps "${env_file}" "${backup_file}"
temp_file="$(mktemp /home/ken/clearlysaid/secrets/web.env.XXXXXX)"
trap 'rm -f "${temp_file}"' EXIT
grep -v '^Admin__BootstrapEmail=' "${env_file}" > "${temp_file}" || true
printf '%s\n' "Admin__BootstrapEmail=${admin_email}" >> "${temp_file}"
chmod --reference="${env_file}" "${temp_file}"
mv "${temp_file}" "${env_file}"
trap - EXIT

echo "One-time administrator bootstrap configured; backup retained as ${backup_file}."

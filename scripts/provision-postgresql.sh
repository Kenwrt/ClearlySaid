#!/usr/bin/env bash
set -euo pipefail

container_name="${1:-postgres}"
database_name="clearlysaid"
role_name="clearlysaid_app"
secret_file="${HOME}/clearlysaid-db.env"
database_password="$(openssl rand -hex 32)"
admin_user="$(docker inspect "${container_name}" --format '{{range .Config.Env}}{{println .}}{{end}}' | sed -n 's/^POSTGRES_USER=//p')"

if [[ -z "${admin_user}" ]]; then
  echo "The PostgreSQL administrator role could not be determined from the container." >&2
  exit 1
fi

docker exec --user postgres "${container_name}" psql --username "${admin_user}" --dbname postgres --set ON_ERROR_STOP=1 \
  --command "DO \$\$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${role_name}') THEN CREATE ROLE ${role_name} LOGIN; END IF; END \$\$;" \
  --command "ALTER ROLE ${role_name} PASSWORD '${database_password}';"

if ! docker exec --user postgres "${container_name}" psql --username "${admin_user}" --dbname postgres --tuples-only --no-align \
  --command "SELECT 1 FROM pg_database WHERE datname = '${database_name}';" | grep -qx 1; then
  docker exec --user postgres "${container_name}" createdb --username "${admin_user}" --owner "${role_name}" "${database_name}"
fi

docker exec --user postgres "${container_name}" psql --username "${admin_user}" --dbname postgres --set ON_ERROR_STOP=1 \
  --command "ALTER DATABASE ${database_name} OWNER TO ${role_name};"

umask 077
printf '%s\n' "ConnectionStrings__ClearlySaid=Host=10.168.168.9;Port=5432;Database=${database_name};Username=${role_name};Password=${database_password};SSL Mode=Prefer" > "${secret_file}"
unset database_password
echo "ClearlySaid PostgreSQL database and protected environment file are ready."

# ClearlySaid PostgreSQL setup

ClearlySaid uses PostgreSQL at `10.168.168.9:5432` for account identities, one-way password hashes, hashed sessions, entitlements, request IDs, and provider-aware usage records. Usage records include input/output character counts, estimated input tokens, provider, model, latency, success, fallback use, and a bounded failure reason. It does not store OpenAI keys, Android signing passwords, payment-card details, or message text.

## Create a least-privilege database and role

Run these commands on the PostgreSQL server as its PostgreSQL administrator. `createuser --pwprompt` requests the password without placing it in shell history.

```bash
sudo -u postgres createuser --pwprompt clearlysaid_app
sudo -u postgres createdb --owner=clearlysaid_app clearlysaid
```

Restrict PostgreSQL firewall and `pg_hba.conf` access so `clearlysaid_app` can connect only from Web01 (`10.168.168.8`). Do not expose port 5432 through Cloudflare or the public Internet.

## Configure Web01

Create a root-readable environment file on Web01, outside the repository:

```text
ConnectionStrings__ClearlySaid=Host=10.168.168.9;Port=5432;Database=clearlysaid;Username=clearlysaid_app;Password=REPLACE_WITH_RANDOM_PASSWORD;SSL Mode=Prefer
```

Use `chmod 600` on that file. Pass it to Docker with `--env-file`; do not put the password directly in a reusable shell command. ClearlySaid creates its own application tables at startup. The database role owns only the `clearlysaid` database.

For stronger transport protection, install a trusted PostgreSQL server certificate and change `SSL Mode=Prefer` to `SSL Mode=VerifyFull` with the appropriate root certificate.

Back up the database daily and test restoration periodically. Usage rows do not contain dictated text, but backups still contain account email addresses and must be encrypted and access-controlled.

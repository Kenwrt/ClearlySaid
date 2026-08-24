# ClearlySaid Development and Release Workflow

Commit `ed85fcf` is the live production baseline and is tagged `v1.3.0`.
The `main` branch represents production-approved source. Future work begins on
`develop` and uses `codex/feature-name` or `codex/fix-name` branches.

Web01 and API01 are production systems. Feature, fix, develop, and release-candidate
branches must not be deployed to them.

## Interim staging without Web02 or API02

Use an isolated local Docker environment with separate Web, API, PostgreSQL,
ports, data-protection keys, and non-production credentials. If remote testers
need temporary access later, expose only the local staging port through a separate
Cloudflare Tunnel protected by Cloudflare Access. Never route staging to Web01,
API01, or production data.

## Branch workflow

1. Create work from `develop` on a `codex/` branch.
2. Build and test locally.
3. Merge reviewed work into `develop`.
4. Create `release/x.y.z` for a scheduled release freeze.
5. Test the release candidate in local staging.
6. Merge the approved release into `main`.
7. Create annotated tag `vX.Y.Z`.
8. Obtain explicit production deployment approval.
9. Deploy the exact tag only to applicable production servers.
10. Verify public hosts, health, authentication, protected routes, active images,
    database migration, and rollback containers.

## GitHub protection for main

- Require pull requests.
- Require successful Web and API Release builds and applicable tests.
- Require the branch to be current before merging.
- Block force pushes and deletion.
- Restrict direct pushes.
- Require manual approval for the production environment.

## Release checklist

1. Select a version and release date.
2. Freeze enhancements on `release/x.y.z`.
3. Build Web, API, Android test APK, and the signed Android bundle when credentials are available.
4. Complete local staging UI, authentication, activity, billing test-mode, privacy, and protected-route checks.
5. Review database changes for backward compatibility and rollback safety.
6. Merge through a pull request, tag the approved commit, and obtain production approval.
7. Deploy the exact tag and retain the prior production containers for rollback.
8. Merge release corrections back into `develop`.

Urgent fixes begin from the current production tag on `hotfix/x.y.z`, follow the
same test and approval process, and are merged back into `develop`.

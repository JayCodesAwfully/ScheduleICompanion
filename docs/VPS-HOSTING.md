# Companion VPS hosting

## Static mod catalogue

The Companion only requires two HTTPS-accessible files for each release:

- `catalog.json`
- the mod DLL referenced by `downloadUrl`

Copy `ScheduleICompanion.App/ModPackages/catalog.json` as a starting point, replace the
`bundled:` URL with the public HTTPS URL of the uploaded DLL, and retain the exact SHA-256
hash. Enter the public catalogue URL in the Companion's **Mods** tab. The manager refuses
non-HTTPS downloads and refuses a DLL whose hash does not match the catalogue.

A normal Nginx or Caddy static directory is sufficient. Serve the catalogue with
`Content-Type: application/json`, enable TLS, and give DLLs immutable versioned names so
cached releases cannot be silently replaced.

## Optional backpack backup API

The VPS is a durability mirror, not the authority for live inventory transfers. The game host
commits a transaction first; the owner or host then uploads the committed snapshot.

Recommended endpoints:

- `POST /v1/auth/steam-ticket` exchanges a Steam session ticket for a short-lived token.
- `GET /v1/backpacks/{careerId}` returns the authenticated owner's latest snapshot.
- `PUT /v1/backpacks/{careerId}` requires `If-Match` with the previous revision/hash.
- `GET /v1/backpacks/{careerId}/history` lists immutable recovery revisions.
- `POST /v1/backpacks/{careerId}/restore/{revision}` creates a new revision from history.

Store `steam_id`, `career_id`, `revision`, `content_hash`, encrypted snapshot bytes,
transaction-tail hash, creation time, and uploader identity. A unique constraint on
`(steam_id, career_id, revision)` plus conditional updates prevents an older client from
overwriting newer data.

The mod must queue uploads while offline and must never clear a local backpack because the
service is unavailable. Do not place database credentials or a server master key in the mod;
only the API service talks directly to the database.

### Deployment packages

- `VPS-Backpack-Database-Setup-Windows` installs the service natively on Windows Server 2025.
- `VPS-Backpack-Database-Setup` deploys the containerised service to Ubuntu/Debian from a
  Windows administration PC.

The Windows package uses PostgreSQL 18, Python 3.13 and Caddy. Only Caddy accepts public
traffic; PostgreSQL and the FastAPI process are restricted to the VPS loopback interface.
Start with the package's `README-FIRST.txt`, then run `SETUP-WINDOWS-VPS.bat` as Administrator.
It accepts either a domain or a dedicated public IPv4 address. IP mode requests Let's Encrypt's
short-lived IP certificate profile and relies on Caddy's automatic renewal.

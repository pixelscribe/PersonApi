# PersonApi

A minimal ASP.NET Core Web API for managing `Person` records, backed by MySQL via EF Core (Pomelo provider).

## Requirements

- .NET 10 SDK
- A reachable MySQL server

## Configuration

The API needs one setting: a MySQL connection string at `ConnectionStrings:Default`. The app throws on startup if it's missing.

Pick one of the following. Env vars and user-secrets override `appsettings.json`.

**Option 1 — appsettings.Development.json** (simplest for local dev; note this file is tracked by git, so avoid real credentials here if the repo is shared):
```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Port=3306;Database=personapi;User=root;Password=yourpassword;"
  }
}
```

**Option 2 — user-secrets** (keeps credentials out of the repo entirely):
```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Port=3306;Database=personapi;User=root;Password=yourpassword;"
```

**Option 3 — environment variable** (`:` becomes `__`):
```powershell
$env:ConnectionStrings__Default = "Server=localhost;Port=3306;Database=personapi;User=root;Password=yourpassword;"
```
Note this only applies to the process/session it's set in — running via `dotnet run` in that same terminal picks it up, but launching via a debugger (F5) starts a separate process that won't inherit it unless it's also added to `Properties/launchSettings.json` or set at the User/Machine level.

## Database setup

This project uses EF Core migrations to create the schema, including the `FULLTEXT` index on `(first_name, last_name)` that the search endpoint depends on.

```powershell
dotnet tool install --global dotnet-ef   # if not already installed
dotnet ef database update
```

To bootstrap a brand-new server, point `ConnectionStrings:Default` at it (see Configuration above) and run `dotnet ef database update` — this creates the database itself (if the configured user has `CREATE` privileges) and applies all migrations.

Note: migration generation (`dotnet ef migrations add ...`) doesn't require a live database connection, even though the app normally uses `ServerVersion.AutoDetect` at startup — `Data/AppDbContextFactory.cs` provides a fixed server version for design-time tooling.

## Running

```powershell
dotnet run
```

By default (see `Properties/launchSettings.json`):
- HTTP: `http://localhost:5094`
- HTTPS: `https://localhost:7268`

The raw OpenAPI document is available at `/openapi/v1.json` in the Development environment (no Swagger UI is configured).

## Testing

```powershell
dotnet test
```

`PersonApi.Tests/` contains integration tests that spin up a real, disposable MySQL 8.0 container via [Testcontainers](https://testcontainers.com/) for the run — Docker must be running locally, but no manual database setup or connection string is needed; the test fixture (`PersonApiFactory`) handles that automatically. The suite covers all `/api/person` endpoints, including the `search` endpoint's `FULLTEXT` behavior, which an in-memory/mocked provider can't validate.

## Docker

```powershell
docker build -t personapi .
docker run --rm -p 8080:8080 -e ConnectionStrings__Default="Server=...;Database=personapi;User=...;Password=...;" personapi
```

The container listens on port `8080` with no HTTPS inside it — terminate TLS at a reverse proxy/load balancer in front of it. The app starts successfully even without a reachable database; only requests that actually touch the database fail until `ConnectionStrings__Default` points somewhere valid.

## CI/CD

- **`.github/workflows/ci.yml`** — runs on every pull request and push to `main`: builds, then runs the full test suite (GitHub's hosted Linux runners have Docker preinstalled, so Testcontainers works with no extra setup). Test results are published directly to the PR via `dorny/test-reporter`, not just a pass/fail badge.
- **`.github/workflows/release.yml`** — runs after a PR merges to `main`. Computes the next version from [Conventional Commits](https://www.conventionalcommits.org/) messages since the last tag (`fix:` → patch, `feat:` → minor, `feat!:`/a `BREAKING CHANGE:` footer → major), then pushes a git tag and creates a GitHub Release — each one an immutable snapshot of that commit.
- `main` is protected by a branch ruleset: changes must go through a PR, and the `test` check from `ci.yml` must pass before merging. Self-approval is currently allowed (no required review count).

## API Reference

Base route: `/api/person`

### `Person` shape

```json
{
  "id": 1,
  "firstName": "Jane",
  "lastName": "Doe",
  "email": "jane.doe@example.com",
  "createdAt": "2026-07-21T12:00:00"
}
```

`createdAt` is set by the database (`CURRENT_TIMESTAMP`) and cannot be set by the client.

---

### `GET /api/person`

Returns all people, ordered by `id`.

```powershell
Invoke-RestMethod -Uri "http://localhost:5094/api/person" -Method Get
```

**200 OK** — array of `Person`.

---

### `GET /api/person/{id}`

Returns a single person by id.

```powershell
Invoke-RestMethod -Uri "http://localhost:5094/api/person/1" -Method Get
```

- **200 OK** — `Person`
- **404 Not Found** — no person with that id

---

### `GET /api/person/search?q={query}`

Full-text search over `first_name` and `last_name` (uses a MySQL `FULLTEXT` index, not `LIKE`).

```powershell
Invoke-RestMethod -Uri "http://localhost:5094/api/person/search?q=jane" -Method Get
```

- **200 OK** — array of matching `Person`
- **400 Bad Request** — `q` is missing/blank

---

### `POST /api/person`

Creates a person.

**Body:**
```json
{
  "firstName": "Jane",
  "lastName": "Doe",
  "email": "jane.doe@example.com"
}
```
All fields required; `email` must be a valid email address; `firstName`/`lastName` max 100 chars, `email` max 255 chars.

```powershell
Invoke-RestMethod -Uri "http://localhost:5094/api/person" -Method Post -ContentType "application/json" -Body (@{
    FirstName = "Jane"
    LastName  = "Doe"
    Email     = "jane.doe@example.com"
} | ConvertTo-Json)
```

- **201 Created** — the created `Person`, with a `Location` header pointing to `GET /api/person/{id}`
- **400 Bad Request** — validation failure
- **409 Conflict** — `email` already exists

---

### `PUT /api/person/{id}`

Replaces an existing person's fields. Same body/validation as `POST`.

```powershell
Invoke-RestMethod -Uri "http://localhost:5094/api/person/1" -Method Put -ContentType "application/json" -Body (@{
    FirstName = "Jane"
    LastName  = "Smith"
    Email     = "jane.smith@example.com"
} | ConvertTo-Json)
```

- **204 No Content** — updated
- **400 Bad Request** — validation failure
- **404 Not Found** — no person with that id
- **409 Conflict** — `email` already in use by another person

---

### `DELETE /api/person/{id}`

```powershell
Invoke-RestMethod -Uri "http://localhost:5094/api/person/1" -Method Delete
```

- **204 No Content** — deleted
- **404 Not Found** — no person with that id

## Known issues

- `PersonApi.csproj` pins `Microsoft.EntityFrameworkCore.Design` to `9.0.0` rather than the latest `10.x`, since Pomelo's MySQL provider (`9.0.0`) only supports EF Core 9.x so far. Revisit once Pomelo ships an EF Core 10-compatible release.

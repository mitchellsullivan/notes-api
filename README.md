# Notes Service

A small note-taking HTTP API. ASP.NET Core 6 + EF Core, with token auth,
per-note sharing (users and teams), optimistic concurrency via ETags, and
full-text search.

## Run (zero dependencies: SQLite)

```bash
dotnet run --project src/NotesService
```

Data lives in `./data/notes.db` (override the location with the `DATA_FILE`
environment variable).

## Run with Postgres (docker-compose)

```bash
docker compose up --build
```

This starts Postgres 16 and the API on http://localhost:8080. The compose
file sets `POSTGRES_CONNECTION`, which switches the app to Postgres;
without it the app falls back to SQLite. On Postgres, search
(`GET /v1/notes?q=...`) is served by a database-maintained tsvector with a
GIN index; on SQLite it falls back to a substring scan.

## Tests

```bash
dotnet test
```

Tests boot the real application (via `WebApplicationFactory`) against a
throwaway SQLite file per test class — no external dependencies. To
exercise the exact provider the docker-compose deployment uses, point the
suite at a Postgres server:

```bash
docker compose up -d db
POSTGRES_TEST_CONNECTION="Host=localhost;Database=notes;Username=notes;Password=notes" dotnet test
```

Each test class then gets its own `notes_test_*` database, dropped when the
fixture disposes.

## API overview

- `POST /v1/users` — create a user; the response contains the bearer token
  (shown exactly once). Everything below except `/healthz` requires
  `Authorization: Bearer <token>`.
- `GET /v1/me` — the current user.
- `POST /v1/notes` — create a note.
- `GET /v1/notes?q=&limit=&offset=` — list notes visible to you (owned or
  shared, directly or via a team), newest first.
- `GET /v1/notes/{id}` — one note plus your effective permission.
- `PATCH /v1/notes/{id}` — edit title/body (requires edit permission).
- `DELETE /v1/notes/{id}` — delete (owner only).
- `POST|GET|DELETE /v1/notes/{id}/shares` — manage grants (owner only).
- `POST /v1/teams`, `GET /v1/teams/{id}`,
  `POST|DELETE /v1/teams/{id}/members[/{userId}]` — teams.

Mutating note requests are optimistic-concurrency checked: send
`If-Match: "<version>"` using the ETag returned by reads and writes.
Errors are shaped as `{ "error": { "code", "message" } }`.

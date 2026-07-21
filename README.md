# Notes Service

DISCLAIMER: I’m taking a calculated risk and treating this challenge as an AI-fluency demonstration, for one reason:

_The recruiter explicitly stressed that this is an AI-forward role and asked in-depth about my experience coding with AI. Without that conversation, I would have approached this differently._

The features were planned in advance by me. The code was interactively generated with Claude Fable, GPT-5.6 Sol, and Kimi K3. 

My goal is to show that in a few hours I can collaborate with AI to ship something as correct and feature-complete as possible, without exceeding my existing skillset or anything I'm capable of explaining. This may be wildly off-base, but I'm gambling that for an AI-native role it isn't.

---

**Description** - A small note-taking HTTP API. ASP.NET Core 6 + EF Core, with token auth,
per-note sharing (users and teams), optimistic concurrency via ETags, and
full-text search.

**Language/Framework** — .NET. This is a Java shop, and C# is close enough to be easily readable by Java devs. My professional experience is one year of Java, plus several more years across .NET and Go. Doing this in Spring Boot would be blatantly dishonest.

**Storage** — SQLite for tests, Postgres for release.

## Design choices

- Is user management, full support for password login, etc., a primary concern, versus implementing the notes features? (I'll skip it.)
- Is SQLite enough for the spec's "several small teams"? (Probably yes — but still used Postgres for release and FTS, with SQLite kept for zero-dependency dev and tests.)
- Implement full-text search? (Yes — a Postgres tsvector with a GIN index, falling back to a substring scan on SQLite.)
- Optimistic concurrency via ETag/`If-Match` rather than last-write-wins.
- Opaque tokens over JWT, at the cost of one DB read per request — acceptable at this scale.
- 404 instead of 403 when a caller lacks rights, so the API never confirms a resource exists.

## Run with Docker and Postgres (docker-compose)

```bash
docker compose up --build
```

This starts Postgres 16 and the API on http://localhost:8080. The compose
file sets `POSTGRES_CONNECTION`, which switches the app to Postgres;
without it the app falls back to SQLite. On Postgres, search
(`GET /v1/notes?q=...`) is served by a database-maintained tsvector with a
GIN index; on SQLite it falls back to a substring scan.

## Run with dotnet (zero dependencies: SQLite)

```bash
dotnet run --project src/NotesService
```

Data lives in `./data/notes.db` (override the location with the `DATA_FILE`
environment variable).

## API docs

Interactive OpenAPI docs (Swagger UI) are served at `/swagger` in all
environments, so reviewers running via Docker get them too. A real
deployment would gate this to non-production. Paste the token returned by
`POST /v1/users` into the Authorize dialog to call protected endpoints.

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
POSTGRES_TEST_CONNECTION="Host=localhost;Database=notes;Username=notes;Password=notes"
dotnet test
docker compose down
```

Each test class then gets its own `notes_test_*` database, dropped when the
fixture disposes.

## API overview

- `POST /v1/users` — create a user; the response contains the bearer token
  (shown exactly once). Everything below except `/healthz` requires
  `Authorization: Bearer &lt;token&gt;`.
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
`If-Match: "&lt;version&gt;"` using the ETag returned by reads and writes.
Errors are shaped as `{ "error": { "code", "message" } }`.

## With more time

- Full User CRUD with passwords, etc.
- Token lifecycle (expiry, rotation, revocation)
- EF migrations instead of `EnsureCreated`
- Stop serving Swagger in production

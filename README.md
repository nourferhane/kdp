# ZUNAVIO · KDP Factory

A production-grade .NET 10 monolith that runs a multi-agent pipeline for Amazon KDP
books end to end: idea → market research/validation → architecture → manuscript →
visual production → book production → metadata → QA → ready-to-publish → published.

PostgreSQL is the **single source of truth** (state machine, runs, reviews, assets,
background jobs). LLM agents and Google Drive/Docs/Sheets are the execution layer.
The control plane never depends on AI for a correct transition; a deterministic
orchestrator decides, an optional AI decider layers on top, and human reviews
consistently gate checkpoint transitions.

## Layout

```
src/
  Zunavio.KdpFactory.Domain        Entities, value objects, enums, gate state machine
  Zunavio.KdpFactory.Application   Orchestration, deciders, human reviews, DTOs, DI
  Zunavio.KdpFactory.Infrastructure EF Core + PostgreSQL, OpenAI, Google, background jobs, seeding
  Zunavio.KdpFactory.Web           REST API (Swagger), health checks, Blazor Server dashboard, Dockerfile
tests/
  Zunavio.KdpFactory.Domain.Tests
  Zunavio.KdpFactory.Application.Tests
  Zunavio.KdpFactory.IntegrationTests
```

## The pipeline

- **Gate chain**: IDEA → MARKET_RESEARCH → MARKET_VALIDATION → ARCHITECTURE →
  MANUSCRIPT → VISUAL_PRODUCTION → BOOK_PRODUCTION → METADATA → QA →
  READY_TO_PUBLISH → PUBLISHED, plus PAUSED / REJECTED.
- **Advance** only moves to the immediately-next gate; **rollback** may move to any
  earlier gate (never into IDEA or PUBLISHED). QA promotion requires QA PASS.
- **Decisions** are layered: deterministic rules first (Level 1), then the optional
  AI decider (Level 2). Mandatory human reviews always apply (concept selection,
  image-generation approval, QA-unresolved-issues, pre-publication, agent review).
- **Agents** (9): Scout, Validator, Architect, Writer, ArtDirector, Production,
  Metadata, QA, Launch. Each run is a `background_jobs` row claimed by the
  in-process worker using `FOR UPDATE SKIP LOCKED`; output JSON is validated
  against a per-agent contract and the AgentRun stores evidence (payload +
  artifact file ids) for any human review.
- **Google** (optional): Drive artifact folders/docs, Docs prompt authoring,
  Sheets "ZUNAVIO KDP CONTROL CENTER" read/write mirror (never breaks the app).

## Quick start (local)

Requires Docker (for Postgres) and the .NET SDK 10.0.

```bash
cp .env.example .env           # defaults are enough for a local run
docker compose up -d db        # Postgres on 5432
dotnet run --project src/Zunavio.KdpFactory.Web
```

The dashboard and REST API are protected by cookie authentication. Sign in with
`ADMIN_USERNAME` / `ADMIN_PASSWORD` (from `.env`; the example defaults are
`admin` / `change-me`). In **Production** both values are **required** and the
app refuses to start without them.

On first startup the app migrates the database, seeds the 9-agent catalog and the
demo project `ZNV-001` (Winter Quest — The Lost Snowflake Compass) already parked
at MANUSCRIPT with an approved architecture doc.

- Dashboard (Blazor Server): http://localhost:5063
- Login: http://localhost:5063/login
- Swagger: http://localhost:5063/swagger
- Health: `/health/live`, `/health/ready`

Anonymous access is limited to `/login`, `/auth/login`, `/auth/logout`, `/health/*`
and (Development only) `/swagger`. Every other page and every `/api/*` endpoint
requires an authenticated session.

Without `OPENAI_API_KEY` and Google credentials, everything runs deterministically
(prompt offline text, no Drive). Set them in `.env` to enable the full AI layer.

## Deploy

- **docker compose up --build** builds the web service + Postgres (override env
  values via a local `.env` — never edit secrets into `docker-compose.yml`).
- **Koyeb (recommended)**: follow `KOYEB_DEPLOYMENT.md` for the exact service,
  health-check and environment-variable configuration with Supabase.
- **Render**: `render.yaml` provides the web service (Docker) and a managed
  `kdpfactory` Postgres database. Set the `sync: false` secrets in the dashboard.

## Security

- All credentials (`DATABASE_URL`, `OPENAI_API_KEY`, `GOOGLE_SERVICE_ACCOUNT_JSON`,
  `ADMIN_USERNAME`, `ADMIN_PASSWORD`) are environment variables only. A real
  `.env` is gitignored; `.env.example` holds placeholders.
- **Rotate `ADMIN_PASSWORD` immediately if it may have been exposed.** An earlier
  revision of this repository committed a real admin password in
  `docker-compose.yml` (commit `38551d7`). It was removed from the working tree
  but still exists in git history — treat that password as compromised, change it
  in production, and rotate anything else that reused it.
- Production `DATABASE_URL` connections default to `SslMode=Require`.
- `/health/live` and `/health/ready` are public but never leak exception details.

## REST API (highlights)

```
GET    /api/projects                 list
POST   /api/projects                 create (enqueues bootstrap)
GET    /api/projects/{id}            detail
POST   /api/projects/{id}/run-next   dispatch the next agent for a gate
POST   /api/projects/{id}/run-agent/{agentCode}
POST   /api/projects/{id}/continue   resume after a review rollback
POST   /api/projects/{id}/pause | /resume
GET    /api/reviews/pending
POST   /api/reviews/{id}/approve | /reject | /cancel   {comment,resolutionPayloadJson}
GET    /api/agents
POST   /api/agents/{id}/refresh-prompt
GET    /api/jobs?take=50
POST   /api/control-center/import
POST   /api/control-center/seed-agent-prompt-ids
```

## Tests

### Import a project from the existing Control Center

After setting `ASSET_UPLOAD_API_KEY`, Google credentials and
`GOOGLE_CONTROL_CENTER_SPREADSHEET_ID` in `.env`, rebuild the web container:

```bash
docker compose up -d --build web
read -r -s -p 'Asset API key: ' ZUNAVIO_IMPORT_KEY; echo
curl -fsS -X POST http://localhost:8080/api/controlcenter/import-project/ZNV-002 \
  -H "X-Asset-Api-Key: $ZUNAVIO_IMPORT_KEY"
unset ZUNAVIO_IMPORT_KEY
```

This endpoint reads only the matching row from the legacy `Projects` tab and
inserts it into PostgreSQL without changing the spreadsheet. Repeating the
request returns `created: false`. It preserves `VISUAL_PRODUCTION` and maps
`BLOCKED_NEEDS_HUMAN` to the application's `Paused` status while retaining the
original status in the response and the sheet. The image uploader uses the
project's existing Drive folder, registers verified images in PostgreSQL and
adds their asset rows without rewriting legacy sheet headers.

Check the result with the plugin's `zunavio_get_project_assets` tool. It should
return `success: true` even before images exist (`count: 0`).

```bash
dotnet test
```

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

On first startup the app migrates the database, seeds the 9-agent catalog and the
demo project `ZNV-001` (Winter Quest — The Lost Snowflake Compass) already parked
at MANUSCRIPT with an approved architecture doc.

- Dashboard (Blazor Server): http://localhost:5xxx
- Swagger: http://localhost:5xxx/swagger
- Health: `/health/live`, `/health/ready`

Without `OPENAI_API_KEY` and Google credentials, everything runs deterministically
(prompt offline text, no Drive). Set them in `.env` to enable the full AI layer.

## Deploy

- **docker compose up --build** builds the web service + Postgres (override env
  values in `docker-compose.yml`).
- **Render**: `render.yaml` provides the web service (Docker) and a managed
  `kdpfactory` Postgres database. Set the `sync: false` secrets in the dashboard.

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

```bash
dotnet test
```
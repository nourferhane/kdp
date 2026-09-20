# Deploying to Koyeb (with Supabase PostgreSQL)

This guide is specific to Koyeb + Supabase. For general setup, see `README.md`.

## Prerequisites

- A Koyeb account and installed `koyeb` CLI (optional; the dashboard works too)
- A Supabase project with the following enabled:
  - PostgreSQL database (the project's default)
  - **Session Pooler** connection string copied from
    Supabase Dashboard → Project Settings → **Database → Connection string → Session pooler**

> The Session Pooler (port `5432` via the `db.<ref>.supabase.co` host) is the
> recommended connection for server-side workloads like Koyeb. It keeps a
> pooled connection to the database and works behind NAT/load balancers.
> The Transaction Pooler (port `6543`) is only needed if your app uses
> transaction-less ad-hoc connections heavily; stick to **Session Pooler** here.

Do **not** hardcode the Supabase host or password in this repository. Everything
secret is injected at deploy time through Koyeb Secrets / Environment Variables.

## 1. Repository configuration

- Repository: `nourferhane/kdp`
- Branch: `master`
- Builder: **Dockerfile**
- Dockerfile path: `src/Zunavio.KdpFactory.Web/Dockerfile`
- Build context: repository root
- Exposed port: `8080`

The container binds to `0.0.0.0` by default (`ASPNETCORE_URLS=http://+:8080`) and
honors the `PORT` environment variable if Koyeb sets it: when `PORT` is present
the app binds to `http://0.0.0.0:${PORT}`.

## 2. Health checks

- **Live probe**: `GET /health/live` — process liveness, no dependencies
- **Ready probe**: `GET /health/ready` — returns `Healthy` only when PostgreSQL
  is reachable (runs `SELECT 1`); returns `Unhealthy` otherwise

Both endpoints are public (anonymous) and never echo sensitive details.

Configure Koyeb's health check to `GET /health/ready`.

## 3. Environment variables (Koyeb Secrets / Env Vars)

Enter the real secrets via Koyeb Secrets or as environment variables — never
commit them.

| Key | Value / note |
| --- | --- |
| `DATABASE_URL` | Supabase **Session Pooler** URL, e.g. `postgresql://postgres.<ref>:<password>@aws-0-<region>.pooler.supabase.com:5432/postgres` |
| `MIGRATE_ON_STARTUP` | `true` for the first deploy (applies EF migrations), `false` afterwards |
| `SEED_DEMO_PROJECT` | `false` in production |
| `RUN_BACKGROUND_WORKER` | `true` (web + worker run in the same service) |
| `AI_PROVIDER` | `Gemini` or `OpenAI` |
| `GEMINI_API_KEY` | secret — Gemini Developer API key |
| `GEMINI_MODEL` | `gemini-3.8-flash` by default |
| `OPENAI_API_KEY` | optional alternative secret when `AI_PROVIDER=OpenAI` |
| `OPENAI_MODEL` | `gpt-4o-mini` or your OpenAI model |
| `GOOGLE_SERVICE_ACCOUNT_JSON` | secret — full service-account JSON on one line |
| `GOOGLE_ROOT_FOLDER_ID` | `13ni2nWoQ4V9mxiC0BvPKPMoBgxHEdJvC` |
| `GOOGLE_PROMPTS_FOLDER_ID` | `1jfwxolnzema4QCE1YYPBDtBKgDphDMIY` |
| `GOOGLE_PROJECT_FOLDER_ID` | `13ni2nWoQ4V9mxiC0BvPKPMoBgxHEdJvC` |
| `GOOGLE_ARCHITECTURE_DOC_ID` | `1xJq1bzh19oHwqpW3-j76uk1mIu7mh0HH7awFnkQFVZI` |
| `GOOGLE_CONTROL_CENTER_SPREADSHEET_ID` | `12BNoMgLvZ6tXu16flHAQwwfNeWdaGQsLCaqKCF9N-rA` |
| `ADMIN_USERNAME` | secret — dashboard/API login |
| `ADMIN_PASSWORD` | secret — dashboard/API login (strong, unique) |

`ADMIN_USERNAME` and `ADMIN_PASSWORD` are **required in production**: the app
refuses to start without them.

## 4. SSL / TLS notes

- Production connection strings default to `SslMode=Require` (encryption
  enabled); full certificate validation can be forced with `?sslmode=verify-full`.
- Koyeb terminates TLS at its edge; the app trusts `X-Forwarded-Proto` /
  `X-Forwarded-For` via `UseForwardedHeaders` so cookies stay `Secure` and
  redirects do not loop.

## 5. First deploy checklist

1. Create the Supabase project and copy the **Session Pooler** URL into
   `DATABASE_URL`.
2. On Koyeb, create a service from the GitHub repo as described above.
3. Add all environment variables (use Koyeb Secrets for passwords/keys).
4. Set `MIGRATE_ON_STARTUP=true`.
5. Trigger the deploy; watch `/health/ready` become `Healthy`.
6. Log in at `https://<service>.koyeb.app/login` with `ADMIN_USERNAME` /
   `ADMIN_PASSWORD`.
7. After the first successful boot, flip `MIGRATE_ON_STARTUP=false` (migrations
   are already applied and idempotent; this gives you explicit control).
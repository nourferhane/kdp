# Connecting ChatGPT to the KDP Factory MCP server

The MCP server lives at `/mcp` and speaks the Streamable HTTP transport.
ChatGPT's built-in "Connectors" feature (and any MCP client that supports
OAuth 2.0 for protected resources) can plug into it directly.

Everything below that touches the **Auth0 dashboard requires a human tenant
admin** — the values cannot be invented or guessed. This app never hardcodes
them and never bypasses them.

## How the pieces fit together

```
ChatGPT / MCP client
   │ 1. GET /mcp  ->  200, application/json, endpoint "/mcp"
   │ 2. GET {AuthorizationServers}/.well-known/oauth-authorization-server  (RFC 8414 metadata)
   │ 3. POST {metadata}/oidc/register            (RFC 7591 dynamic client registration)
   │ 4. browser OAuth dance (PKCE) against Auth0 -> access token + requested scopes
   │ 5. MCP JSON-RPC over {endpoint}, Authorization: Bearer <token>
   ▼
ZUNAVIO KDP Factory (this app)
   │ - Accepts the Bearer token if it validates against the Auth0 tenant/audience
   │ - Serves protected-resource metadata at
   │     /.well-known/oauth-protected-resource (RFC 9728)
   │ - Authorizes every tool call by scope:
   │     mcp:tools        read-only tools
   │     mcp:tools:write  write tools (import, upload, generation)
```

The app's `/.well-known/oauth-protected-resource` advertises scopes
`mcp:tools` and `mcp:tools:write` so a compliant client requests exactly
those during the authorization step. `zunavio_import_project`,
`zunavio_upload_image`, `zunavio_resume_visual_production`,
`zunavio_record_scout_rejection` and `zunavio_finalize_scout_rejection` require the
write scope; read tools use `mcp:tools` alone.

## Auth0 tenant setup (one-time, human in the dashboard)

1. **Dynamic client registration must be enabled.** Auth0 exposes
   `POST https://{tenant}.{region}.auth0.com/oidc/register` but it is
   **disabled by default**. In Auth0 Dashboard → Settings → Dynamic Client
   Registration, switch registration ON for the OAuth flows you intend to use
   (the MCP connector registers a new client each first connect, prefix
   `tpc_`). No client secret is involved: PKCE is mandatory.

2. **Create a Client Grant per client.** Auth0 Dashboard → Applications →
   APIs (or Applications → Settings → APIs tab) still require you to
   authorize each registered `client_id` for the scopes, otherwise the token
   comes back without `mcp:tools` / `mcp:tools:write` and every tool returns
   access-denied. Because MCP clients create new `tpc_` clients at runtime,
   new registrations need a matching grant — plan to re-check this page
   whenever a new connector is added.

3. **Pick the audience.** `AUTH0_AUDIENCE` must equal the `aud` claim of the
   tokens, so point it at an Auth0 API identifier (for example the tenant's
   default API). `AUTH0_DOMAIN` is the tenant subdomain *without* the scheme
   and without a trailing slash, e.g. `your-tenant.us.auth0.com`.

4. **Remember the MCP resource itself does not own client credentials.** It
   only validates tokens. All consent, scopes and grant handling live in the
   Auth0 tenant; scope strings on the Auth0 API must match
   `mcp:tools` / `mcp:tools:write` exactly (space-separated in the token's
   `scope` or `permissions` claim — both are accepted).

## Environment variables

| Variable | Required | Meaning |
|----------|----------|---------|
| `AUTH0_DOMAIN` | Production: yes | Auth0 tenant subdomain, no scheme/trailing slash |
| `AUTH0_AUDIENCE` | Production: yes | Auth0 API identifier used as the JWT audience |

- Leave both **unset in Development** → the app registers a placeholder Bearer
  scheme and `/mcp` returns **401 for every token** (loud, never silently open).
- **Production refuses to start** if either is missing (same as `ADMIN_*`).

They are read at startup from the environment and passed through
`docker-compose.yml` from the host `.env`:

```bash
# .env (see .env.example)
AUTH0_DOMAIN=your-tenant.us.auth0.com
AUTH0_AUDIENCE=https://your-tenant.us.auth0.com/api/v2/
```

## Connect ChatGPT

1. ChatGPT (Pro/Plus/Team) → Settings → Connectors → **Add connector** →
   **Type an endpoint URL manually**.
2. Enter the public base URL of the app, e.g. `https://kdp.example.com/mcp`.
   ChatGPT reads the protected-resource metadata, runs the OAuth dance and then
   lists the available tools:
   - `zunavio_get_project` (read)
   - `zunavio_get_project_assets` (read)
   - `zunavio_verify_asset` (read)
   - `zunavio_import_project` (write)
   - `zunavio_upload_image` (write)
   - `zunavio_resume_visual_production` (write)
   - `zunavio_record_scout_rejection` (write)
   - `zunavio_finalize_scout_rejection` (write)
3. Authorize the requested scopes in the Auth0 consent screen when prompted.

## Verify

```bash
# Protected-resource metadata advertised by the app
curl -fsS https://kdp.example.com/.well-known/oauth-protected-resource | jq

# Auth0 metadata the client actually follows (RFC 8414)
curl -fsS https://your-tenant.us.auth0.com/.well-known/oauth-authorization-server | jq .authorization_endpoint
```

With a valid read token, ask ChatGPT to check a known project
(`zunavio_get_project { "projectCode": "ZNV-001" }`). To test a write, ask it
to import `ZNV-002` via `zunavio_import_project` — the import is read-only on
the spreadsheet, idempotent, and a repeat returns `created: false`.

## Recover blocked visual production

1. Check `zunavio_get_project`. An unknown project returns `project_not_found`;
   the uploader will not create its folder or claim an API project exists.
2. If PostgreSQL says `Paused / VisualProduction` and the legacy Control Center
   says `VISUAL_PRODUCTION / BLOCKED_NEEDS_HUMAN /
   HUMAN_INTERVENTION_REQUIRED`, call `zunavio_resume_visual_production`.
   This guarded operation synchronizes the sheet and PostgreSQL to `ACTIVE`,
   `RUN_IMAGE_GENERATION`, `PENDING_IMAGE_GENERATION`. A pending review or changed
   row blocks it. It never marks visual QA complete.
3. Generate each image with ChatGPT Image Generation. Pass the *real generated
   file bytes* as base64 (no data-URI prefix) to `zunavio_upload_image` with
   the project code and page number. This validates the image signature,
   uploads/replaces the canonical page file, downloads it from Drive for a
   SHA-256 comparison, and registers a Draft asset in PostgreSQL. It returns
   the actual `assetCode` and `driveFileId`; a local path or made-up ID is not
   accepted. Two known diagnostic placeholder hashes are refused.
4. Call `zunavio_verify_asset` with each returned `assetCode`. It independently
   downloads the Drive bytes and checks the recorded hash. Inspect the physical
   Drive image and perform independent visual QA before any later gate change.

The REST `POST /api/assets/upload` route uses the same verification service
and still requires its API key. Deployment, OAuth write-scope grant, connector
refresh and live end-to-end validation are separate operational steps. Never
publish on KDP as part of this recovery.

## Record an evidence-backed Scout rejection

1. The project must be active at `MarketResearch` in PostgreSQL and
   `MARKET_RESEARCH / ACTIVE` in the legacy Control Center. After a Validator
   hold, both must show `VALIDATOR_HOLD / RUN_SCOUT_TARGETED_EVIDENCE`.
2. Write and inspect the Scout decision as a real Google Doc in one of the
   project's subfolders. Call `zunavio_record_scout_rejection` with its real
   Drive file ID and version such as `v1.2`. The server checks the Drive file
   and text, registers the evidence in PostgreSQL and verifies the updated
   Control Center and PostgreSQL states. It sets
   `SCOUT_REJECTION_RECOMMENDED / REVIEW_SCOUT_DECISION` without archiving.
3. Produce a **separate** Orchestrator review document in a project subfolder.
   If the review independently confirms rejection, include the exact decision
   code `SCOUT_FAIL_UNPROVEN_DEMAND` and call
   `zunavio_finalize_scout_rejection` with its own file ID. This operation
   requires the earlier registered Scout evidence and archives the project;
   it does not publish anything. A changed state or pending human review blocks
   either operation. On a reconciliation error, inspect both systems before
   retrying; never infer success from a Drive document alone.

## Troubleshooting

| Symptom | Likely cause |
|---------|--------------|
| Client registration fails (`invalid_client`/registration URL unknown) | Dynamic client registration disabled in the Auth0 tenant |
| 401 on every tool call | `AUTH0_*` unset (dev placeholder) or token audience ≠ `AUTH0_AUDIENCE` |
| 403 / "access denied" although the token is valid | Missing Client Grant: no `mcp:tools`/`mcp:tools:write` on that `client_id` |
| Tool exists but ChatGPT can't find the endpoint | Base URL should point at the Streamable HTTP endpoint (usually `/mcp`) |
| Every login opens a new consent | New `tpc_` client registered per connector/refreshed session; check grants again |

## Security notes

- Tools fail safe: the auth policies are applied in `Program.cs` and in the
  `Zunavio.KdpFactory.Web/Mcp` folder. An endpoint policy on `/mcp` enforces the
  `McpAuth` scheme; a second policy per tool enforces the scope, and write tools
  additionally require `mcp:tools:write`.
- The dashboard/cookie session is unrelated to MCP tokens; both must be
  configured independently.
- No secret ever leaves the server; only the IdP-issued Bearer token is
  submitted by the client.

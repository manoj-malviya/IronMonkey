---
name: run-ironmonkey
description: Build, run, screenshot, and drive the IronMonkey CRM (.NET 10 Aspire API + Blazor Server web + PostgreSQL). Use when asked to run, start, stop, launch, smoke-test, screenshot, or verify IronMonkey in the real app, or to reproduce a UI or API behaviour end-to-end.
---

# Running IronMonkey

Multi-tenant CRM: .NET 10 + Aspire. Three moving parts — a PostgreSQL container,
`IronMonkey.ApiService`, and the `IronMonkey.Web` Blazor **Server** frontend.

Two things to know before you start:

- **`make` is the control surface.** `make up` / `make down` / `make status`.
- **The Blazor UI needs a real browser.** Pages render over a SignalR circuit, so
  `@bind-Value` only captures input after the circuit boots. `curl` can fetch the
  HTML but can never fill the form. Use the driver.

All paths below are relative to the repo root. Requires Docker (PostgreSQL runs in
a container) and `google-chrome` (present at `/usr/bin/google-chrome`).

## Start / stop

```bash
make up        # PostgreSQL + API + web; blocks until each answers
make status    # live health probes
make down      # stops all three, removes the PostgreSQL container
```

`make up` from cold takes ~1–2 min: it creates the `CentralDb` database, applies 7
migrations, and Hangfire builds 12 more tables. `make help` lists every target;
`make doctor` prints prerequisites plus this repo's known issues.

URLs: web `http://localhost:5299`, API `https://localhost:7306`, Aspire dashboard
`https://localhost:17019` (login token is in `.run/apphost.log`).

## Drive it (agent path)

`.claude/skills/run-ironmonkey/driver.mjs` — zero dependencies, speaks CDP to
system Chrome over Node's built-in `WebSocket`. Exits non-zero if any check fails.

```bash
node .claude/skills/run-ironmonkey/driver.mjs smoke     # API + page reachability
node .claude/skills/run-ironmonkey/driver.mjs signup    # fill + submit the signup form
node .claude/skills/run-ironmonkey/driver.mjs login     # bad-credential path
node .claude/skills/run-ironmonkey/driver.mjs counter    # proves @onclick works
node .claude/skills/run-ironmonkey/driver.mjs all        # all of the above
node .claude/skills/run-ironmonkey/driver.mjs shot http://localhost:5299/ .run/home.png
```

`all` output, verified against a running stack:

```
  PASS  GET  /health          200
  PASS  GET  /api/recipes     200
  PASS  GET  /tenants         401
  PASS  form controls present  found 8
  PASS  recipe cards loaded from API  ...|Automobile Dealership|Educational Institution
  PASS  all fields filled  7/7
  PASS  navigated to success page  http://localhost:5299/signup/success
  PASS  email retained (circuit live)  "nobody@example.com"
  PASS  @onclick updates server state  0 -> 2
```

Screenshots land in `.run/` (gitignored). **Open them** — a blank frame means the
circuit never booted, not that the app is fine.

`signup` writes a real row. Confirm persistence:

```bash
make psql   # then: SELECT "CompanyName","AdminEmail","Status" FROM "SignupRequests" ORDER BY "CreatedAt" DESC LIMIT 3;
```

Overrides, if ports are taken: `IM_WEB_URL`, `IM_API_URL`, `IM_CDP_PORT`, `IM_OUT_DIR`.

### Adding a flow

Extend `driver.mjs`. The two helpers that matter:

- `chrome.fillByLabel('Business Email', 'x@y.com')` — the forms have **no id or
  name attributes**, so this walks from the visible `<label>` to the next
  input/select sibling, then dispatches `input` + `change` so Blazor observes it.
  Returns `'ok'`, `'no-label'`, or `'no-control'` — check it.
- `chrome.goto(url)` — navigates, then polls for `window.Blazor` before returning.

## Tests

```bash
make test                                                            # 151 tests, ~4 min
dotnet test IronMonkey.Tests --filter "FullyQualifiedName~TenantProvisioningTests"   # 4 tests, ~15 s
```

Integration tests use Testcontainers, so Docker is required. Prefer `--filter`
while iterating — the full run is slow.

## Gotchas

- **`make up` does not start the frontend via Aspire.** `AppHost.cs` calls
  `.WaitFor(apiService)` on the `webfrontend` resource, and Aspire 13.1.0 never
  launches it — DCP registers only `postgres` and `apiservice`, with no log line
  about `webfrontend` at all. Bisected: dropping `.WaitFor` launches it in ~10 s;
  `WithExternalHttpEndpoints()` is innocent. So `make start-web` runs the Blazor app
  directly, passing `services__apiservice__https__0` to point it at the API.
  Aligning `Aspire.Hosting.PostgreSQL` 9.1.0 → 13.1.0 (SDK version) does **not**
  fix it.
- **Do not run `dotnet dev-certs https --trust`.** It hangs on Linux waiting for a
  keyring prompt. Not needed: the Web app accepts the dev cert in Development only
  (`Program.cs`), and the driver uses `curl -k`.
- **`dotnet ef` ignores `ConnectionStrings__CentralDb`.** `DesignTimeContextFactories.cs`
  hardcodes `localhost:5432`, but Aspire assigns a **random** host port each run.
  Always pass `--connection` explicitly — `make migrate` computes it from the
  container.
- **A page with `@onclick` or `@bind-Value` that does nothing** is almost always a
  missing render mode. `App.razor` sets `@rendermode="InteractiveServer"` on
  `<Routes>` and `<HeadOutlet>`; that covers every routed page, so individual pages
  need no directive. Do not add `@rendermode @(null)` to opt a page out — component
  discovery throws `ArgumentNullException` at startup and the app won't boot.
- **`pgrep`/`pkill -f 'IronMonkey.Web/bin'` matches your own shell.** Anchor to the
  executable: `pkill -f 'IronMonkey.Web/bin/.*/IronMonkey.Web$'`. The Makefile does.
- **`dotnet run` re-execs**, so a recorded PID is the launcher, not the app. Two
  `IronMonkey.Web` processes is normal (launcher + app); confirm with
  `ss -ltnp | grep :5299` — there should be exactly one listener.
- **`GET /roles` returns 500.** Pre-existing, unrelated to routing: `ListRoles` uses
  the obsolete `AppDbContext` against the central DB, where the tenant-scoped
  `Roles` table doesn't exist. Other `AppDbContext` endpoints likely share it.
- **Admin pages redirect (302) rather than render.** They carry `[Authorize]`. To drive
  them, log in as the platform SuperAdmin: start the app with
  `PlatformAdmin__Password='<pw>' make up` (seeding is skipped when the password is
  unset), then log in with the `PlatformAdmin:Email` from `appsettings.json`. The admin
  API lives under `/admin/*` (e.g. `POST /admin/signup/{id}/approve`) and requires the
  `admin:access` permission, which only SuperAdmin holds — a tenant Admin gets 403.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `make up` times out; log shows `3D000: database "CentralDb" does not exist` | Aspire's `AddDatabase()` never issues `CREATE DATABASE`. The API creates it at startup; if you see this, `make down && make up` for a clean container. |
| Web log: `JavaScript interop calls cannot be issued at this time` | Something called `ProtectedSessionStorage` during prerender. Read the token via `AdminAuthenticationStateProvider.GetTokenAsync()` (in-memory cache), never session storage directly. |
| `Failed to bind to address http://127.0.0.1:5299: address already in use` | A previous web instance survived. `make stop-web`, then retry. |
| Driver: `Chrome CDP never came up on :<port>` | Stale Chrome holding the port: `pkill -f 'remote-debugging-port=9222'`, or pass `IM_CDP_PORT=9333`. Note Chrome silently refuses to start a second instance sharing a `--user-data-dir`, so the driver suffixes the profile dir with the port — keep that if you edit the launch args. |
| Driver reports `no-label` / `no-control` for a field | The label text changed. Grep the `.razor` file for the current text and update the call. |
| API 404 on a route that looks registered | Endpoints declare **absolute** paths (`MapGet("/api/recipes")`). If someone re-adds a `MapGroup("/api/recipes")` prefix, the route becomes `/api/recipes/api/recipes`. Groups here must use `MapGroup(string.Empty)`. |
| `AmbiguousMatchException` on a route | Two endpoints declare the same verb+path. `CreateUser` (legacy) and `CreateTenantUserEndpoint` both claimed `POST /users`; the legacy one is deliberately left unregistered. |

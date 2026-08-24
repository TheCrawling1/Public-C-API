# API Router

A small, RESTful **API router / gateway** built with ASP.NET Core 8 and EF Core (SQLite).

It sits between clients and other APIs as an intermediary layer. A client sends it
**one** request describing what it wants; the router authenticates the caller,
checks the request against per-user **rules**, then **forwards or executes** the work
and returns one **bundled** response.

Because it is an intermediary that acts on the caller's behalf, it leans directly into
the REST **layered-system** constraint — the design and the architecture reinforce each
other, which is the point of the project.

---

## What it does

- **Receive API requests** — clean, noun-based resources over standard HTTP verbs.
- **Send API requests** — forwards sub-requests to registered upstream **targets**.
- **Establish rules on requests** — first-class, editable policy `rules`, evaluated on
  every request (allow / deny / restrict method or target / rate-limit).
- **Specific users** — each caller is a `user` authenticated by an API key, with its own rules.
- **Automatic requests** — a stored dispatch can be fired on a schedule with no client involved.

### The signature demo: change your desktop wallpaper via an API call

A `target` is pluggable. It is either an **`http`** target (forward the request to an external
API) or an **`action`** target (run a local, in-process handler). The bundled
`set-wallpaper` action downloads an image and applies it as the desktop wallpaper —
natively on **Windows**, **GNOME/Linux**, and **macOS**. Wrap it in a `schedule` and your
wallpaper rotates automatically.

---

## How it honors REST

| Principle | In this project |
|---|---|
| **Client–server** | The router is a standalone service; clients only speak HTTP + JSON. |
| **Statelessness** | Every request carries its own `X-Api-Key`; no server-side session is held. |
| **Uniform interface** | Resource nouns (`/users`, `/rules`, `/targets`, `/dispatches`, `/schedules`), standard verbs, correct status codes (`201`, `204`, `400`, `401`, `403`, `404`, `409`). |
| **Layered system** | The router *is* the intermediary layer — clients don't know what's behind it. |
| **Discoverable** | Reads are safe, idempotent `GET`s (cacheable by default HTTP semantics); OpenAPI/Swagger documents the whole surface. |

---

## Architecture

```
        ┌──────────────────────────── API Router ────────────────────────────┐
        │                                                                     │
Client ─┼─► Controllers ─► API-key auth ─► Rule engine ─► Dispatch executor ──┼─► HTTP target (forward)
 (HTTP) │   (REST layer)   (per user)      (+ rate limit)   │                 │
        │                                                   └── Action handler┼─► set-wallpaper (local OS)
        │                                                                     │
        │   Scheduler (background) ─────────► fires stored dispatches ────────┘
        │                                                                     │
        │   EF Core / SQLite  ◄── users · rules · targets · dispatches · schedules
        └─────────────────────────────────────────────────────────────────────┘
```

### Resources

| Resource | Purpose |
|---|---|
| `User` | A caller, authenticated by an API key; owns rules, dispatches, schedules. |
| `Rule` | A policy evaluated per request. Deny-by-default; first match (by priority) wins. |
| `Target` | A destination: `http` (forwarded) or `action` (local handler, e.g. `set-wallpaper`). |
| `Dispatch` | One inbound job holding one or more **steps** (sub-requests) + their results. |
| `Schedule` | A stored dispatch fired automatically on an interval. |

---

## Endpoints

| Method & path | Description | Auth |
|---|---|---|
| `POST /api/dispatches` | Run a bundled job of sub-requests, return aggregated results | API key |
| `GET /api/dispatches` · `GET /api/dispatches/{id}` | Your dispatch history / one dispatch | API key |
| `GET/POST/PATCH/DELETE /api/schedules` | Manage automatic dispatches | API key |
| `GET/POST/PATCH/DELETE /api/users` | Manage callers | **admin**¹ |
| `GET/POST/PATCH/DELETE /api/targets` | Manage destinations | **admin**¹ |
| `GET/POST/PATCH/DELETE /api/rules` | Manage policy | **admin**¹ |
| `GET /health` | Liveness probe | open |

¹ Management endpoints require an **admin** API key — they expose API keys and grant
access. The seeded demo user is the bootstrap admin (`IsAdmin = true`); it can mint
further users, admin or not. This solves the chicken-and-egg problem: one admin exists
from first run, and everything else is created through authenticated calls.

---

## Getting started

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet restore
dotnet run --project src/ApiRouter
```

Then open **http://localhost:5080/swagger** (or **https://localhost:7080/swagger** with the
`https` profile — run `dotnet dev-certs https --trust` once so the browser trusts the local
cert). On first run the app applies migrations to create `router.db` (SQLite) and seeds:

- a demo **admin user** with API key `demo-key-please-change`,
- an `httpbin` **http target** and a `wallpaper` **action target**,
- one allow **rule** (deny-by-default otherwise), rate-limited to 60 req/min.

In Swagger, click **Authorize** and paste the demo key to call the protected endpoints.
Ready-to-run examples are in [`docs/requests.http`](docs/requests.http).

> Development runs over HTTP for convenience; in any non-Development environment HTTP is
> redirected to HTTPS and HSTS is sent.

### Schema changes

The schema is versioned with EF Core migrations under `src/ApiRouter/Data/Migrations`.
After changing an entity, add a migration (applied automatically on next startup):

```bash
dotnet tool install --global dotnet-ef      # once
dotnet ef migrations add <Name> --project src/ApiRouter
```

### Example 1 — bundled fan-out

```jsonc
POST /api/dispatches      // header: X-Api-Key: demo-key-please-change
{
  "mode": "Parallel",
  "steps": [
    { "targetKey": "httpbin", "method": "GET",  "path": "/uuid" },
    { "targetKey": "httpbin", "method": "POST", "path": "/anything", "body": { "hi": "there" } }
  ]
}
```

The response is one `DispatchResponse` with a per-step result (status code + body) for
each sub-request.

### Example 2 — change the wallpaper

```jsonc
POST /api/dispatches      // header: X-Api-Key: demo-key-please-change
{
  "mode": "Sequential",
  "steps": [
    { "targetKey": "wallpaper", "parameters": { "imageUrl": "https://picsum.photos/1920/1080" } }
  ]
}
```

### Example 3 — do it automatically

```jsonc
POST /api/schedules       // header: X-Api-Key: demo-key-please-change
{
  "name": "rotate-wallpaper",
  "intervalSeconds": 60,
  "dispatch": {
    "mode": "Sequential",
    "steps": [
      { "targetKey": "wallpaper", "parameters": { "imageUrl": "https://picsum.photos/1920/1080" } }
    ]
  }
}
```

The background scheduler fires it every 60 seconds; inspect the runs at `GET /api/dispatches`.

---

## How rules work

Rules are **deny-by-default**. On each step the engine takes the rules that apply to the
caller (their own rules plus any global rules), orders them by `priority` (ascending),
and the **first match wins**. A rule matches on target kind, a target-key glob
(`*`, `internal-*`, …), and — for HTTP targets — an HTTP-method glob. An `allow` rule may
also carry a per-minute rate limit. If nothing matches, the step is denied.

Example — let a user read any API but never write, at up to 100 req/min:

```json
{ "name": "reads only", "effect": "Allow", "priority": 100,
  "targetPattern": "*", "methodPattern": "GET", "maxRequestsPerMinute": 100 }
```

---

## Project structure

```
ApiRouter.sln
src/ApiRouter/
  Models/          Domain entities (User, Rule, Target, Dispatch, DispatchStep, Schedule)
  Data/            EF Core DbContext, seed data, and migrations
  Dtos/            Request/response contracts
  Auth/            API-key authentication handler
  Rules/           Rule engine, glob matcher, rate limiter
  Dispatching/     Dispatch executor (forwarding + action invocation)
  Actions/         Pluggable local actions (set-wallpaper)
  Scheduling/      Background service that fires schedules
  Controllers/     REST endpoints
  Program.cs       Composition root
tests/ApiRouter.Tests/   xUnit tests — unit (rule engine, glob, rate limiter, key hasher,
                         SSRF guard, status summarization) and integration
                         (WebApplicationFactory: auth, admin gating, dispatch, SSRF)
docs/requests.http       Runnable sample requests
```

Run the tests with:

```bash
dotnet test
```

---

## Notes & limitations

- **Storage** is SQLite via EF Core with **migrations** — `Database.Migrate()` runs on
  startup and the schema is versioned under `src/ApiRouter/Data/Migrations`. Evolve it with
  `dotnet ef migrations add <Name> --project src/ApiRouter` (see *Schema changes* below).
- **Rate limiting** is in-process (fine for a single instance); a multi-instance
  deployment would back it with a shared store such as Redis.
- **Wallpaper support** is OS-specific and best-effort per platform (Windows via
  `SystemParametersInfo`, Linux via `gsettings`/GNOME, macOS via `osascript`); non-GNOME
  Linux desktops would need their own command.
- **API keys are hashed (SHA-256) at rest** — the raw key is shown once at creation and
  never stored, so it can't be recovered or leaked from the database. Management endpoints
  require an admin key. **HTTPS is enforced** outside Development — HTTP requests are
  307-redirected to HTTPS and an HSTS header is sent — so keys aren't carried in cleartext.
- **Errors are RFC 7807 `ProblemDetails` JSON** — both unhandled exceptions (via
  `AddProblemDetails` + `UseExceptionHandler`, outside Development) and hand-written
  validation/conflict responses use the same shape, so the error contract is uniform.
- **Bootstrap admin key** — the demo key is used only in Development so the sample runs out
  of the box. Outside Development the app **fails closed**: it refuses to start unless
  `Bootstrap:AdminApiKey` is configured (env var `Bootstrap__AdminApiKey`), so the
  well-known demo key is never an admin credential in production.
- **SSRF protection** — the `set-wallpaper` action fetches a client-supplied URL, so it
  **validates the IP of every connection** — the initial host and each redirect hop — via
  `SocketsHttpHandler.ConnectCallback`, rejecting private, loopback, link-local, and
  cloud-metadata (`169.254.169.254`) addresses. Because the address actually connected to is
  the one validated, DNS rebinding is closed, and redirects can be safely followed (many image
  hosts 302 to a CDN). The download size is capped. The HTTP-forward path can't be repointed
  by clients (admin-registered host, client supplies only the path) and has redirects disabled.
- **Abuse limits** — a dispatch is capped at 25 steps, a user at 50 schedules, and list
  endpoints are bounded (`GET /api/dispatches` takes `skip`/`take`, max 100).
- **Swagger** is served in Development, or anywhere `Swagger:Enabled=true`, so the full API
  surface isn't published by default in Production.
- **Updates use `PATCH`** (partial merge — supplied fields overwrite, omitted fields are
  left unchanged), which is why the management verbs are `GET/POST/PATCH/DELETE`.

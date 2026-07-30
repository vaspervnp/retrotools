# RetroTools — Sprite & Spritemap Studio

> **Language:** English · [Ελληνικά](README.el.md)

A web tool for designing **sprites** and **spritemaps** for the 8-bit machines
**Amstrad CPC**, **Commodore 64** and **ZX Spectrum**, respecting each machine's
authentic constraints (hardware palettes, graphics modes, byte alignment,
attribute clash).

> **Status: complete end to end** — create a project, draw sprites or **import them
> from PNG**, organise them into groups and spritemaps, **export them as code that
> runs**, and take a **full JSON backup**.
> **392 tests green**, 82 of them against a real MariaDB.
> See [plan.md](plan.md) for the technical study and what remains.

---

## Documentation

| Document | What it covers |
|---|---|
| [**User manual**](docs/manual.md) | How to use the tool: platforms, editor, spritemaps, export |
| [OAuth setup](docs/oauth-setup.md) | Creating the GitHub and Google login keys, step by step |
| [plan.md](plan.md) | Hardware study, architecture, every design decision and why |

---

## What it does

- **Pixel editor** with zoom, drawing tools, undo/redo, frames and animation preview,
  and the correct **pixel aspect ratio** per mode (CPC Mode 0 pixels really *are* wide).
- **Hardware palettes**: 27 CPC colours (with programmable pens), 16 fixed C64 colours
  (Pepto), 15 ZX Spectrum colours (8 base × BRIGHT).
- **PNG import** that automatically picks the best-matching hardware colours and reports
  honestly what was lost: how many colours were rounded, how many cells exceeded the
  Spectrum's limit.
- **Hardware constraints are enforced**: the tool will not let you build a sprite that
  cannot run — byte alignment per mode, fixed dimensions for C64 hardware sprites,
  colour limits per mode.
- **Groups & spritemaps**: organise sprites into groups and grids (animation strips,
  tilesets, character sets).
- **Save / load** to MariaDB.
- **Export** to Z80 `defb` (rasm), 6502 (ACME), a `.prg` that loads in VICE, raw `.bin`,
  C headers and PNG. Each platform is offered only the formats that apply to it.
  The generated source carries comments with the palette's **hardware values** — what a
  programmer actually needs to set up the screen.
- **JSON backup**: an entire project (sprites, frames, palette, groups, spritemaps) in
  one `.retrotools.json` file that sits in git next to your game's source and reloads
  whenever you want.
- **Multi-user** with GitHub / Google sign-in; every project belongs to its owner.
- **Two tools for servers with no .NET installed**, as self-contained executables:
  [`retrotools-secrets`](#configuring-a-server-without-the-net-sdk) for settings (it also
  tests the real database connection) and [`retrotools-migrate`](#creating-the-schema) for
  migrations. Both replace SDK commands that do not exist in production.

## Supported platforms — summary

| | ZX Spectrum | Commodore 64 | Amstrad CPC |
|---|---|---|---|
| Palette | 15 colours | 16 fixed | 27 (16 selectable pens) |
| Modes | 256×192, 8×8 attributes | hires 320×200 · multicolor 160×200 | Mode 0 160×200/16 · Mode 1 320×200/4 · Mode 2 640×200/2 |
| Hardware sprites | — | 8 × 24×21 (hires) / 12×21 (multicolor) | — |
| Sprite alignment | width %8 | width %8 (HW: 24) | width %2 / %4 / %8 per mode |

For details (bit layouts, memory addresses, colour tables) see [plan.md §3](plan.md).

---

## Stack

- **C# 10** (`LangVersion 10.0`) on **.NET 10**
- **ASP.NET Core MVC** (site + REST API) + **Blazor** Interactive Server (editor)
- **HTML canvas** + a JS module for the per-pixel input loop
- **EF Core 9** + **Pomelo.EntityFrameworkCore.MySql 9.0.0** → **MariaDB 11**
- **Cookie auth + OAuth** (GitHub, Google) — no ASP.NET Identity
- **xUnit** for tests
- Self-hosted as a **Windows Service / systemd** unit behind a reverse proxy
- `retrotools-secrets` and `retrotools-migrate`: self-contained single-file CLIs for
  servers without .NET

> ⚠ The EF Core packages are **pinned to 9.0.x**. Pomelo has no build for EF Core 10;
> upgrading will break the provider at runtime. See [plan.md §2](plan.md).

---

## Quick start

### Prerequisites

- .NET SDK 10 ([download](https://dotnet.microsoft.com/download))
- Access to MariaDB 11 with a database dedicated to the application
- Git

### Install

```bash
git clone <repo-url> retrotools
```

```bash
cd retrotools && dotnet restore
```

### Configuring the database connection

The connection string is **never inside the repository**. Supply it in one of the
following ways (priority runs from bottom to top):

**1. User secrets (recommended for development)**

```bash
dotnet user-secrets set "ConnectionStrings:RetroTools" "Server=YOUR_HOST;Port=3306;Database=DB_NAME;User ID=YOUR_USER;Password=YOUR_PASSWORD;" --project src/RetroTools.Web
```

**2. Environment variable (for deployment)**

```bash
export ConnectionStrings__RetroTools="Server=YOUR_HOST;Port=3306;Database=DB_NAME;User ID=YOUR_USER;Password=YOUR_PASSWORD;"
```

**3. `appsettings.Local.json`** — copy `appsettings.Local.json.example` and fill in the
values. The file is in `.gitignore`.

If the connection string is missing, the application stops at startup with an explicit
message.

### Configuring GitHub / Google sign-in (optional in development)

The application has no passwords of its own — sign-in happens only through GitHub and
Google.

**➜ [Full instructions: docs/oauth-setup.md](docs/oauth-setup.md)** — step-by-step
creation of the OAuth applications, the correct callback URLs, a table of common errors
and what each one means, and how to rotate a leaked key.

Short version: the callback URLs are `/signin-github` and `/signin-google`, and the keys
live in four configuration keys:

```bash
dotnet user-secrets set "Authentication:GitHub:ClientId" "YOUR_ID" --project src/RetroTools.Web
```

```bash
dotnet user-secrets set "Authentication:GitHub:ClientSecret" "YOUR_SECRET" --project src/RetroTools.Web
```

The same for `Authentication:Google:ClientId` / `:ClientSecret`.
If they are missing, that provider simply does not appear — the application starts
normally.

#### Local sign-in without OAuth

To work on the UI without setting up OAuth applications, the route
`/account/dev/signin` signs you in as a local test user.

> ⚠️ **It requires two separate opt-ins:** the `Development` environment **and**
> `RetroTools:EnableDevSignIn = true` in `appsettings.Development.json` (which is
> gitignored). If either is missing, the route returns **404** as if it did not exist.
> Never enable this setting on a server.

### Configuring a server without the .NET SDK

`dotnet user-secrets` is an **SDK** command. A production server usually has no SDK —
and possibly not even the runtime, if the app is deployed self-contained. That is why
`retrotools-secrets` exists.

Publish it as a **single self-contained file** (the server needs nothing installed):

```bash
dotnet publish src/RetroTools.Secrets -c Release -r linux-x64 -o ./secrets-tool
```

Use `-r win-x64` for Windows. Copy the single executable to the server and run:

```bash
./retrotools-secrets set "ConnectionStrings:RetroTools"
```

With no value on the command line it reads from stdin — **so the password never lands in
your shell history**.

| Command | What it does |
|---|---|
| `path` | Where the settings file lives |
| `list` | All settings, with values masked (`--reveal` for the full values) |
| `set <key> [value]` | Set one; with no value, reads from stdin |
| `remove <key>` / `clear --force` | Delete |
| `import <file.json>` | Import from `appsettings.Local.json` — skips placeholders |
| `export-env` | Lines for a systemd `EnvironmentFile` |
| `check` | Is a required setting missing? Is an OAuth provider half-configured? |
| `test` | `check` **plus a real connection** to MariaDB |

`test` is the one that matters: a present connection string proves nothing — a wrong
password, a closed firewall or a wrong database name only show up this way.

Exit codes: `0` success, `1` usage error, `2` a setting is missing or the connection
failed — so it fits into a provisioning script.

If you prefer environment variables over a file:

```bash
./retrotools-secrets export-env > /etc/retrotools.env && chmod 600 /etc/retrotools.env
```

> The tool writes **the same file** as `dotnet user-secrets`, at the same path and in the
> same format — the two are interchangeable. On Linux it restricts the file to `0600`.

### Creating the schema

With the SDK:

```bash
dotnet ef database update --project src/RetroTools.Data --startup-project src/RetroTools.Web
```

**Without the SDK** — with `retrotools-migrate`, published self-contained just like
`retrotools-secrets`:

```bash
dotnet publish src/RetroTools.Migrator -c Release -r linux-x64 -o ./migrate-tool
```

| Command | What it does |
|---|---|
| `status` (default) | What is pending. Exit `0` up to date, `2` migrations pending |
| `list` | Every migration, applied ones marked |
| `up` | Apply; asks for confirmation, `--yes` for scripts |
| `up --create-database` | Also creates the database if missing, with utf8mb4 |
| `script --output x.sql` | Generates idempotent SQL instead of executing it |

```bash
./retrotools-migrate status
```

```bash
./retrotools-migrate up
```

The tool **refuses** to proceed if the database holds migrations this executable does
not know about — that means the database is newer than the code, typically a wrong build
or a half-finished rollback.

It also distinguishes the three connection failures, because each has a different fix:
unreachable server, missing database, or a database the user cannot access.

> Schema changes in MariaDB are **not transactional**: if something fails halfway, the
> database is left half-updated. Take a `mysqldump` first. The tool reminds you before
> it applies anything.
>
> If you would rather the application had no DDL rights, use
> `script --output schema.sql` and hand the SQL to your database administrator.

### Run

```bash
dotnet run --project src/RetroTools.Web
```

### Tests

```bash
dotnet test
```

Integration tests that need a database are **skipped** automatically when no connection
string is configured — they do not fail on CI without secrets.

---

## Deployment

Self-hosted as a service behind a reverse proxy (nginx / Apache / IIS ARR / Caddy).

### Order of operations on the server

1. **Publish** the application and both tools:
   ```bash
   dotnet publish src/RetroTools.Web -c Release -r linux-x64 --self-contained -o ./publish
   ```
   ```bash
   dotnet publish src/RetroTools.Secrets -c Release -r linux-x64 -o ./publish
   ```
2. **Configure the secrets** with `retrotools-secrets` — no SDK needed on the server:
   ```bash
   ./retrotools-secrets set "ConnectionStrings:RetroTools"
   ```
3. **Verify before starting the service**:
   ```bash
   ./retrotools-secrets test
   ```
   It returns `0` only if every required setting is present **and** the database
   answers — so it works as a precondition in a provisioning script.
4. **Apply the migrations** with `retrotools-migrate` — this needs no SDK either:
   ```bash
   ./retrotools-migrate status
   ```
   ```bash
   ./retrotools-migrate up
   ```
5. **Set up the service** and the reverse proxy using the settings in the table below.

Both tools read configuration in **the same priority order** (`--connection` →
environment variable → `--file` → user-secrets), so you configure once and use both.

Both also return **distinct exit codes**, so they chain in a script:

```bash
./retrotools-secrets test || exit 1
./retrotools-migrate status; [ $? -le 2 ] || exit 1
./retrotools-migrate up --yes
```

### Hosting settings

In the `RetroTools` section of `appsettings`:

| Setting | What it does |
|---|---|
| `BehindReverseProxy` | Enables `X-Forwarded-*` processing (**required**, or the OAuth callback breaks) |
| `KnownProxies` / `KnownNetworks` | Which proxies to trust (IP or CIDR). Without them the headers are ignored — they are spoofable |
| `TrustAnyProxy` | Bypasses the check above. Only if Kestrel is not exposed |
| `PathBase` | Hosting under a sub-path, e.g. `/spritestudio` |
| `EnableHttpsRedirection` | Set it to `false` when the proxy terminates TLS |

`UseWindowsService()` / `UseSystemd()` activate on their own when the application runs as
a service; from a console they are no-ops.

> **Blazor Server needs WebSockets.** The proxy must allow the upgrade (`Upgrade` /
> `Connection` headers in nginx), otherwise the editor falls back to long-polling.

---

## Repository layout

```
retrotools/
├─ src/
│  ├─ RetroTools.Core/     # palettes, modes, codecs, PNG, export — pure domain, no dependencies
│  ├─ RetroTools.Data/     # EF Core entities, DbContext, migrations
│  ├─ RetroTools.Web/      # MVC controllers, REST API, Blazor editor, wwwroot
│  ├─ RetroTools.Configuration/  # where secrets live — shared by the tools
│  ├─ RetroTools.Secrets/  # secrets CLI, for servers without the SDK
│  └─ RetroTools.Migrator/ # migrations CLI, for servers without the SDK
├─ tests/
├─ docs/
│  ├─ manual.md            # user manual
│  └─ oauth-setup.md       # creating the GitHub / Google keys
├─ plan.md                 # technical study + roadmap
└─ README.md
```

---

## Security & credentials

The repository **never contains**: connection strings, database hostnames, usernames or
passwords. The `.gitignore` entries that guarantee it:

```
appsettings.Local.json
appsettings.*.Local.json
appsettings.Development.json
.env
.env.*
secrets/
```

If a new setting with a secret is needed, it goes into user-secrets or an environment
variable — never into a committed file. The committed `*.example` files contain
**placeholders only**.

### Managing the secrets

| Environment | How |
|---|---|
| Development with the SDK | `dotnet user-secrets set …` |
| Server **without the SDK** | `retrotools-secrets set …` — [instructions](#configuring-a-server-without-the-net-sdk) |
| Container / systemd | Environment variables; `retrotools-secrets export-env` generates them |

Both tools write **the same file**, at the same path and in the same format, so they are
interchangeable.

> Be aware that **the user-secrets store is not encrypted** — not by the SDK either. The
> protection is that the file lives outside the project folder (so it never enters git)
> and that its permissions are restricted to the owner. `retrotools-secrets` enforces
> `0600` on Unix; without that the file is readable by every account on the machine. If
> you need real encryption at rest, use something like Vault or your OS secret store and
> pass the values in as environment variables.

---

## Roadmap

Phases M0 → M8, in detail in [plan.md §10](plan.md). In short:
setup → platform catalog → codecs → data layer → CRUD/API → pixel editor →
spritemaps → export/import → polish.

## Contributing

The code is written in **C# 10** — features from newer versions are not allowed (raw
string literals, `required` members, primary constructors, collection expressions).

## Licence

TBD.

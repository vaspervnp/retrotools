# RetroTools – Sprite / Spritemap Studio

> **Language:** English · [Ελληνικά](plan.el.md)

Implementation plan for a web tool for designing sprites & spritemaps for
**Amstrad CPC**, **Commodore 64** and **ZX Spectrum**.

- Document version: 1.1
- Date: 2026-07-29
- Status: **Approved** — the decisions in §15 have been incorporated. **M0 complete.**

---

## 1. Goal & scope

### 1.1 What the tool does

A web application where the user:

1. Creates a **project** by choosing a platform (CPC / C64 / Spectrum) and a graphics **mode**.
2. Draws **sprites** in a pixel editor with the platform's **authentic palette** and
   **authentic constraints** (colour count, byte alignment, attribute clash, pixel aspect ratio).
3. Organises sprites into **sprite groups / spritemaps** (sheets with rows and columns —
   animation strips, tilesets, character sets).
4. **Saves and loads** everything in MariaDB.
5. **Exports** to formats period assemblers accept directly (Z80 / 6502) as well as
   PNG/JSON, and **imports** from PNG/JSON.

### 1.2 Out of scope (v1)

- Full-screen bitmap / loading-screen editor (sprites & tiles only).
- Level map editor — spritemaps/tilesheets only.
- Emulator integration / live preview on real hardware.
- Multi-user real-time collaboration.
- ULAplus, Timex hi-colour, VDC, CPC Plus (ASIC 4096 colours, hardware sprites) — recorded
  as future extensions (see §14).

---

## 2. Technical stack & decisions

| Topic | Decision | Rationale |
|---|---|---|
| Language | **C# 10** (`<LangVersion>10.0</LangVersion>`) | Explicit user requirement |
| Target framework | **net10.0** | User's choice. SDK 10.0.301 / runtime 10.0.9 installed. |
| Web framework | **ASP.NET Core MVC + Blazor (Interactive Server)** | Explicit requirement. MVC for site/CRUD/API, Blazor for the editor. |
| ORM | **EF Core 9.0.x + Pomelo.EntityFrameworkCore.MySql 9.0.0**, pinned | Pomelo (the only mature MariaDB provider) **has no build for EF Core 10**. The EF 9 assemblies run fine on .NET 10 — confirmed by build plus live queries against MariaDB 11.4.3. |
| DB | **MariaDB 11.4.3** | Explicit requirement. Connection, DDL rights, utf8mb4 and BLOB round-trip all confirmed. The server's details stay outside the repository. |
| Rendering canvas | HTML `<canvas>` + JS module, with Blazor holding the authoritative model | Per-pixel drawing over Blazor Server would have unacceptable latency per mouse-move. |
| Auth | **Cookie auth + OAuth (GitHub, Google)**, our own `users` table. **Multi-user from the start.** | User's choice. **No ASP.NET Core Identity**: `Identity.EntityFrameworkCore` 10.x requires EF Core 10, which would break Pomelo 9. Without local passwords, Identity offers nothing here. |
| Hosting | Self-hosted service (**Windows Service / systemd**) behind a reverse proxy | User's choice. See §2.2. |
| Tests | xUnit | The codecs and palettes are critical. |

> **C# 10 note:** `LangVersion 10.0` forbids raw string literals (C#11), `required` members
> (C#11), primary constructors (C#12) and collection expressions (C#12). File-scoped
> namespaces, global usings and record structs are allowed. `LangVersion` is set centrally
> in `Directory.Build.props`, together with `TargetFramework`.

> **⚠ EF Core lock:** the `Pomelo.EntityFrameworkCore.MySql` and
> `Microsoft.EntityFrameworkCore.*` packages must stay on **9.0.x**. Upgrading to 10.x pulls
> in EF Core 10 and Pomelo 9 breaks at runtime (provider APIs change between majors). This
> unlocks only when a Pomelo build for EF Core 10 exists.

### 2.1 Repository layout

```
retrotools/
├─ RetroTools.sln
├─ src/
│  ├─ RetroTools.Core/          # Domain: palettes, modes, codecs, validation. No dependencies.
│  ├─ RetroTools.Data/          # EF Core: entities, DbContext, migrations, repositories
│  ├─ RetroTools.Web/           # MVC controllers + views + Blazor components + wwwroot
│  ├─ RetroTools.Configuration/ # where secrets live — shared, so the tools agree
│  ├─ RetroTools.Secrets/       # secrets CLI, self-contained for servers without the SDK
│  └─ RetroTools.Migrator/      # migrations CLI, self-contained
├─ tests/
├─ docs/
│  ├─ manual.md                 # user manual
│  └─ oauth-setup.md            # creating the GitHub / Google keys
├─ Directory.Build.props        # TargetFramework + LangVersion, centrally
├─ .gitignore
├─ plan.md
└─ README.md
```

### 2.2 Deployment model

Self-hosted **as a service**, on Windows or Linux, behind a reverse proxy.

- **Windows:** `sc.exe create` → the application runs with `UseWindowsService()`.
- **Linux:** a systemd unit file → `UseSystemd()`.
  Both calls are no-ops when run from a console, so development is unaffected.
- A **reverse proxy** (nginx / Apache / IIS ARR / Caddy) terminates TLS. The application:
  - reads `X-Forwarded-For` / `-Proto` / `-Host` via `UseForwardedHeaders()`,
  - accepts those headers **only** from explicitly declared proxies (`KnownProxies` /
    `KnownNetworks` in CIDR form) — otherwise the header is spoofable,
  - supports `PathBase` for hosting under a sub-path (e.g. `/spritestudio`),
  - disables its internal HTTPS redirect (`EnableHttpsRedirection: false`) since the proxy
    does it.
- **Critical for OAuth:** without correct forwarded headers the redirect URIs come out as
  `http://` and the GitHub/Google callbacks fail.
- **Migrations without the SDK:** `dotnet ef` needs the SDK, but only to *create*
  migrations. Applying them needs nothing but the EF Core runtime, so `RetroTools.Migrator`
  carries it inside and publishes self-contained. It refuses to proceed if the database
  holds migrations it does not know about — a sign the database is newer than the code.
  It distinguishes an unreachable server from a missing database from a database without
  permissions, because the three have different fixes and a bare "cannot connect" helps
  with none of them. Creating a database requires an explicit `--create-database`: a typo
  in the name must not silently produce an empty database where everything "works".
- **WebSockets:** Blazor Server needs a WebSocket upgrade at the proxy
  (`proxy_set_header Upgrade/Connection` in nginx), otherwise it falls back to long-polling
  with noticeable latency in the editor.
- **Data protection keys** must persist to disk (or to the database), otherwise every
  restart invalidates the auth cookies.
- Ready-made samples (systemd unit, nginx site, Windows service) will go into `docs/deploy/`.

---

## 3. Platform study

This is the **heart** of the tool: every number here becomes data in `PlatformCatalog`
inside `RetroTools.Core`.

### 3.1 ZX Spectrum (48K/128K)

#### Resolution & colour
- Screen **256 × 192** pixels.
- **There is no per-pixel colour.** The screen is divided into **32 × 24 attribute cells of
  8×8 pixels**.
- Each cell has **one** attribute byte:

| Bit | 7 | 6 | 5–3 | 2–0 |
|---|---|---|---|---|
| Meaning | FLASH | BRIGHT | PAPER (0–7) | INK (0–7) |

- So **at most 2 colours per 8×8 cell**, and BRIGHT applies to **both** of them together.
  This is the famous **attribute clash**.
- Palette: 8 base colours in **GRB** bit order (bit0=Blue, bit1=Red, bit2=Green) × 2
  brightness levels = **15 unique colours** (bright black = black).

| # | Name | Normal | Bright |
|---|---|---|---|
| 0 | Black | `#000000` | `#000000` |
| 1 | Blue | `#0000D8` | `#0000FF` |
| 2 | Red | `#D80000` | `#FF0000` |
| 3 | Magenta | `#D800D8` | `#FF00FF` |
| 4 | Green | `#00D800` | `#00FF00` |
| 5 | Cyan | `#00D8D8` | `#00FFFF` |
| 6 | Yellow | `#D8D800` | `#FFFF00` |
| 7 | White | `#D8D8D8` | `#FFFFFF` |

> The non-bright level is about 85% of full voltage. The literature gives it as either
> `0xD8` (Lospec) or `0xD7` (Fuse and others). Implemented as a **selectable palette
> profile** (`D8` default, `D7` alternative) so the preview matches the user's emulator.

#### Sprites
- **No hardware sprites.** Everything is a software sprite, drawn at a byte-aligned width.
- Practical editor constraints:
  - Width: a **multiple of 8** (1 byte = 8 pixels). Allowed: 8, 16, 24, 32, 48, 64.
  - Height: free in pixels (typically 8, 16, 21, 24, 32).
  - Optional **mask** (AND mask + OR data) for transparency — a second bitplane of the same
    dimensions.
- Sprite colour: either **monochrome + attribute** per cell (the classic approach), or a
  "colour sprite" where the tool keeps a separate attribute grid of `ceil(w/8) × ceil(h/8)`.

#### Memory (for export)
- Bitmap: 6144 bytes at `0x4000`, non-linear layout in 3 thirds:
  ```
  addr = 0x4000 + ((y & 0xC0) << 5) + ((y & 0x07) << 8) + ((y & 0x38) << 2) + x_byte
  ```
- Attributes: 768 bytes at `0x5800`, linear: `0x5800 + (y >> 3) * 32 + x_byte`.
- The tool exports **linearly** (row by row, friendly to blitter routines) **and**
  optionally in screen-layout order.

#### Pixel aspect ratio
1 : 1 (square pixels).

---

### 3.2 Commodore 64

#### Colour
- VIC-II with a **fixed palette of 16 colours** (it cannot change — there is no
  programmable palette).
- We use the **Pepto palette** (the de-facto standard, calculated from analysis of the
  VIC-II):

| # | Name | Hex | | # | Name | Hex |
|---|---|---|---|---|---|---|
| 0 | Black | `#000000` | | 8 | Orange | `#6F4F25` |
| 1 | White | `#FFFFFF` | | 9 | Brown | `#433900` |
| 2 | Red | `#68372B` | | 10 | Light Red | `#9A6759` |
| 3 | Cyan | `#70A4B2` | | 11 | Dark Grey | `#444444` |
| 4 | Purple | `#6F3D86` | | 12 | Grey | `#6C6C6C` |
| 5 | Green | `#588D43` | | 13 | Light Green | `#9AD284` |
| 6 | Blue | `#352879` | | 14 | Light Blue | `#6C5EB5` |
| 7 | Yellow | `#B8C76F` | | 15 | Light Grey | `#959595` |

> Alternative palette profiles (Colodore, VICE "Pepto NTSC") are provided for as a display
> setting — the data is always stored as indices 0–15.

#### Hardware sprites (MOBs)
The **only** one of the three platforms with real hardware sprites.

| Property | Hi-res | Multicolor |
|---|---|---|
| Dimensions | **24 × 21** pixels | **12 × 21** (double-width pixels → 24 screen pixels) |
| Bits/pixel | 1 | 2 |
| Data size | 63 bytes (3 × 21), in a 64-byte block | same |
| Colours | 1 + transparency | 3 + transparency |

- **Count:** 8 simultaneously (0–7), max 8 per raster line without multiplexing.
- **Multicolor colours:**
  | Bit pair | Colour source |
  |---|---|
  | `00` | Transparent (the background shows) |
  | `01` | `$D025` — Sprite Multicolor 0 (**shared by all sprites**) |
  | `10` | `$D027+n` — this sprite's own colour |
  | `11` | `$D026` — Sprite Multicolor 1 (**shared by all sprites**) |
- **Expansion:** X (`$D01D`) and/or Y (`$D017`) → displayed as 48×21, 24×42 or 48×42 (the
  data stays 24×21).
- **Sprite pointers:** a byte at `screen_base + $03F8 + n`, value = `data_address / 64`.
- The tool keeps **shared palette slots** per project (MC0/MC1) and a per-sprite colour —
  exactly like the hardware.

#### Char / bitmap "sprites" (tiles)
| Mode | Resolution | Colours |
|---|---|---|
| Standard text | 40×25 chars (8×8) | 1 colour/char + shared background |
| Multicolor text | 40×25 (4×8 double-width pixels) | 3 shared + 1 per-char (from 0–7) |
| Hi-res bitmap | 320×200 | 2 colours per 8×8 cell |
| Multicolor bitmap | 160×200 (double-width pixels) | 4 per 8×8 cell: `00`=$D021 shared, `01`=screen RAM hi-nibble, `10`=screen RAM lo-nibble, `11`=Colour RAM |

#### Pixel aspect ratio
Hi-res 1:1 · Multicolor 2:1 (wide pixels).

---

### 3.3 Amstrad CPC (464 / 664 / 6128)

#### Colour
- **27 colours**: 3 levels (0% / 50% / 100%) × 3 RGB channels = 3³ = 27.
- The Gate Array accepts **32 hardware ink values (`0x40`–`0x5F`)** which map onto the 27
  firmware colours (5 duplicates).

Full table (firmware # ↔ hardware value ↔ RGB at 0/128/255):

| FW# | Name | R,G,B % | Hex | HW value(s) |
|---|---|---|---|---|
| 0 | Black | 0,0,0 | `#000000` | `0x54` |
| 1 | Blue | 0,0,50 | `#000080` | `0x44`, `0x50` |
| 2 | Bright Blue | 0,0,100 | `#0000FF` | `0x55` |
| 3 | Red | 50,0,0 | `#800000` | `0x5C` |
| 4 | Magenta | 50,0,50 | `#800080` | `0x58` |
| 5 | Mauve | 50,0,100 | `#8000FF` | `0x5D` |
| 6 | Bright Red | 100,0,0 | `#FF0000` | `0x4C` |
| 7 | Purple | 100,0,50 | `#FF0080` | `0x45`, `0x48` |
| 8 | Bright Magenta | 100,0,100 | `#FF00FF` | `0x4D` |
| 9 | Green | 0,50,0 | `#008000` | `0x56` |
| 10 | Cyan | 0,50,50 | `#008080` | `0x46` |
| 11 | Sky Blue | 0,50,100 | `#0080FF` | `0x57` |
| 12 | Yellow | 50,50,0 | `#808000` | `0x5E` |
| 13 | White | 50,50,50 | `#808080` | `0x40`, `0x41` |
| 14 | Pastel Blue | 50,50,100 | `#8080FF` | `0x5F` |
| 15 | Orange | 100,50,0 | `#FF8000` | `0x4E` |
| 16 | Pink | 100,50,50 | `#FF8080` | `0x47` |
| 17 | Pastel Magenta | 100,50,100 | `#FF80FF` | `0x4F` |
| 18 | Bright Green | 0,100,0 | `#00FF00` | `0x52` |
| 19 | Sea Green | 0,100,50 | `#00FF80` | `0x42`, `0x51` |
| 20 | Bright Cyan | 0,100,100 | `#00FFFF` | `0x53` |
| 21 | Lime | 50,100,0 | `#80FF00` | `0x5A` |
| 22 | Pastel Green | 50,100,50 | `#80FF80` | `0x59` |
| 23 | Pastel Cyan | 50,100,100 | `#80FFFF` | `0x5B` |
| 24 | Bright Yellow | 100,100,0 | `#FFFF00` | `0x4A` |
| 25 | Pastel Yellow | 100,100,50 | `#FFFF80` | `0x43`, `0x49` |
| 26 | Bright White | 100,100,100 | `#FFFFFF` | `0x4B` |

> On real hardware the "50%" measures closer to ~40% of full voltage. Two palette profiles
> exist: **"Nominal"** (0/128/255, default) and **"Measured"** (a darker mid-level), for
> display only.

#### Modes
| Mode | Resolution | Pens | Bits/pixel | Pixels/byte | Aspect |
|---|---|---|---|---|---|
| 0 | 160 × 200 | 16 | 4 | 2 | 2:1 (wide) |
| 1 | 320 × 200 | 4 | 2 | 4 | 1:1 |
| 2 | 640 × 200 | 2 | 1 | 8 | 1:2 (narrow) |
| 3 (undocumented) | 160 × 200 | 4 | 4 (only 2 useful) | 2 | 2:1 |

- The screen palette has **16 pens** (0–15) + **1 border ink**. Each pen points at one of
  the 27 colours. Mode 1 uses pens 0–3, Mode 2 pens 0–1.
- **Flashing ink** (alternating two colours) is supported — an optional field in the palette.

#### Pixel encoding (critical for export)
The CPC has an interleaved bit layout inside the byte:

- **Mode 0** — 2 pixels/byte, `A` = left, `B` = right, `bN` = bit N of the pen value (0–15):
  ```
  bit7 bit6 bit5 bit4 bit3 bit2 bit1 bit0
  A.b0 B.b0 A.b2 B.b2 A.b1 B.b1 A.b3 B.b3
  ```
- **Mode 1** — 4 pixels/byte (`A`..`D` from the left):
  ```
  A.b0 B.b0 C.b0 D.b0 A.b1 B.b1 C.b1 D.b1
  ```
- **Mode 2** — 8 pixels/byte, straight: bit7 = leftmost pixel.

> **Unified rule (implemented in M2):** all three modes follow one formula. Bit `k` of the
> pen of a pixel at position `p` inside the byte goes to bit `BitPositions[k] − p`, with
> `BitPositions = { 7, 3, 5, 1 }`. Mode 1 uses the first two, Mode 2 only the first (and so
> degenerates into plain MSB-first). This replaces three separate implementations with one,
> and is verified exhaustively (256 combinations per mode) against the explicit formula from
> the documentation.

#### Sprites
- **No hardware sprites** (except on the CPC Plus). Software sprites with **byte alignment**:
  | Mode | Width must be a multiple of |
  |---|---|
  | 0 | **2** pixels |
  | 1 | **4** pixels |
  | 2 | **8** pixels |
- Height is free. Typical Mode 0 sizes: 4×16, 8×16, 16×16, 16×24, 32×32.
- Optional mask for transparency.

#### Memory (for export)
- 16 KB at `0xC000` (default), 80 bytes per row, with 8 interleaved "banks":
  ```
  addr = base + ((y & 7) * 0x800) + ((y >> 3) * 0x50) + x_byte
  ```

---

### 3.4 Comparison table

| | ZX Spectrum | Commodore 64 | Amstrad CPC |
|---|---|---|---|
| Hardware palette | 15 colours (8×2 bright) | 16 fixed | 27 |
| Programmable palette | No | No | **Yes** (16 pens out of 27) |
| Simultaneous colours (sprite area) | 2 per 8×8 cell | 4 per sprite (MC) | 16 (Mode 0) |
| Hardware sprites | No | **Yes** (8 × 24×21) | No |
| Resolution | 256×192 | 320×200 / 160×200 | 160/320/640 × 200 |
| Attribute clash | **Yes** (severe) | Yes (in bitmap/char modes) | **No** |
| Sprite byte alignment | 8 px | 8 px (24 for HW) | 2 / 4 / 8 px |
| CPU / assembler export | Z80 (`defb`) | 6502 (`.byte`) | Z80 (`defb`) |

---

## 4. Domain model

```
Project (1 platform + 1 mode)
 ├── Palette          ← which hardware colour each slot/pen points at
 ├── Sprite *         ← w × h, N frames
 │    └── SpriteFrame *   ← pixel indices + (Spectrum) attributes + (optional) mask
 ├── SpriteGroup *    ← a logical group (e.g. "Player", "Enemies")
 └── SpriteMap *      ← a cols × rows grid of cells
      └── SpriteMapCell * ← a reference to a sprite/frame + flags (flipX, flipY, priority)
```

### 4.1 Pixel data representation

- Each frame is stored as an **indexed buffer: 1 byte per pixel** = an index into the
  palette slot (0–15).
  - Simple, mode-independent, easy to undo/redo and diff.
  - Conversion to packed hardware bytes happens **only at export**, in the codecs.
- Size: a 64×64 sprite is 4 KB → comfortable for a `MEDIUMBLOB`. **Deflate** is applied
  before storage (typically >90% compression on pixel art).
- An `RSPR` container format (16-byte header: magic, version, w, h, encoding, flags) so the
  encoding can change in future without a migration.

### 4.2 Attributes (Spectrum)

A separate buffer of `ceil(w/8) × ceil(h/8)` bytes, one attribute byte per cell (the same
bit layout as the hardware).

---

## 5. Database (MariaDB 11)

Charset `utf8mb4`, collation `utf8mb4_unicode_ci`, engine InnoDB.
No table prefix (the database is dedicated to the application).

### 5.1 Schema (DDL sketch)

```sql
CREATE TABLE platforms (
  code        VARCHAR(16)  NOT NULL PRIMARY KEY,   -- 'cpc' | 'c64' | 'zx'
  name        VARCHAR(64)  NOT NULL,
  color_count SMALLINT     NOT NULL
);

CREATE TABLE platform_modes (
  id            INT AUTO_INCREMENT PRIMARY KEY,
  platform_code VARCHAR(16) NOT NULL,
  code          VARCHAR(32) NOT NULL,     -- 'mode0','mc_sprite','hires','attr8x8'
  name          VARCHAR(64) NOT NULL,
  width, height SMALLINT NOT NULL,
  max_colors    SMALLINT NOT NULL,
  bits_per_pixel TINYINT  NOT NULL,
  width_align    TINYINT  NOT NULL,       -- 2 / 4 / 8 / 24
  height_align   TINYINT  NOT NULL,
  pixel_ar_num, pixel_ar_den TINYINT NOT NULL,
  UNIQUE KEY (platform_code, code),
  FOREIGN KEY (platform_code) REFERENCES platforms(code)
);

-- Multi-user from the start. No ASP.NET Identity, no local passwords:
-- authentication happens exclusively through GitHub / Google OAuth.
CREATE TABLE users (
  id            CHAR(36)     NOT NULL PRIMARY KEY,   -- our own stable GUID
  display_name  VARCHAR(128) NOT NULL,
  email         VARCHAR(256) NULL,
  avatar_url    VARCHAR(512) NULL,
  created_utc   DATETIME(3)  NOT NULL,
  last_login_utc DATETIME(3) NULL,
  is_disabled   TINYINT(1)   NOT NULL DEFAULT 0
);

-- One user can link both GitHub and Google to the same account.
CREATE TABLE user_logins (
  provider      VARCHAR(32)  NOT NULL,               -- 'github' | 'google'
  provider_key  VARCHAR(128) NOT NULL,               -- the provider's stable subject id
  user_id       CHAR(36)     NOT NULL,
  linked_utc    DATETIME(3)  NOT NULL,
  PRIMARY KEY (provider, provider_key),
  KEY (user_id),
  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE projects (
  id            BIGINT AUTO_INCREMENT PRIMARY KEY,
  owner_id      CHAR(36) NOT NULL,          -- multi-user: every project belongs to a user
  visibility    TINYINT NOT NULL DEFAULT 0, -- 0=private, 1=unlisted, 2=public (read-only)
  name          VARCHAR(128) NOT NULL,
  description   VARCHAR(1024) NULL,
  platform_code VARCHAR(16) NOT NULL,
  mode_id       INT NOT NULL,
  created_utc   DATETIME(3) NOT NULL,
  updated_utc   DATETIME(3) NOT NULL,
  row_version   BIGINT NOT NULL DEFAULT 0,      -- optimistic concurrency
  KEY (owner_id), KEY (platform_code),
  FOREIGN KEY (owner_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE palettes (
  id         BIGINT AUTO_INCREMENT PRIMARY KEY,
  project_id BIGINT NULL,          -- NULL = system preset
  name       VARCHAR(64) NOT NULL,
  is_system  TINYINT(1) NOT NULL DEFAULT 0
);

CREATE TABLE palette_entries (
  palette_id     BIGINT NOT NULL,
  slot_index     TINYINT NOT NULL,   -- pen / colour register
  hw_color_index SMALLINT NOT NULL,  -- 0..26 (CPC) | 0..15 (C64) | 0..15 (ZX ink+bright)
  role           VARCHAR(16) NULL,   -- 'background','border','mc0','mc1','transparent'
  PRIMARY KEY (palette_id, slot_index)
);

CREATE TABLE sprite_groups (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  project_id BIGINT NOT NULL,
  name VARCHAR(128) NOT NULL,
  sort_order INT NOT NULL DEFAULT 0
);

CREATE TABLE sprites (
  id         BIGINT AUTO_INCREMENT PRIMARY KEY,
  project_id BIGINT NOT NULL,
  group_id   BIGINT NULL,
  name       VARCHAR(128) NOT NULL,
  width_px   SMALLINT NOT NULL,
  height_px  SMALLINT NOT NULL,
  palette_id BIGINT NULL,
  has_mask   TINYINT(1) NOT NULL DEFAULT 0,
  meta_json  JSON NULL,             -- e.g. C64: sprite colour, expandX/Y, multicolor
  sort_order INT NOT NULL DEFAULT 0,
  created_utc, updated_utc DATETIME(3) NOT NULL,
  KEY (project_id), KEY (group_id)
);

CREATE TABLE sprite_frames (
  id          BIGINT AUTO_INCREMENT PRIMARY KEY,
  sprite_id   BIGINT NOT NULL,
  frame_index SMALLINT NOT NULL,
  duration_ms SMALLINT NOT NULL DEFAULT 100,
  pixel_data  MEDIUMBLOB NOT NULL,   -- RSPR container, deflate
  attr_data   MEDIUMBLOB NULL,       -- ZX attributes
  mask_data   MEDIUMBLOB NULL,
  UNIQUE KEY (sprite_id, frame_index)
);

CREATE TABLE spritemaps (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  project_id BIGINT NOT NULL,
  name VARCHAR(128) NOT NULL,
  cols SMALLINT NOT NULL,
  rows SMALLINT NOT NULL,
  cell_width_px  SMALLINT NOT NULL,
  cell_height_px SMALLINT NOT NULL,
  created_utc, updated_utc DATETIME(3) NOT NULL
);

CREATE TABLE spritemap_cells (
  spritemap_id BIGINT NOT NULL,
  col SMALLINT NOT NULL,
  row SMALLINT NOT NULL,
  sprite_id BIGINT NULL,
  frame_index SMALLINT NOT NULL DEFAULT 0,
  flags TINYINT NOT NULL DEFAULT 0,   -- bit0 flipX, bit1 flipY
  PRIMARY KEY (spritemap_id, col, row)
);
```

All foreign keys cascade downwards from `projects` with `ON DELETE CASCADE`.

**Ownership enforcement (multi-user):** every query against `sprites` / `spritemaps` /
`palettes` necessarily goes through `project_id` → `owner_id`. Implemented as an **EF Core
global query filter** on `projects`
(`p => p.OwnerId == _currentUser.Id || p.Visibility == Public`), so a forgotten `Where`
cannot leak another user's data. API endpoints that accept an `id` also check explicitly and
return **404 (not 403)** for foreign objects, so their existence is never disclosed.

### 5.2 Migrations

- EF Core migrations (`dotnet ef migrations add`), not hand-written SQL. ✅ `InitialSchema`
  applied.
- `platforms` / `platform_modes` are filled by a **runtime seed** from `PlatformCatalog` on
  every startup, not by `HasData`. That way correcting the hardware data needs no new
  migration.
- **Migrations are not applied automatically.** Startup checks whether any are pending and
  logs a warning with the command to run. `Database.Migrate()` on every startup would be
  dangerous in production and would create a race between multiple instances behind the
  reverse proxy.
- The server version is declared explicitly (`MariaDbServerVersion 11.4`) rather than
  `AutoDetect`, which would open a connection at startup and prevent the application from
  starting when the database is briefly down.

---

## 6. Credential management (requirement: **nothing in git**)

A three-tier strategy, in priority order:

1. **`dotnet user-secrets`** (primary, for development). ✅ *implemented*
   `RetroTools.Web.csproj` carries a `<UserSecretsId>`; the secrets are stored at
   `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` — **outside the repository**.
   `RetroTools.Data.Tests.csproj` shares the same `UserSecretsId`, so the integration tests
   find the same connection string without duplication.
   ```bash
   dotnet user-secrets set "ConnectionStrings:RetroTools" "Server=...;Port=3306;Database=DB_NAME;User ID=...;Password=...;" --project src/RetroTools.Web
   ```
2. **Environment variables** (for deployment): `ConnectionStrings__RetroTools`.
3. **`appsettings.Local.json`** (fallback), explicitly added to `.gitignore`.

### 6.1 Which secrets exist

| Configuration key | What it is |
|---|---|
| `ConnectionStrings:RetroTools` | MariaDB host, database, user, password |
| `Authentication:GitHub:ClientId` / `:ClientSecret` | GitHub OAuth App |
| `Authentication:Google:ClientId` / `:ClientSecret` | Google Cloud OAuth 2.0 Client |

All three pairs go **exclusively** into user-secrets or environment variables. If the OAuth
keys are missing, that provider is simply **not registered** — the application starts
normally and shows only the available sign-in buttons.

### 6.1.1 Configuring a server without the SDK

`dotnet user-secrets` is an **SDK** command, and a production server does not have the SDK —
and if the application runs self-contained, not even the runtime.

`RetroTools.Secrets` fills the gap. The user-secrets store is not exotic: it is a JSON file
with flat keys, at a path defined by the operating system
(`%APPDATA%\Microsoft\UserSecrets\<id>\` or `~/.microsoft/usersecrets/<id>/`). The tool
writes **exactly the same file**, so the two tools are interchangeable.

Design decisions:

| Decision | Why |
|---|---|
| Publishes as a **self-contained single file** | The server may have no .NET at all; a 37 MB executable is copied and runs |
| `MySqlConnector` only, no EF Core | The tool wants to open a connection, not map entities |
| `set` with no value reads from **stdin** | The password does not land in shell history |
| Values are **masked** in output | A server console often ends up in logs or session recordings |
| `0600` on the file on Unix | Without it, every account on the machine can read it |
| `import` **skips placeholders** | A `Password=DB_PASSWORD` must not become a real setting |
| OAuth checked **in pairs** | A ClientId without a ClientSecret is not half a configuration — the provider silently disappears |
| Distinct exit codes (0/1/2) | It fits into a provisioning script |

`test` does not merely check whether a connection string exists — it **opens a real
connection**: a wrong password, a closed firewall or a wrong database name only show up that
way.

### 6.1.2 Applying migrations without the SDK

`RetroTools.Migrator` does the same for the schema. See §2.2 for the safety decisions:
refusing unknown migrations, distinguishing the three connection failures, and requiring
`--create-database` explicitly.

### 6.2 Guarantees

- ✅ The committed `appsettings.json` contains **no** connection string and no OAuth keys.
- ✅ A committed `appsettings.Local.json.example` template with **placeholders only**.
- ✅ A `.gitignore` with an explicit, commented section for secrets — created **before** any
  commit.
- ✅ The application **fails with a clear message** at startup if the connection string is
  missing (`ConnectionStringProvider.Require`), rather than a `NullReferenceException` on the
  first query.
- ✅ The logs print **only the database hostname**, never the whole connection string.
- A pre-commit check (optional): grep `git diff --cached` for `Password=`.

### 6.3 Connection status — verified ✅

The smoke tests in `RetroTools.Data.Tests` (3/3 passed) confirmed:

| Check | Result |
|---|---|
| Connection & version | `11.4.3-MariaDB-deb11` |
| Current database | the dedicated application database |
| Server charset | `utf8mb4` |
| DDL rights | CREATE / INSERT / SELECT / DROP TABLE OK |
| `MEDIUMBLOB` round-trip | OK (binary unaltered) |
| UTF-8 Greek text | OK |
| EF Core 9 provider on .NET 10 | OK |

---

## 7. Application architecture

### 7.1 `RetroTools.Core` (no dependencies)

| Namespace | Contents |
|---|---|
| `Platforms` | `PlatformDefinition`, `GraphicsMode`, `SpriteSizeRule`, `PixelAspect`, `PixelSlot`/`PixelSlotRole`, `PlatformCatalog` (static, the data from §3) |
| `Palettes` | `Rgb24`, `HardwareColor`, `PaletteProfile` (ZX D8/D7, C64 Pepto, CPC Nominal/Measured), `HardwarePalette` with reverse hardware→index lookup and nearest-colour in linear RGB |
| `Model` | `FrameBuffer` (indexed, 1 byte/pixel), `AttributeGrid` (ZX) |
| `Imaging` | `PngWriter` — indexed PNG (colour type 3) written from scratch; serves both the UI thumbnails and the PNG export of §8, with no imaging-framework dependency. `PngReader` + `ImageQuantizer` for import |
| `Codecs` | `ISpriteCodec`, `SpriteCodecBase`, `CpcInterleavedCodec` (Mode 0/1/2/3), `LinearSpriteCodec` (C64 & ZX, MSB-first), `MaskCodec`, `SpriteCodecs` factory |
| `Export` | `Z80AsmExporter`, `Acme6502Exporter`, `CHeaderExporter`, `BinaryExporter`, `PrgExporter`, `PngExporter`, `SpriteExporters` registry |
| `Serialization` | `RsprContainer` (read/write, deflate), `ProjectDocument` + validator + serializer |

**`Core` carries the densest unit-test coverage** — round-trip tests `encode(decode(x)) == x`
for every mode.

### 7.2 `RetroTools.Data`

- `RetroToolsDbContext`, entity configurations (Fluent API), unit-of-work through the
  `DbContext`.
- Optimistic concurrency via `row_version` on `projects` / `sprites` / `spritemaps`.

### 7.3 `RetroTools.Web`

**MVC part** (controllers):

| Endpoint | Methods |
|---|---|
| `/api/platforms` | GET (catalog: modes, palettes, constraints — feeds the editor) |
| `/api/projects` `/api/projects/{id}` | GET, POST, PUT, DELETE |
| `/api/projects/{id}/sprites` | GET, POST |
| `/api/sprites/{id}` | GET, PUT, DELETE |
| `/api/sprites/{id}/frames/{index}` | GET, PUT (pixel buffer) |
| `/api/spritemaps` `/api/spritemaps/{id}` | GET, POST, PUT, DELETE |
| `/api/export/sprite/{id}?format=` | GET (`bin`, `asm-z80`, `asm-6502`, `prg`, `c`, `png`) |
| `/api/projects/{id}/document` · `/api/projects/import` | JSON project export / import |
| `/api/projects/{id}/sprites/import-png` | POST (PNG import with quantization) |
| `/signin/{provider}` `/signout` `/signin-github` `/signin-google` | OAuth challenge & callbacks |
| `/api/me` | GET (current user, linked providers) |

Every `/api/*` route requires an authenticated user except `/api/platforms` (static hardware
data).

**Blazor part** (Interactive Server, per-page render mode):
- `/editor/sprite/{id}` — the pixel editor
- `/editor/spritemap/{id}` — the spritemap composer

### 7.4 The pixel editor (design detail)

The critical performance point. The design:

- A JS module `wwwroot/js/pixel-canvas.js` owns the `<canvas>`, an offscreen `ImageData`
  holding the indexed buffer, the zoom/pan and the mouse events.
- The JS draws **immediately and locally** (zero latency) and sends the Blazor component
  **batched deltas** on pointer-up.
- The Blazor component holds the **authoritative** `FrameBuffer`, the undo/redo stack
  (command pattern) and autosaves (debounced, ~1.5 s).
- A palette/mode change → the server sends a new LUT to the JS and it redraws without
  touching the pixel data.

**Editor tools:** pencil, eraser, flood fill, line, rectangle (outline/filled), colour
picker, grid overlays (pixel + 8×8 cell), zoom 2×–32×.

**Panels:** palette picker (with a hardware colour chooser for CPC), frame timeline, sprite
list, export buttons.

### 7.4.1 Traps found during implementation (M5)

Three mistakes that are invisible in the code but break in practice — recorded so they are
not repeated:

1. **`OnAfterRenderAsync(firstRender: true)` runs before the data loads.** With an async
   `OnInitializedAsync`, the first render happens while the sprite is still `null` and the
   canvas is not in the DOM. The usual `if (!firstRender) return;` guard then blocks
   initialisation **forever**. The fix: initialise on the first render where both the data
   and the element exist.
2. **`byte[]` does not cross from JS to .NET as a plain array.** Blazor treats byte arrays
   specially (it expects a reference to a binary transfer), so a `[1,2,3]` from JavaScript
   fails deserialisation **with no console error** — the stroke simply never arrived. The
   `[JSInvokable]` signatures use `int[]` and convert in C#.
3. **`DbContextOptions` is scoped.** A singleton service consuming it takes the application
   down at startup. `EditorDataService` is scoped.

### 7.5 Authentication — why accounts are not linked automatically

The obvious thing would be: if someone signs in with GitHub and their email matches an
existing Google account, merge them. **We do not**, and that is a deliberate security
decision.

Auto-linking by email is a well-known account-takeover route: if a provider returns an email
it has not verified, someone only has to claim the victim's email to gain access to all
their projects. GitHub does not expose verification status on the basic endpoint, so we
cannot even check it reliably.

Instead:

| Situation | Behaviour |
|---|---|
| The login already exists (provider + key) | Normal sign-in, profile refresh |
| Unknown login, unknown email | New account |
| Unknown login, **known email** | **Abort** — the user is sent to `/account/link-required` with instructions to sign in with the original provider and link the second one from settings |

Linking a second provider happens **only** from an already signed-in user
(`UserProvisioningService.LinkAsync`), where identity is proven.

Four more timing and security details:

- Provisioning runs in **`OnTicketReceived`**, not `OnCreatingTicket`: we must be able to
  abort the sign-in **before** a cookie is issued. Otherwise the user would be left
  authenticated with no matching account — seeing an empty application with no explanation.
- **Sign-out is POST only**, with an antiforgery token. A GET `/signout` can be triggered by
  an `<img src>` on a foreign page and throw the user out.
- `returnUrl` goes through `Url.IsLocalUrl` — otherwise it is an open redirect, a
  ready-made phishing tool with our domain as the bait.
- Neither provider maps the profile picture by default; the claim actions for `avatar_url`
  (GitHub) and `picture` (Google) are declared explicitly.

### 7.6 Read ≠ write (the query-filter trap)

The global query filters of §5.1 let **public** projects through — correct for reading. But
if a write path used the same query, every public project would become **writable by
anyone**: the filter would return it and `SaveChanges` would go through.

That is why [`ProjectAccess`](src/RetroTools.Web/Services/ProjectAccess.cs) has two separate
families of methods:

| Method | What it returns |
|---|---|
| `FindReadable*` | Mine **and** public — relies on the query filters |
| `FindWritable*` | **Only** mine — an explicit `OwnerId == currentUser` check |

Every `POST` / `PUT` / `DELETE` necessarily goes through `FindWritable*`. Covered by a test
that publishes a project, confirms another user can read it, and that `PUT` and `DELETE`
return 404 for them.

**Why 404 and not 403:** a 403 would confirm that the object exists, letting someone
enumerate ids and learn how many projects other users have. A 404 leaks nothing.

**401 instead of a redirect on the API:** the cookie events return a clean 401/403 when the
path starts with `/api`. A 302 to an HTML sign-in page is useless to `fetch()`.

---

## 8. Export / import

### 8.1 Export

| Format | Platforms | Contents |
|---|---|---|
| `.asm` Z80 | CPC, ZX | **rasm** dialect (user's choice): `defb`, labels per sprite/frame, equates for w/h. sjasmplus and pasmo accept the same output. |
| `.asm` 6502 | C64 | **ACME** dialect: `!byte`, sprite pointers, `*=` origin |
| `.prg` | C64 | Binary with a 2-byte load address — **loads straight into VICE** (`LOAD"*",8,1` or drag & drop) |
| `.bin` | all | Raw packed bytes, as they would sit in memory |
| `.h` / `.c` | all | `const unsigned char sprite_x[] = {…}` (z88dk / cc65 / SDCC) |
| `.png` | all | Preview with the correct aspect ratio, x1/x2/x4 |
| `.json` | all | Full project (lossless) — the import/backup format |
| `.spd` | C64 | SpritePad compatibility (stretch goal, M8) |

Export options: data order (row-major / column-major / screen-interleaved), mask inclusion,
padding, transparency colour, 64-byte blocks for the C64.

### 8.2 Import

#### JSON project ✅

A full project in one `.retrotools.json`: a backup, a transfer between installations, or a
file that sits in git next to your game's source.

Design principles:

| Decision | Why |
|---|---|
| Pixels as base64 of the **raw indexed buffer**, not RSPR | It is a public exchange format; it must not be bound to our internal storage encoding |
| The `id`s are **document-local** | A file cannot point at another user's data; they are remapped on import |
| Import **always creates a new** project | It never overwrites work; the owner is always the uploader, whatever the file says |
| `format` and `version` have **no default in the model** | If they came from an initializer, deserialisation would fill them in and **any JSON** would pass as a RetroTools project |
| Unknown version → rejection | Silently half-read work is worse than a clean error |
| Every error at once | The user fixes once, not seven times |
| Explicit upper limits (2048 sprites, 256 frames, 32 MB) | Without them a malicious file exhausts memory before validation is even reached |

The validator also checks what the API checks: dimensions against `SpriteSizeRule`, pixel
values against `MaxPixelValue`, attribute lengths, referential integrity (groups, cells →
sprites) and identifier uniqueness.

#### PNG ✅

[`PngReader`](src/RetroTools.Core/Imaging/PngReader.cs) accepts all five filter types
(None/Sub/Up/Average/Paeth), colour types 0/2/3/4/6 and bit depths 1 to 16. Interlace
(Adam7) is **explicitly rejected** with a message telling the user what to do — it is rare
in pixel art and would double the complexity.

[`ImageQuantizer`](src/RetroTools.Core/Imaging/ImageQuantizer.cs) has two strategies:

| Strategy | Behaviour |
|---|---|
| **AutoAssign** (default) | Picks the hardware colours that best cover the image. It rounds each colour to the nearest hardware colour **first** and then counts frequencies — otherwise near-identical shades that end at the same hardware colour would waste two slots |
| **UseProjectPalette** | The image is fitted to the existing slots; the palette does not change |

Distance is measured in **linear** space with luminance coefficients: in sRGB space `0x80`
is not visually half, so a plain Euclidean distance would systematically pick the wrong
shades — precisely at the CPC's mid levels.

Transparent pixels go to the mode's transparent slot; conversely, an opaque pixel is never
allowed to land there just because it happened to resemble whatever is assigned to it.

#### Raw `.bin` — **pending**

With an explicit mode and dimensions. Low priority: PNG and JSON cover the real workflows.

### 8.3 Attribute clash: where it is actually checked

M5 claimed a "live attribute-clash overlay" in the editor. **That was wrong** and the check
was removed: in per-cell modes the indexed buffer holds exactly `MaxColorsPerCell` possible
values (0 = PAPER, 1 = INK on the Spectrum). A cell **cannot** acquire a third colour — the
model rules it out by construction, which is a feature and not a gap: the tool cannot
produce a sprite that does not run.

The real colour loss happens on **image import**, and is reported there with numbers: how
many cells of the source image exceeded the limit and what the worst one was.

---

## 9. Validation rules per platform

| Rule | ZX | C64 | CPC |
|---|---|---|---|
| Width alignment | %8 | %8 (HW sprite: exactly 24 or 12) | %2 / %4 / %8 per mode |
| Height | free | HW sprite: exactly 21 | free |
| Max colours per 8×8 | 2 (+shared bright) | — | — |
| Max colours per sprite | — | 2 (hires) / 4 (MC) | 16 / 4 / 2 per mode |
| Shared colours | — | MC0/MC1 shared across all sprites | — |
| Palette index within range | ✔ | ✔ | ✔ |

Warnings are **non-blocking** (the user can draw freely) but they **block export** unless
"force" is chosen.

---

## 10. Implementation phases

| Phase | Deliverable | Definition of done |
|---|---|---|
| **M0 – Setup** ✅ | Solution, 5 projects, `Directory.Build.props`, `.gitignore`, user-secrets, hosting for service/reverse-proxy, MariaDB smoke tests | ✅ `dotnet build` clean (0 warnings); 3/3 DB tests green; the application starts and connects |
| **M1 – Platform catalog** ✅ | `PlatformCatalog` with all the data from §3, palette profiles, `PixelSlot` roles | ✅ 121 unit tests green: 27 CPC colours + the base-3 invariant, 32 HW inks→27 FW, 15 unique ZX, 16 C64 Pepto, ZX memory layout, C64 shared registers |
| **M2 – Codecs** ✅ | CPC Mode 0/1/2/3 interleaved packing, C64 hires/MC 63-byte, ZX bitmap + attributes + mask, `RsprContainer` | ✅ 204 tests green: exhaustive check of 256 combinations per CPC mode against an independent reference formula, round-trips, CPC/ZX memory layouts |
| **M3 – Data layer** ✅ | 12 EF Core entities (including `users`/`user_logins`), the `InitialSchema` migration, a seeder from `PlatformCatalog`, global ownership query filters on **every** entity | ✅ The migration was applied to the live database; 21 integration tests green, including: B cannot see A's project/sprite/frame, BLOB round-trip, cascade, concurrency, utf8mb4 |
| **M3.5 – Auth** ✅ | Cookie auth + GitHub & Google OAuth, `UserProvisioningService`, `/api/me`, `/account/*` | ✅ 12 provisioning tests; both challenges redirect correctly (verified with dummy keys); with no keys the application starts and reports `providers: false`. Accounts are **not** linked automatically by email — see §7.5 |
| **M4 – REST API** ✅ | `/api/platforms`, `/api/projects`, `/api/sprites`(+frames), `/api/spritemaps`, DTOs, validation against `PlatformCatalog`, `ProjectAccess` | ✅ 21 integration tests over the real HTTP pipeline: 401 instead of a redirect, 404 instead of 403, **a public project readable but never writable**, dimension and colour limits enforced |
| **M5 – Pixel editor** ✅ | `pixel-canvas.js` + Blazor, 7 tools, undo/redo, autosave, palette panel, projects/sprites pages | ✅ Verified in the browser: draw → autosave → reload → the pixels are there; undo/redo per stroke; a 384×192 canvas for a 16×16 sprite (Mode 0's 2:1 ratio) |
| **M6 – Groups & spritemaps** ✅ | Groups, spritemap composer with flip flags, `PngWriter` for thumbnails | ✅ Verified: a 4×4 spritemap, sprites placed, saved, reloaded — the cells are there. The thumbnails decode in the browser (96×48, 176 bytes) |
| **M7 – Export** ✅ | `.bin`, Z80 (rasm), 6502 (ACME), `.prg`, C header, PNG; an `/api/export` API filtered per platform; buttons in the editor | ✅ 26 tests. A C64 sprite → exactly 63 bytes with the right bits; `.prg` with a little-endian load address; CPC `.asm` with `defb &AA` and Gate Array values in the comments; download verified from the browser |
| **M7b – JSON project** ✅ | `ProjectDocument` + validator + serializer, `/api/projects/{id}/document`, `/api/projects/import`, UI | ✅ 31 tests. Round trip through the API with identical pixels; the owner is always the uploader; a tampered file is rejected without creating anything |
| **M7c – PNG import** ✅ | `PngReader` (5 filters, colour types 0/2/3/4/6, depths 1–16 bit), `ImageQuantizer` with automatic palette assignment, API + UI | ✅ 38 tests. Every filter type yields an identical image; round trip with our own writer; verified in the browser: a 256-colour gradient → 13 CPC pens |
| **M8 – Deployment & polish** | systemd unit + Windows service + nginx samples, data-protection keys, animation preview, tags/search, `.spd` | The application runs as a service behind a proxy with working OAuth |

Every phase closes with green tests and a commit.

---

## 11. Testing

- **Unit (xUnit):** codecs (round-trip + known byte patterns), palette mapping, validators,
  RSPR container, PNG reader/writer, exporters, project documents.
- **Integration:** EF Core against a real MariaDB (a separate test schema or a transaction
  rollback per test).
- **Manual verification:** export a C64 sprite → load it in VICE; a CPC Mode 0 sprite →
  WinAPE; ZX → Fuse. The most reliable acceptance test.

---

## 12. Performance & limits

- Max sprite size: 128×128 (16 KB indexed) — well above anything period-appropriate.
- Max frames per sprite: 64. Max sprites per project: 1024 (soft limits, configurable).
- Autosave debounce 1.5 s; delta batching per stroke; no `StateHasChanged` per pixel.
- Response caching on `/api/platforms` (static data).

---

## 13. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Blazor Server latency while drawing | The JS canvas owns the input loop; the server only sees batched deltas |
| Connecting to a remote MariaDB (latency/outages) | Connection resiliency (`EnableRetryOnFailure`), autosave with retry, local dirty state |
| `LangVersion 10` on .NET 10 templates | ✅ Confirmed in M0: clean build, 0 warnings; the forbidden features are documented in the README |
| **EF Core 9 on .NET 10** (no Pomelo for EF 10) | ✅ Verified with live queries against MariaDB 11.4.3. The packages are pinned to 9.0.x with a comment in the `.csproj`. Escape hatch if needed: MySqlConnector + Dapper with DbUp migrations |
| OAuth redirect URIs behind a proxy | `UseForwardedHeaders` with explicit `KnownProxies`; documented in `docs/deploy/` |
| Blazor Server through a proxy without WebSockets | A sample nginx config with upgrade headers; the long-polling fallback documented as a known problem |
| Palette divergence from the emulators | Palette profiles as a display setting; the data is always stored as hardware indices |
| Accuracy of the CPC hardware ink table | The §3.3 table was verified against cpctech/Grimware; covered by a unit test (32→27, 5 duplicates) |

---

## 14. Future extensions

- ZX: ULAplus (64 colours), Timex hi-colour/hi-res, ZX Spectrum Next (256 colours, 16×16
  hardware sprites).
- C64: char/tile editor, SpritePad + CharPad import/export, sprite multiplexer preview.
- CPC: CPC Plus (4096 colours, 16 hardware sprites), rasters/split palettes, OCP Art Studio.
- General: a shared sprite library, versioning/history, export to tilemap engines,
  PWA/offline.

---

## 15. Decisions (closed questions)

| # | Question | Decision | Impact on the plan |
|---|---|---|---|
| 1 | User accounts | **Multi-user from the start**, sign-in with **GitHub** and **Google** | New `users` / `user_logins` tables (§5.1); a new **M3.5** phase; `owner_id` becomes `NOT NULL`; a global ownership query filter; **no** ASP.NET Identity (it would break Pomelo) |
| 2 | Target framework | **net10.0** + `LangVersion 10.0` | EF Core pinned to 9.0.x — verified to work (§2) |
| 3 | Platform priority | **All three in parallel** per phase | None — that was already the plan's assumption |
| 4 | Assembler dialects | Z80 → **rasm**; C64 → **VICE** | §8.1 |
| 5 | Deployment | **Self-hosted service** (Windows Service / systemd) behind a **reverse proxy** | New §2.2; `UseWindowsService()` + `UseSystemd()` + forwarded headers + PathBase — implemented in M0; sample configs in M8 |

### 15.1 A clarification about C64 export

**VICE is an emulator, not an assembler** — there is no "VICE dialect" for source code. I
read the answer as *"the export must work with VICE"* and deliver **two** things:

1. **`.prg`** — a binary with a 2-byte load address, loads straight into VICE (drag & drop or
   `LOAD"*",8,1`). This is the "runs in VICE" deliverable.
2. **`.asm` in ACME** — the most widespread open 6502 dialect in the C64 scene, for anyone
   who wants source.

If you specifically meant **KickAssembler** or the **VICE monitor** syntax
(`a c000 lda #$00`), say so and I will add that exporter — it is small work now that the
codecs already produce the bytes.

### 15.2 What is needed for M3.5 (auth)

Two OAuth applications — you create them, the ClientId/ClientSecret go into user-secrets.
Step-by-step instructions are in [docs/oauth-setup.md](docs/oauth-setup.md).

Until you supply them the application starts normally — it just does not show the sign-in
buttons.

---

## Sources

- [Gate Array – cpctech](https://cpctech.cpcwiki.de/docs/garray.html) (the hardware ink table 0x40–0x5F ↔ firmware 0–26)
- [Gate Array – Grimware](https://www.grimware.org/doku.php/documentations/devices/gatearray) (pixel bit encoding for Modes 0/1/2)
- [Calculating the color palette of the VIC-II – Pepto](https://www.pepto.de/projects/colorvic/) (the C64 palette)
- [ZX Spectrum Palette – Lospec](https://lospec.com/palette-list/zx-spectrum)
- [ZX Spectrum graphic modes – Wikipedia](https://en.wikipedia.org/wiki/ZX_Spectrum_graphic_modes)
- [The ZX Spectrum Color Palette, Resolution and Attributes – retrotechlab](https://www.retrotechlab.com/the-zx-spectrum-color-palette-resolution-and-attributes/)
- [Amstrad CPC – Wikipedia](https://en.wikipedia.org/wiki/Amstrad_CPC)

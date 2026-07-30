# RetroTools — User manual

> **Language:** English · [Ελληνικά](manual.el.md)

This manual covers using the tool. For installation see the
[README](../README.md); for the hardware study and design decisions see
[plan.md](../plan.md).

---

## Contents

1. [The idea in one paragraph](#the-idea-in-one-paragraph)
2. [Signing in](#signing-in)
3. [Creating a project](#creating-a-project)
4. [Choosing a platform and mode](#choosing-a-platform-and-mode)
5. [The pixel editor](#the-pixel-editor)
6. [Palette](#palette)
7. [Frames and animation](#frames-and-animation)
8. [Groups](#groups)
9. [Spritemaps](#spritemaps)
10. [Importing from PNG](#importing-from-png)
11. [Exporting](#exporting)
12. [Backup and transfer](#backup-and-transfer)
13. [Sharing a project](#sharing-a-project)

---

## The idea in one paragraph

A modern drawing program lets you put any colour anywhere. An 8-bit machine does not.
The Spectrum allows two colours per 8×8 cell; the C64 forces hardware sprites to be
exactly 24×21 pixels; the CPC packs two, four or eight pixels into each byte depending on
the mode, so a sprite's width has to be a multiple of 2, 4 or 8.

RetroTools knows those rules and applies them while you draw. The result is that a sprite
you finish here **can actually run on the machine** — you do not discover at export time
that it was impossible all along.

---

## Signing in

There are no local passwords. Sign in with **GitHub** or **Google**.

Only the providers whose keys are configured appear. If neither is configured, see
[OAuth setup](oauth-setup.md).

If you sign in with a second provider whose email matches an existing account, the sign-in
is **stopped** and you are asked to sign in with the original provider instead. That is
deliberate: automatically merging accounts by email is a well-known account-takeover
route. Link the second provider from your account settings, where your identity is
already proven.

---

## Creating a project

A project holds one platform and one mode. Everything inside it — sprites, palette,
groups, spritemaps — follows that mode's rules.

On the **My projects** page, give the project a name, pick the platform and mode from the
dropdown, and press **Create**.

You cannot change a project's mode afterwards. The pixel data would still be valid, but
the constraints would not — a 16×16 sprite is fine in CPC Mode 0 and impossible as a C64
hardware sprite. Create a second project instead, or export to JSON and adjust.

---

## Choosing a platform and mode

The dropdown groups modes by machine. What to pick:

### Amstrad CPC

| Mode | Resolution | Colours | Sprite width | Notes |
|---|---|---|---|---|
| Mode 0 | 160×200 | 16 | multiple of 2 | The usual choice for games. Pixels are **twice as wide as tall** |
| Mode 1 | 320×200 | 4 | multiple of 4 | Square pixels |
| Mode 2 | 640×200 | 2 | multiple of 8 | Narrow pixels, mostly for text |
| Mode 3 | 160×200 | 4 | multiple of 2 | Undocumented; same encoding as Mode 0 with fewer pens |

The CPC is the only one of the three with a **programmable palette**: each of the 16 pens
can point at any of the 27 hardware colours.

### Commodore 64

| Mode | Size | Colours | Notes |
|---|---|---|---|
| Hardware sprite — hires | **exactly 24×21** | 1 + transparent | Fixed by the VIC-II |
| Hardware sprite — multicolor | **exactly 12×21** | 3 + transparent | Wide pixels; displayed 24 pixels across |
| Character — hires | 8×8 | 2 | |
| Character — multicolor | 4×8 | 4 | Wide pixels |
| Bitmap — hires | free, width %8 | 2 per 8×8 cell | |
| Bitmap — multicolor | free, width %4 | 4 per cell | Wide pixels |

The C64 is the only one with **real hardware sprites** — and the only one where some
palette slots are **shared registers**. See [Palette](#palette).

### ZX Spectrum

| Mode | Size | Notes |
|---|---|---|
| Software sprite with attributes | free, width %8 | Bitmap plus an 8×8 attribute grid |
| Software sprite, monochrome | free, width %8 | Bitmap only; the background supplies the colours. The most common kind in Spectrum games |
| UDG — 8×8 character | fixed 8×8 | The basis for tile-based games |

---

## The pixel editor

Open a sprite with **Edit**. The canvas shows the sprite at the current zoom, **with the
mode's real pixel shape** — a CPC Mode 0 sprite is drawn twice as wide as it is tall,
because that is how it will look on the machine.

### Tools

| Tool | What it does |
|---|---|
| ✏ Pencil | Draws with the selected colour. Drag for a continuous line |
| ◻ Erase | Draws slot 0 |
| 🪣 Fill | Flood-fills the contiguous area of the same colour |
| ／ Line | Drag from start to end; a preview follows the cursor |
| ▭ Rectangle | Outline |
| ▬ Filled rectangle | |
| ⌖ Pick colour | Sets the current colour from the pixel under the cursor |

**Right-click always erases**, whichever tool is selected.

### Zoom and grids

The zoom slider goes from 2× to 32×. Two grid overlays:

- **Pixel grid** — appears from zoom 6× upwards, below that it is just noise.
- **Cell grid** — red, marks the 8×8 boundaries where the Spectrum's attribute limit and
  the C64's per-cell colours apply. Only shown for modes that have cells.

### Undo / redo

Per **stroke**, not per pixel: one drag of the pencil is one undo step. History holds 100
steps and resets when you switch frames.

### Saving

Automatic, about 1.5 seconds after your last stroke. The badge at the top right says
**Unsaved** or **Saved**. Switching frames or leaving the page saves first.

Drawing itself happens entirely in your browser, so it never waits for the server. Only
the finished stroke is sent.

---

## Palette

The left panel lists the mode's slots. Each row shows the colour swatch, the slot's name
in the machine's own vocabulary (`PAPER`, `INK`, `Multicolor 0`, `Pen 3`), and the
hardware register it comes from.

Click the swatch to draw with that slot. Use the dropdown to change which hardware colour
the slot points at.

### The "shared" warning

Some C64 slots are marked **shared**. Those are global registers — `$D025` and `$D026` for
multicolor sprites, `$D021` for the background. Changing one of them changes it for
**every sprite on the screen**, not just this one.

That is how the hardware works and the tool shows it rather than hiding it. If two sprites
need different colours in a shared slot, they cannot appear on screen at the same time.

### Transparency

Slots marked as transparent have no colour of their own; the background shows through.
They appear as a neutral dark square in the palette and as a checkerboard around the
canvas.

### Palette profiles

The same hardware colour looks slightly different across emulators. A project records
which profile it uses:

- **CPC**: `nominal` (0/128/255) or `measured` (a darker mid-level, closer to real hardware)
- **ZX**: `d8` or `d7` — the two readings of the non-bright level found in the literature
- **C64**: `pepto`

This only affects what you see. The stored data is always hardware colour indices, so
switching profiles never alters your sprite.

---

## Frames and animation

Every sprite starts with one frame. **+ New frame** adds an empty one; the numbered
buttons switch between them. A sprite must keep at least one frame — to get rid of it,
delete the sprite.

Frames are exported one after another, so an animation is a single contiguous block of
bytes in memory.

---

## Groups

Groups are for organising, nothing more: "Player", "Enemies", "Tiles". Create them at the
bottom of the project page and assign each sprite with the dropdown on its card.

**Deleting a group does not delete its sprites** — they just become ungrouped.

---

## Spritemaps

A spritemap is a grid of cells, each pointing at a sprite. Use it for animation strips,
tilesets or character sets — anything where position in a grid carries meaning.

Create one on the project page with a name and dimensions, which opens the composer:

1. Pick a sprite in the left-hand list.
2. Click a cell to place it.
3. **Right-click a cell to empty it.**
4. Press **Save**.

The **Horizontal / Vertical flip** switches apply to cells you place next, and are stored
per cell — the same sprite can appear mirrored in several places without duplicating it.

Shrinking the grid discards cells that fall outside it. If they were kept they would be
invisible but would come back on a later enlargement.

---

## Importing from PNG

On the project page, under **…or import from PNG**.

The image's dimensions must be valid for the mode. A 16×16 PNG is fine for CPC Mode 0 and
rejected for a C64 hardware sprite, which demands exactly 24×21.

### The palette switch

- **Off (default)** — the tool picks the hardware colours that best cover the image and
  **updates the project palette**. Best for the first sprite in a project.
- **On** — the image is fitted to the palette you already have. Use this once you have
  other sprites, so their colours do not change under you.

### What you get told

Import never fails silently. Expect messages like:

```
The image has 256 distinct colours and was converted to 16.
3 cells of 8×8 in the image had more than 2 colours (up to 4) — the hardware
allows 2 per cell, so the rest were lost (attribute clash).
```

That last one is the Spectrum's central limitation. The message tells you which regions
need reworking, before you find out from an emulator.

Fully transparent pixels map to the mode's transparent slot where one exists.

Interlaced PNGs are rejected with a message saying so — save without interlacing.

---

## Exporting

In the editor, under **Export**. Only the formats that apply to the platform are offered.

| Format | Contents |
|---|---|
| `.bin` | Raw packed bytes, exactly as they would sit in memory |
| Z80 assembly | `defb` for **rasm** (sjasmplus and pasmo accept the same) |
| 6502 assembly | `!byte` in **ACME** dialect |
| `.prg` | C64 binary with a load address — **loads straight into VICE** |
| C header | `const unsigned char …[]` for z88dk / cc65 / SDCC |
| PNG | Preview image with the correct pixel aspect ratio |

The source formats carry a header comment with the size, the bytes per row, and the
palette in **hardware values**:

```
; player
; Amstrad CPC — Mode 0 — 160×200, 16 colours
; 16x16 pixels · 8 bytes/row · 128 bytes/frame · 1 frame
; Palette:
;   0: Pen 0 = Black (firmware 0, hardware &54)
```

`&54` is what you write to the Gate Array. An RGB value would be useless — there is
nowhere to write it.

For C64 hardware sprites the export also states that the data must be aligned to a
64-byte boundary and that the sprite pointer is `address / 64`. That is the most common
cause of "why am I seeing garbage" on the C64.

### Masks

Where the sprite has a mask, the **Include mask** switch adds it after each frame's data
in the form a Z80 `AND mask : OR data` routine expects: the mask bit is `1` where the
background shows through.

---

## Backup and transfer

**Export JSON** on the projects page downloads the whole project — sprites, frames,
masks, attributes, palette, groups, spritemaps — as one `.retrotools.json`. It is
readable text, so it belongs in git next to your game's source.

Upload it under **Import from file**. Import always creates a **new** project and never
overwrites anything; you become its owner regardless of what the file says.

If the file has a problem you get every error at once, not one at a time:

```
Sprite 'player' (id 1): the mode requires exactly 24×21 pixels (given 8×8).
Spritemap 'tileset': a cell points at a non-existent sprite 99.
```

Nothing is created when validation fails.

---

## Sharing a project

Projects are private by default. Setting the visibility to **Public** makes the project
readable by anyone with the link, including visitors who are not signed in.

Public means **read-only**. Nobody but the owner can change or delete a project, whatever
its visibility. Attempts return "not found" rather than "forbidden", so the existence of
other people's projects is never disclosed.

# RetroTools – Sprite / Spritemap Studio

Πλάνο υλοποίησης web εργαλείου σχεδίασης sprites & spritemaps για **Amstrad CPC**, **Commodore 64** και **ZX Spectrum**.

- Έκδοση εγγράφου: 1.1
- Ημερομηνία: 2026-07-29
- Κατάσταση: **Εγκεκριμένο** — οι αποφάσεις του §15 ενσωματώθηκαν. **M0 ολοκληρωμένο.**

---

## 1. Στόχος & Scope

### 1.1 Τι θα κάνει το εργαλείο

Web εφαρμογή όπου ο χρήστης:

1. Δημιουργεί **Project** επιλέγοντας πλατφόρμα (CPC / C64 / Spectrum) και γραφικό **mode**.
2. Σχεδιάζει **sprites** σε pixel editor με **αυθεντική παλέτα** και **αυθεντικούς περιορισμούς** της πλατφόρμας (αριθμός χρωμάτων, byte alignment, attribute clash, pixel aspect ratio).
3. Ομαδοποιεί sprites σε **Sprite Groups / Spritemaps** (sheets με γραμμές/στήλες, π.χ. animation strips, tilesets, character sets).
4. **Αποθηκεύει / φορτώνει** τα πάντα σε MariaDB.
5. Κάνει **export** σε μορφές που τρώνε άμεσα οι assemblers της εποχής (Z80 / 6502) καθώς και σε PNG/JSON, και **import** από PNG/JSON.

### 1.2 Εκτός scope (v1)

- Full-screen bitmap/loading-screen editor (μόνο sprites & tiles).
- Map editor επιπέδων (level design) — μόνο spritemaps/tilesheets.
- Emulator integration / live preview σε πραγματικό υλικό.
- Multi-user real-time collaboration.
- ULAplus, Timex hi-colour, VDC, CPC Plus (ASIC 4096 χρώματα, hardware sprites) — καταγράφονται ως μελλοντικά extensions (βλ. §14).

---

## 2. Τεχνικό Stack & Αποφάσεις

| Θέμα | Απόφαση | Αιτιολογία |
|---|---|---|
| Γλώσσα | **C# 10** (`<LangVersion>10.0</LangVersion>`) | Ρητή απαίτηση χρήστη |
| Target Framework | **net10.0** | Επιλογή χρήστη. SDK 10.0.301 / runtime 10.0.9 εγκατεστημένα. |
| Web framework | **ASP.NET Core MVC + Blazor (Interactive Server)** | Ρητή απαίτηση. MVC για site/CRUD/API, Blazor για τον editor. |
| ORM | **EF Core 9.0.x + Pomelo.EntityFrameworkCore.MySql 9.0.0**, καρφωμένα | Το Pomelo (ο μόνος ώριμος MariaDB provider) **δεν έχει build για EF Core 10**. Τα EF 9 assemblies τρέχουν κανονικά πάνω σε .NET 10 — επιβεβαιώθηκε με build + live queries στη MariaDB 11.4.3. |
| DB | **MariaDB 11.4.3** | Ρητή απαίτηση. Επιβεβαιώθηκε σύνδεση, δικαιώματα DDL, utf8mb4, BLOB round-trip. Τα στοιχεία του διακομιστή μένουν εκτός repository. |
| Rendering canvas | HTML `<canvas>` + JS module, με Blazor να κρατά το authoritative model | Το ζωγράφισμα ανά pixel σε Blazor Server θα είχε απαράδεκτο latency ανά mouse-move. |
| Auth | **Cookie auth + OAuth (GitHub, Google)**, δικός μας πίνακας `users`. **Multi-user από την αρχή.** | Επιλογή χρήστη. **Χωρίς ASP.NET Core Identity**: το `Identity.EntityFrameworkCore` 10.x απαιτεί EF Core 10, που θα έσπαγε το Pomelo 9. Χωρίς τοπικούς κωδικούς το Identity δεν προσφέρει τίποτα εδώ. |
| Hosting | Self-hosted service (**Windows Service / systemd**) πίσω από reverse proxy | Επιλογή χρήστη. Βλ. §2.2. |
| Tests | xUnit | Κρίσιμα τα codecs/παλέτες. |

> **Σημείωση C# 10:** Το `LangVersion 10.0` απαγορεύει raw string literals (C#11), `required` members (C#11), primary constructors (C#12), collection expressions (C#12). Επιτρέπονται file-scoped namespaces, global usings, record structs. Το `LangVersion` ορίζεται κεντρικά στο `Directory.Build.props`, μαζί με το `TargetFramework`.

> **⚠ Κλείδωμα EF Core:** τα πακέτα `Pomelo.EntityFrameworkCore.MySql`, `Microsoft.EntityFrameworkCore.*` πρέπει να μείνουν στο **9.0.x**. Αναβάθμιση σε 10.x θα τραβήξει EF Core 10 και το Pomelo 9 θα σπάσει στο runtime (τα provider APIs αλλάζουν ανά major). Ξεκλειδώνει μόνο όταν βγει Pomelo για EF Core 10.

### 2.1 Δομή repository

```
retrotools/
├─ RetroTools.sln
├─ src/
│  ├─ RetroTools.Core/          # Domain: παλέτες, modes, codecs, validation. Χωρίς εξαρτήσεις.
│  ├─ RetroTools.Data/          # EF Core: entities, DbContext, migrations, repositories
│  └─ RetroTools.Web/           # MVC controllers + Views + Blazor components + wwwroot
├─ tests/
│  ├─ RetroTools.Core.Tests/
│  └─ RetroTools.Data.Tests/    # integration tests με MariaDB
├─ docs/
│  └─ platform-notes.md         # οι πίνακες του §3 σε αναλυτική μορφή
├─ Directory.Build.props   # TargetFramework + LangVersion κεντρικά
├─ .gitignore
├─ plan.md
└─ README.md
```

### 2.2 Μοντέλο deployment

Self-hosted **ως service**, σε Windows ή Linux, πίσω από reverse proxy.

- **Windows:** `sc.exe create` → η εφαρμογή τρέχει με `UseWindowsService()`.
- **Linux:** unit αρχείο systemd → `UseSystemd()`.
  Και οι δύο κλήσεις είναι no-op όταν τρέχει από κονσόλα, οπότε το development δεν επηρεάζεται.
- **Reverse proxy** (nginx / Apache / IIS ARR / Caddy) τερματίζει το TLS. Η εφαρμογή:
  - διαβάζει `X-Forwarded-For` / `-Proto` / `-Host` μέσω `UseForwardedHeaders()`,
  - δέχεται τα headers **μόνο** από ρητά δηλωμένους proxies (`KnownProxies` / `KnownNetworks` σε CIDR) — αλλιώς ο header είναι spoofable,
  - υποστηρίζει `PathBase` για φιλοξενία κάτω από sub-path (π.χ. `/spritestudio`),
  - απενεργοποιεί το εσωτερικό HTTPS redirect (`EnableHttpsRedirection: false`) αφού το κάνει ο proxy.
- **Κρίσιμο για OAuth:** χωρίς σωστά forwarded headers τα redirect URIs βγαίνουν `http://` και τα GitHub/Google callbacks αποτυγχάνουν.
- **WebSockets:** ο Blazor Server χρειάζεται WebSocket upgrade στον proxy (`proxy_set_header Upgrade/Connection` σε nginx), αλλιώς πέφτει σε long-polling με αισθητό latency στον editor.
- **Data Protection keys:** πρέπει να επιμένουν σε δίσκο (ή στη βάση), αλλιώς κάθε restart ακυρώνει τα auth cookies.
- Έτοιμα δείγματα (systemd unit, nginx site, Windows service) θα μπουν στο `docs/deploy/`.

---

## 3. Μελέτη Πλατφορμών

Αυτή είναι η **καρδιά** του εργαλείου: κάθε αριθμός εδώ γίνεται δεδομένο στο `PlatformCatalog` του `RetroTools.Core`.

### 3.1 ZX Spectrum (48K/128K)

#### Ανάλυση & χρώμα
- Οθόνη **256 × 192** pixels.
- **Δεν υπάρχει per-pixel χρώμα.** Η οθόνη χωρίζεται σε **32 × 24 attribute cells των 8×8 pixels**.
- Κάθε cell έχει **ένα** attribute byte:

| Bit | 7 | 6 | 5–3 | 2–0 |
|---|---|---|---|---|
| Σημασία | FLASH | BRIGHT | PAPER (0–7) | INK (0–7) |

- Άρα **μέγιστο 2 χρώματα ανά 8×8 cell**, και το BRIGHT ισχύει **και για τα δύο** μαζί. Αυτό είναι το περίφημο **attribute clash**.
- Παλέτα: 8 βασικά χρώματα σε σειρά bit **GRB** (bit0=Blue, bit1=Red, bit2=Green) × 2 επίπεδα φωτεινότητας = **15 μοναδικά χρώματα** (bright black = black).

| # | Όνομα | Normal | Bright |
|---|---|---|---|
| 0 | Black | `#000000` | `#000000` |
| 1 | Blue | `#0000D8` | `#0000FF` |
| 2 | Red | `#D80000` | `#FF0000` |
| 3 | Magenta | `#D800D8` | `#FF00FF` |
| 4 | Green | `#00D800` | `#00FF00` |
| 5 | Cyan | `#00D8D8` | `#00FFFF` |
| 6 | Yellow | `#D8D800` | `#FFFF00` |
| 7 | White | `#D8D8D8` | `#FFFFFF` |

> Το non-bright επίπεδο είναι ~85% της τάσης. Στη βιβλιογραφία εμφανίζεται είτε ως `0xD8` (Lospec) είτε ως `0xD7` (Fuse κ.ά.). Θα υλοποιηθεί ως **επιλέξιμο palette profile** (`D8` default, `D7` alternative), ώστε το preview να ταιριάζει με τον emulator του χρήστη.

#### Sprites
- **Δεν υπάρχουν hardware sprites.** Όλα είναι software sprites, σχεδιασμένα σε byte-aligned πλάτος.
- Πρακτικοί περιορισμοί editor:
  - Πλάτος: **πολλαπλάσιο του 8** (1 byte = 8 pixels). Επιτρεπτά: 8, 16, 24, 32, 48, 64.
  - Ύψος: ελεύθερο σε pixels (τυπικά 8, 16, 21, 24, 32).
  - Προαιρετικό **mask** (AND mask + OR data) για διαφάνεια — δεύτερο bitplane ίδιων διαστάσεων.
- Χρώμα sprite: είτε **monochrome + attribute** ανά cell (κλασικό), είτε "colour sprite" όπου το εργαλείο κρατά ξεχωριστό attribute grid `ceil(w/8) × ceil(h/8)`.

#### Μνήμη (για export)
- Bitmap: 6144 bytes @ `0x4000`, μη γραμμικό layout σε 3 thirds:
  ```
  addr = 0x4000 + ((y & 0xC0) << 5) + ((y & 0x07) << 8) + ((y & 0x38) << 2) + x_byte
  ```
- Attributes: 768 bytes @ `0x5800`, γραμμικά: `0x5800 + (y >> 3) * 32 + x_byte`.
- Το εργαλείο θα κάνει export **γραμμικά** (σειρά-σειρά, φιλικό για blitter routines) **και** προαιρετικά σε screen-layout σειρά.

#### Pixel aspect ratio
1 : 1 (τετράγωνα pixels).

---

### 3.2 Commodore 64

#### Χρώμα
- VIC-II με **σταθερή παλέτα 16 χρωμάτων** (δεν αλλάζει — δεν υπάρχει programmable palette).
- Χρησιμοποιούμε την **παλέτα Pepto** (η de-facto standard, υπολογισμένη από ανάλυση του VIC-II):

| # | Όνομα | Hex | | # | Όνομα | Hex |
|---|---|---|---|---|---|---|
| 0 | Black | `#000000` | | 8 | Orange | `#6F4F25` |
| 1 | White | `#FFFFFF` | | 9 | Brown | `#433900` |
| 2 | Red | `#68372B` | | 10 | Light Red | `#9A6759` |
| 3 | Cyan | `#70A4B2` | | 11 | Dark Grey | `#444444` |
| 4 | Purple | `#6F3D86` | | 12 | Grey | `#6C6C6C` |
| 5 | Green | `#588D43` | | 13 | Light Green | `#9AD284` |
| 6 | Blue | `#352879` | | 14 | Light Blue | `#6C5EB5` |
| 7 | Yellow | `#B8C76F` | | 15 | Light Grey | `#959595` |

> Θα προβλεφθούν εναλλακτικά palette profiles (Colodore, VICE "Pepto NTSC") ως ρύθμιση προβολής — τα δεδομένα αποθηκεύονται πάντα ως δείκτες 0–15.

#### Hardware sprites (MOBs)
Η **μόνη** από τις τρεις πλατφόρμες με πραγματικά hardware sprites.

| Χαρακτηριστικό | Hi-res | Multicolor |
|---|---|---|
| Διαστάσεις | **24 × 21** pixels | **12 × 21** (διπλού πλάτους pixels → 24 pixels οθόνης) |
| Bits/pixel | 1 | 2 |
| Μέγεθος δεδομένων | 63 bytes (3 × 21), σε block 64 bytes | ίδιο |
| Χρώματα | 1 + διαφάνεια | 3 + διαφάνεια |

- **Πλήθος:** 8 ταυτόχρονα (0–7), max 8 ανά raster line χωρίς multiplexing.
- **Χρώματα multicolor:**
  | Bit pair | Πηγή χρώματος |
  |---|---|
  | `00` | Διαφανές (φαίνεται το background) |
  | `01` | `$D025` — Sprite Multicolor 0 (**κοινό για όλα τα sprites**) |
  | `10` | `$D027+n` — χρώμα του συγκεκριμένου sprite |
  | `11` | `$D026` — Sprite Multicolor 1 (**κοινό για όλα τα sprites**) |
- **Expansion:** X (`$D01D`) και/ή Y (`$D017`) → εμφάνιση 48×21, 24×42 ή 48×42 (τα δεδομένα παραμένουν 24×21).
- **Sprite pointers:** byte στο `screen_base + $03F8 + n`, τιμή = `data_address / 64`.
- Το εργαλείο θα κρατά **shared palette slots** ανά project (MC0/MC1) και per-sprite χρώμα — ακριβώς όπως το hardware.

#### Char / bitmap "sprites" (tiles)
| Mode | Ανάλυση | Χρώματα |
|---|---|---|
| Standard text | 40×25 chars (8×8) | 1 χρώμα/char + κοινό background |
| Multicolor text | 40×25 (pixels 4×8, διπλού πλάτους) | 3 κοινά + 1 per-char (από 0–7) |
| Hi-res bitmap | 320×200 | 2 χρώματα ανά 8×8 cell |
| Multicolor bitmap | 160×200 (pixels διπλού πλάτους) | 4 ανά 8×8 cell: `00`=$D021 κοινό, `01`=screen RAM hi-nibble, `10`=screen RAM lo-nibble, `11`=Colour RAM |

#### Pixel aspect ratio
Hi-res 1:1 · Multicolor 2:1 (φαρδιά pixels).

---

### 3.3 Amstrad CPC (464 / 664 / 6128)

#### Χρώμα
- **27 χρώματα**: 3 επίπεδα (0% / 50% / 100%) × 3 κανάλια RGB = 3³ = 27.
- Ο Gate Array δέχεται **32 hardware ink values (`0x40`–`0x5F`)** που χαρτογραφούνται στα 27 firmware χρώματα (5 διπλότυπα).

Πλήρης πίνακας (firmware # ↔ hardware value ↔ RGB @ 0/128/255):

| FW# | Όνομα | R,G,B % | Hex | HW value(s) |
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

> Στο πραγματικό υλικό το "50%" μετριέται πιο κοντά στο ~40% της τάσης. Θα υπάρξουν δύο palette profiles: **"Nominal"** (0/128/255, default) και **"Measured"** (πιο σκούρο mid-level) μόνο για την προβολή.

#### Modes
| Mode | Ανάλυση | Pens | Bits/pixel | Pixels/byte | Aspect |
|---|---|---|---|---|---|
| 0 | 160 × 200 | 16 | 4 | 2 | 2:1 (φαρδιά) |
| 1 | 320 × 200 | 4 | 2 | 4 | 1:1 |
| 2 | 640 × 200 | 2 | 1 | 8 | 1:2 (στενά) |
| 3 (undocumented) | 160 × 200 | 4 | 4 (μόνο 2 χρήσιμα) | 2 | 2:1 |

- Η παλέτα οθόνης έχει **16 pens** (0–15) + **1 border ink**. Κάθε pen δείχνει σε ένα από τα 27 χρώματα. Στο Mode 1 χρησιμοποιούνται pens 0–3, στο Mode 2 pens 0–1.
- Υποστηρίζεται **flashing ink** (εναλλαγή δύο χρωμάτων) — προαιρετικό πεδίο στην παλέτα.

#### Pixel encoding (κρίσιμο για export)
Ο CPC έχει "μπερδεμένη" (interleaved) διάταξη bits μέσα στο byte:

- **Mode 0** — 2 pixels/byte, `A` = αριστερό, `B` = δεξί, `bN` = bit N της τιμής pen (0–15):
  ```
  bit7 bit6 bit5 bit4 bit3 bit2 bit1 bit0
  A.b0 B.b0 A.b2 B.b2 A.b1 B.b1 A.b3 B.b3
  ```
- **Mode 1** — 4 pixels/byte (`A`..`D` από αριστερά):
  ```
  A.b0 B.b0 C.b0 D.b0 A.b1 B.b1 C.b1 D.b1
  ```
- **Mode 2** — 8 pixels/byte, ευθύ: bit7 = αριστερότερο pixel.

> **Ενοποιημένος κανόνας (υλοποιημένος στο M2):** και τα τρία modes περιγράφονται από έναν τύπο.
> Το bit `k` του pen ενός pixel στη θέση `p` μέσα στο byte πηγαίνει στο bit `BitPositions[k] − p`,
> με `BitPositions = { 7, 3, 5, 1 }`. Το Mode 1 χρησιμοποιεί τα δύο πρώτα, το Mode 2 μόνο το πρώτο
> (και προκύπτει ως απλό MSB-first). Αυτό αντικαθιστά τρεις χωριστές υλοποιήσεις με μία, και
> επαληθεύεται εξαντλητικά (256 συνδυασμοί ανά mode) έναντι του ρητού τύπου της τεκμηρίωσης.

#### Sprites
- **Δεν υπάρχουν hardware sprites** (πλην CPC Plus). Software sprites με **byte alignment**:
  | Mode | Πλάτος πρέπει να είναι πολλαπλάσιο του |
  |---|---|
  | 0 | **2** pixels |
  | 1 | **4** pixels |
  | 2 | **8** pixels |
- Ύψος ελεύθερο. Τυπικά μεγέθη Mode 0: 4×16, 8×16, 16×16, 16×24, 32×32.
- Προαιρετικό mask για transparency.

#### Μνήμη (για export)
- 16 KB @ `0xC000` (default), 80 bytes/γραμμή, με 8 interleaved "banks":
  ```
  addr = base + ((y & 7) * 0x800) + ((y >> 3) * 0x50) + x_byte
  ```

---

### 3.4 Συγκριτικός πίνακας

| | ZX Spectrum | Commodore 64 | Amstrad CPC |
|---|---|---|---|
| Παλέτα υλικού | 15 χρώματα (8×2 bright) | 16 σταθερά | 27 |
| Programmable palette | Όχι | Όχι | **Ναι** (16 pens από 27) |
| Χρώματα ταυτόχρονα (sprite area) | 2 ανά 8×8 cell | 4 ανά sprite (MC) | 16 (Mode 0) |
| Hardware sprites | Όχι | **Ναι** (8 × 24×21) | Όχι |
| Ανάλυση | 256×192 | 320×200 / 160×200 | 160/320/640 × 200 |
| Attribute clash | **Ναι** (έντονο) | Ναι (σε bitmap/char modes) | **Όχι** |
| Byte alignment sprite | 8 px | 8 px (24 για HW) | 2 / 4 / 8 px |
| CPU / assembler export | Z80 (`defb`) | 6502 (`.byte`) | Z80 (`defb`) |

---

## 4. Domain Model

```
Project (1 πλατφόρμα + 1 mode)
 ├── Palette          ← ποια hardware χρώματα δείχνει κάθε slot/pen
 ├── Sprite *         ← w × h, N frames
 │    └── SpriteFrame *   ← pixel indices + (Spectrum) attributes + (προαιρ.) mask
 ├── SpriteGroup *    ← λογική ομάδα (π.χ. "Player", "Enemies")
 └── SpriteMap *      ← πλέγμα cols × rows από κελιά
      └── SpriteMapCell * ← αναφορά σε Sprite/Frame + flags (flipX, flipY, priority)
```

### 4.1 Αναπαράσταση pixel δεδομένων

- Κάθε frame αποθηκεύεται ως **indexed buffer: 1 byte ανά pixel** = index στο palette slot (0–15).
  - Απλό, ανεξάρτητο mode, εύκολο undo/redo και diff.
  - Η μετατροπή σε packed hardware bytes γίνεται **μόνο στο export** από τα codecs.
- Μέγεθος: 64×64 sprite = 4 KB → μια χαρά για `MEDIUMBLOB`. Εφαρμόζεται **Deflate** πριν την αποθήκευση (τυπικά >90% συμπίεση σε pixel art).
- Container format `RSPR` (header 16 bytes: magic, version, w, h, encoding, flags) ώστε να αλλάξει μελλοντικά η κωδικοποίηση χωρίς migration.

### 4.2 Attributes (Spectrum)

Ξεχωριστό buffer `ceil(w/8) × ceil(h/8)` bytes, ένα attribute byte ανά cell (ίδια bit διάταξη με το υλικό).

---

## 5. Βάση Δεδομένων (MariaDB 11)

Charset: `utf8mb4`, collation `utf8mb4_unicode_ci`, engine InnoDB.
Πρόθεμα πινάκων: κανένα (η βάση είναι αποκλειστική για την εφαρμογή).

### 5.1 Σχήμα (σκίτσο DDL)

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

-- Multi-user από την αρχή. Χωρίς ASP.NET Identity, χωρίς τοπικούς κωδικούς:
-- η ταυτοποίηση γίνεται αποκλειστικά μέσω GitHub / Google OAuth.
CREATE TABLE users (
  id            CHAR(36)     NOT NULL PRIMARY KEY,   -- δικό μας GUID, σταθερό
  display_name  VARCHAR(128) NOT NULL,
  email         VARCHAR(256) NULL,
  avatar_url    VARCHAR(512) NULL,
  created_utc   DATETIME(3)  NOT NULL,
  last_login_utc DATETIME(3) NULL,
  is_disabled   TINYINT(1)   NOT NULL DEFAULT 0
);

-- Ένας χρήστης μπορεί να συνδέει και GitHub και Google στον ίδιο λογαριασμό.
CREATE TABLE user_logins (
  provider      VARCHAR(32)  NOT NULL,               -- 'github' | 'google'
  provider_key  VARCHAR(128) NOT NULL,               -- το σταθερό subject id του provider
  user_id       CHAR(36)     NOT NULL,
  linked_utc    DATETIME(3)  NOT NULL,
  PRIMARY KEY (provider, provider_key),
  KEY (user_id),
  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE projects (
  id            BIGINT AUTO_INCREMENT PRIMARY KEY,
  owner_id      CHAR(36) NOT NULL,          -- multi-user: κάθε project ανήκει σε χρήστη
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
  meta_json  JSON NULL,             -- π.χ. C64: sprite colour, expandX/Y, multicolor
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

Όλα τα FK με `ON DELETE CASCADE` προς τα κάτω από το `projects`.

**Επιβολή ιδιοκτησίας (multi-user):** κάθε ερώτημα προς `sprites` / `spritemaps` / `palettes`
περνά υποχρεωτικά μέσα από το `project_id` → `owner_id`. Θα υλοποιηθεί ως **EF Core global
query filter** πάνω στο `projects` (`p => p.OwnerId == _currentUser.Id || p.Visibility == Public`),
ώστε να μην μπορεί ένα ξεχασμένο `Where` να διαρρεύσει δεδομένα άλλου χρήστη. Τα API endpoints
που δέχονται `id` κάνουν επιπλέον ρητό έλεγχο και επιστρέφουν **404 (όχι 403)** για ξένα
αντικείμενα, ώστε να μη διαρρέει η ύπαρξή τους.

### 5.2 Migrations

- EF Core migrations (`dotnet ef migrations add`), όχι hand-written SQL. ✅ `InitialSchema` εφαρμοσμένο.
- Τα `platforms` / `platform_modes` γεμίζουν με **runtime seed** από τον `PlatformCatalog` σε κάθε
  εκκίνηση, όχι με `HasData`. Έτσι μια διόρθωση στα δεδομένα υλικού δεν απαιτεί νέο migration.
- **Τα migrations δεν εφαρμόζονται αυτόματα.** Η εκκίνηση ελέγχει αν εκκρεμούν και το καταγράφει
  ως warning με την εντολή που πρέπει να τρέξει. Το `Database.Migrate()` σε κάθε εκκίνηση θα ήταν
  επικίνδυνο σε production και θα δημιουργούσε συνθήκες ανταγωνισμού με πολλαπλά instances πίσω
  από τον reverse proxy.
- Η έκδοση του server δηλώνεται ρητά (`MariaDbServerVersion 11.4`) αντί για `AutoDetect`, που θα
  άνοιγε σύνδεση στο startup και θα εμπόδιζε την εκκίνηση όταν η βάση είναι προσωρινά κάτω.

---

## 6. Διαχείριση Credentials (απαίτηση: **τίποτα στο git**)

Στρατηγική τριών επιπέδων, με σειρά προτεραιότητας:

1. **`dotnet user-secrets`** (κύριο, για development). ✅ *υλοποιημένο*
   Το `RetroTools.Web.csproj` παίρνει `<UserSecretsId>`· τα secrets αποθηκεύονται στο
   `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` — **εκτός του repository**.
   Το `RetroTools.Data.Tests.csproj` μοιράζεται το ίδιο `UserSecretsId`, ώστε τα integration
   tests να βρίσκουν το ίδιο connection string χωρίς αντιγραφή.
   ```bash
   dotnet user-secrets set "ConnectionStrings:RetroTools" "Server=...;Port=3306;Database=DB_NAME;User ID=...;Password=...;" --project src/RetroTools.Web
   ```
2. **Environment variables** (για deployment): `ConnectionStrings__RetroTools`.
3. **`appsettings.Local.json`** (fallback), προστιθέμενο ρητά στο `.gitignore`.

### 6.1 Ποια μυστικά υπάρχουν

| Κλειδί ρύθμισης | Τι είναι |
|---|---|
| `ConnectionStrings:RetroTools` | Host, βάση, χρήστης, κωδικός MariaDB |
| `Authentication:GitHub:ClientId` / `:ClientSecret` | GitHub OAuth App |
| `Authentication:Google:ClientId` / `:ClientSecret` | Google Cloud OAuth 2.0 Client |

Και τα τρία ζευγάρια πάνε **αποκλειστικά** σε user-secrets ή environment variables.
Αν λείπουν τα OAuth κλειδιά, ο αντίστοιχος provider απλώς **δεν καταχωρείται** — η εφαρμογή
σηκώνεται κανονικά και δείχνει μόνο τα διαθέσιμα κουμπιά σύνδεσης.

### 6.2 Εγγυήσεις

- ✅ Το committed `appsettings.json` **δεν** περιέχει connection string ούτε OAuth κλειδιά.
- ✅ Committed template `appsettings.Local.json.example` με **placeholders μόνο**.
- ✅ `.gitignore` με ρητό, σχολιασμένο section για secrets — δημιουργήθηκε **πριν** από κάθε commit.
- ✅ Η εφαρμογή **αποτυγχάνει με καθαρό μήνυμα** στο startup αν λείπει το connection string
  (`ConnectionStringProvider.Require`), αντί για `NullReferenceException` στο πρώτο query.
- ✅ Τα logs τυπώνουν **μόνο το hostname** της βάσης, ποτέ ολόκληρο το connection string.
- Pre-commit έλεγχος (προαιρετικό): `git diff --cached` grep για `Password=`.

### 6.3 Κατάσταση σύνδεσης — επαληθευμένη ✅

Τα smoke tests στο `RetroTools.Data.Tests` (3/3 πέρασαν) επιβεβαίωσαν:

| Έλεγχος | Αποτέλεσμα |
|---|---|
| Σύνδεση & έκδοση | `11.4.3-MariaDB-deb11` |
| Τρέχουσα βάση | `retrotools` |
| Server charset | `utf8mb4` |
| Δικαιώματα DDL | CREATE / INSERT / SELECT / DROP TABLE OK |
| `MEDIUMBLOB` round-trip | OK (binary αναλλοίωτο) |
| UTF-8 ελληνικά | OK |
| EF Core 9 provider σε .NET 10 | OK |

---

## 7. Αρχιτεκτονική εφαρμογής

### 7.1 `RetroTools.Core` (χωρίς εξαρτήσεις)

| Namespace | Περιεχόμενο |
|---|---|
| `Platforms` | `PlatformDefinition`, `GraphicsMode`, `SpriteSizeRule`, `PixelAspect`, `PixelSlot`/`PixelSlotRole`, `PlatformCatalog` (static, τα δεδομένα του §3) |
| `Palettes` | `Rgb24`, `HardwareColor`, `PaletteProfile` (ZX D8/D7, C64 Pepto, CPC Nominal/Measured), `HardwarePalette` με αντίστροφη αναζήτηση hardware→index και nearest-colour σε γραμμικό RGB |
| `Model` | `SpriteModel`, `FrameBuffer`, `AttributeGrid`, `SpriteMapModel` |
| `Model` | `FrameBuffer` (indexed, 1 byte/pixel), `AttributeGrid` (ZX) |
| `Imaging` | `PngWriter` — indexed PNG (colour type 3) γραμμένος από το μηδέν· εξυπηρετεί και τις μικρογραφίες του UI και το export PNG του §8, χωρίς εξάρτηση από imaging framework |
| `Codecs` | `ISpriteCodec`, `SpriteCodecBase`, `CpcInterleavedCodec` (Mode 0/1/2/3), `LinearSpriteCodec` (C64 & ZX, MSB-first), `MaskCodec`, `SpriteCodecs` factory |
| `Codecs.Export` | `AsmZ80Exporter`, `Asm6502Exporter`, `CHeaderExporter`, `BinExporter`, `PngExporter`, `JsonExporter` |
| `Validation` | `ISpriteValidator` ανά πλατφόρμα (alignment, χρώματα/cell, clash detection) |
| `Serialization` | `RsprContainer` (read/write, deflate) |

**Το `Core` θα έχει το πυκνότερο unit-test coverage** — round-trip tests `encode(decode(x)) == x` για κάθε mode.

### 7.2 `RetroTools.Data`

- `RetroToolsDbContext`, entity configurations (Fluent API), repositories (`IProjectRepository`, `ISpriteRepository`, …), unit-of-work μέσω `DbContext`.
- Optimistic concurrency με `row_version` στα `projects`/`sprites`.

### 7.3 `RetroTools.Web`

**MVC μέρος** (controllers + Razor views):
- `HomeController` — landing, επιλογή πλατφόρμας
- `ProjectsController` — λίστα/δημιουργία/διαγραφή/duplicate projects
- `LibraryController` — αναζήτηση sprites, tags
- **API controllers** (`[ApiController]`, route `/api/...`):

| Endpoint | Μέθοδοι |
|---|---|
| `/api/platforms` | GET (catalog: modes, παλέτες, περιορισμοί — τροφοδοτεί τον editor) |
| `/api/projects` `/api/projects/{id}` | GET, POST, PUT, DELETE |
| `/api/projects/{id}/sprites` | GET, POST |
| `/api/sprites/{id}` | GET, PUT, DELETE |
| `/api/sprites/{id}/frames/{index}` | GET, PUT (pixel buffer) |
| `/api/spritemaps` `/api/spritemaps/{id}` | GET, POST, PUT, DELETE |
| `/api/export/sprite/{id}?format=` | GET (`asm`, `bin`, `png`, `c`, `json`) |
| `/api/export/spritemap/{id}?format=` | GET |
| `/api/import` | POST (png / json / spd) |
| `/signin/{provider}` `/signout` `/signin-github` `/signin-google` | OAuth challenge & callbacks |
| `/api/me` | GET (τρέχων χρήστης, συνδεδεμένοι providers) |

Όλα τα `/api/*` απαιτούν authenticated χρήστη πλην του `/api/platforms` (στατικά δεδομένα υλικού).

### 7.6 Ανάγνωση ≠ εγγραφή (η παγίδα των query filters)

Τα global query filters του §5.2 αφήνουν να φανούν και τα **δημόσια** projects — σωστό
για ανάγνωση. Αν όμως μια διαδρομή εγγραφής χρησιμοποιούσε το ίδιο ερώτημα, κάθε δημόσιο
project θα γινόταν **εγγράψιμο από οποιονδήποτε**: το φίλτρο θα το επέστρεφε κανονικά και
το `SaveChanges` θα περνούσε.

Γι' αυτό ο [`ProjectAccess`](src/RetroTools.Web/Services/ProjectAccess.cs) έχει δύο
ξεχωριστές οικογένειες μεθόδων:

| Μέθοδος | Τι επιστρέφει |
|---|---|
| `FindReadable*` | Δικά μου **και** δημόσια — βασίζεται στα query filters |
| `FindWritable*` | **Μόνο** δικά μου — ρητός έλεγχος `OwnerId == currentUser` |

Κάθε `POST` / `PUT` / `DELETE` περνά υποχρεωτικά από `FindWritable*`. Καλύπτεται από test
που δημοσιοποιεί project, επιβεβαιώνει ότι ο άλλος χρήστης το διαβάζει, και ότι το `PUT`
και το `DELETE` του δίνουν 404.

**Γιατί 404 και όχι 403:** ένα 403 θα επιβεβαίωνε ότι το αντικείμενο υπάρχει, επιτρέποντας
σε κάποιον να απαριθμήσει ids και να μάθει πόσα projects έχουν οι άλλοι. Το 404 δεν
διαρρέει τίποτα.

**401 αντί για redirect στα API:** τα cookie events επιστρέφουν καθαρό 401/403 όταν η
διαδρομή αρχίζει με `/api`. Ένα 302 προς HTML σελίδα σύνδεσης είναι άχρηστο για `fetch()`.

**Blazor μέρος** (Interactive Server, per-page render mode):
- `/editor/sprite/{id}` — ο pixel editor
- `/editor/spritemap/{id}` — ο spritemap composer

### 7.4 Ο pixel editor (σχεδιαστική λεπτομέρεια)

Το κρίσιμο σημείο απόδοσης. Σχέδιο:

- Ένα JS module `wwwroot/js/pixel-canvas.js` κατέχει: το `<canvas>`, ένα offscreen `ImageData` με το indexed buffer, το ζουμ/pan και τα mouse events.
- Το JS ζωγραφίζει **άμεσα, τοπικά** (μηδενικό latency) και στέλνει στον Blazor component **batched deltas** (`{x, y, colorIndex}[]`) σε `requestAnimationFrame` / on mouse-up.
- Ο Blazor component κρατά το **authoritative** `FrameBuffer`, το undo/redo stack (command pattern) και κάνει autosave (debounced, ~2 s) μέσω του repository.
- Αλλαγή παλέτας/mode → ο server στέλνει νέο LUT στο JS, redraw χωρίς να αγγίξει τα pixel data.

**Εργαλεία editor:** pencil, line, rectangle (filled/outline), ellipse, flood fill, colour-replace, select/move, flip H/V, rotate 90°, shift (wrap), grid overlay (pixel + 8×8 cell), onion skin, mirror/symmetry, zoom 1×–32×, εικόνα αναφοράς (reference image overlay).

**Panels:** palette picker (με hardware color chooser για CPC), frame timeline + animation preview στο σωστό pixel aspect ratio, sprite list, layer "mask", validation warnings (live attribute-clash overlay για Spectrum, χρώματα > max για C64 MC).

### 7.4.1 Παγίδες που εντοπίστηκαν στην υλοποίηση (M5)

Τρία λάθη που δεν φαίνονται στον κώδικα αλλά σπάνε στην πράξη — καταγράφονται
ώστε να μην επαναληφθούν:

1. **`OnAfterRenderAsync(firstRender: true)` τρέχει πριν φορτώσουν τα δεδομένα.**
   Με ασύγχρονο `OnInitializedAsync`, η πρώτη απόδοση γίνεται ενώ το sprite είναι
   ακόμη `null` και το canvas δεν υπάρχει στο DOM. Ο συνηθισμένος έλεγχος
   `if (!firstRender) return;` αποκλείει τότε την αρχικοποίηση **για πάντα**.
   Σωστό: αρχικοποίηση στην πρώτη απόδοση όπου υπάρχουν και δεδομένα και στοιχείο.
2. **Τα `byte[]` δεν περνούν από JS σε .NET ως απλά arrays.** Ο Blazor τα
   μεταχειρίζεται ειδικά (περιμένει αναφορά σε δυαδική μεταφορά), οπότε ένα
   `[1,2,3]` από JavaScript αποτυγχάνει στην αποσειριοποίηση **χωρίς σφάλμα στην
   κονσόλα** — η πινελιά απλώς δεν έφτανε ποτέ. Οι υπογραφές `[JSInvokable]`
   χρησιμοποιούν `int[]` και η μετατροπή γίνεται σε C#.
3. **Το `DbContextOptions` είναι scoped.** Μια singleton υπηρεσία που το καταναλώνει
   ρίχνει την εφαρμογή στο startup. Ο `EditorDataService` είναι scoped.

### 7.5 Ταυτοποίηση — γιατί δεν δένουμε λογαριασμούς αυτόματα

Το προφανές θα ήταν: αν κάποιος συνδεθεί με GitHub και το email του ταιριάζει με
υπάρχοντα λογαριασμό Google, να τα ενώσουμε. **Δεν το κάνουμε**, και αυτό είναι
συνειδητή απόφαση ασφαλείας.

Το auto-linking βάσει email είναι γνωστός δρόμος κατάληψης λογαριασμού: αν ένας
provider επιστρέψει email που δεν έχει επαληθεύσει, αρκεί κάποιος να δηλώσει το email
του θύματος για να αποκτήσει πρόσβαση σε όλα του τα projects. Το GitHub δεν εκθέτει
την κατάσταση επαλήθευσης στο βασικό endpoint, οπότε δεν μπορούμε καν να το ελέγξουμε
αξιόπιστα.

Αντ' αυτού:

| Κατάσταση | Συμπεριφορά |
|---|---|
| Υπάρχει ήδη η σύνδεση (provider + key) | Κανονική είσοδος, ενημέρωση προφίλ |
| Άγνωστη σύνδεση, άγνωστο email | Νέος λογαριασμός |
| Άγνωστη σύνδεση, **γνωστό email** | **Ματαίωση** — ο χρήστης οδηγείται στο `/account/link-required` με οδηγία να συνδεθεί με τον αρχικό provider και να δέσει τον δεύτερο από τις ρυθμίσεις |

Το δέσιμο δεύτερου provider γίνεται **μόνο** από ήδη συνδεδεμένο χρήστη
(`UserProvisioningService.LinkAsync`), όπου η ταυτότητα είναι αποδεδειγμένη.

Δύο ακόμη λεπτομέρειες χρονισμού και ασφάλειας:

- Το provisioning τρέχει στο **`OnTicketReceived`**, όχι στο `OnCreatingTicket`: πρέπει να
  μπορούμε να ματαιώσουμε τη σύνδεση **πριν** εκδοθεί cookie. Αλλιώς ο χρήστης θα έμενε
  authenticated χωρίς αντίστοιχο λογαριασμό — θα έβλεπε μια άδεια εφαρμογή χωρίς εξήγηση.
- Η **αποσύνδεση είναι μόνο POST** με antiforgery token. Ένα GET `/signout` μπορεί να
  ενεργοποιηθεί από `<img src>` σε ξένη σελίδα και να πετάει τον χρήστη έξω.
- Ο `returnUrl` περνά από `Url.IsLocalUrl` — αλλιώς είναι open redirect, δηλαδή έτοιμο
  εργαλείο phishing με το domain μας ως δόλωμα.
- Κανένας από τους δύο providers δεν χαρτογραφεί την εικόνα προφίλ από προεπιλογή·
  δηλώνονται ρητά τα claim actions για `avatar_url` (GitHub) και `picture` (Google).

---

## 8. Export / Import

### 8.1 Export

| Μορφή | Πλατφόρμες | Περιεχόμενο |
|---|---|---|
| `.asm` Z80 | CPC, ZX | **rasm** διάλεκτος (επιλογή χρήστη): `defb`, labels ανά sprite/frame, equates για w/h. Το ίδιο output δέχονται και sjasmplus/pasmo. |
| `.asm` 6502 | C64 | **ACME** διάλεκτος: `!byte`, sprite pointers, `*=` origin |
| `.prg` | C64 | Δυαδικό με 2-byte load address — **φορτώνει απευθείας στον VICE** (`LOAD"*",8,1` ή drag & drop) |
| `.bin` | όλες | Raw packed bytes, όπως θα κάθονταν στη μνήμη |
| `.h` / `.c` | όλες | `const unsigned char sprite_x[] = {…}` (z88dk / cc65 / SDCC) |
| `.png` | όλες | Preview με σωστό aspect ratio, x1/x2/x4 |
| `.json` | όλες | Πλήρες project (lossless) — μορφή import/backup |
| `.spd` | C64 | SpritePad συμβατότητα (stretch goal, M8) |

Επιλογές export: σειρά δεδομένων (row-major / column-major / screen-interleaved), συμπερίληψη mask, padding, χρώμα διαφάνειας, μπλοκ 64 bytes για C64.

### 8.2 Import

#### JSON project ✅

Πλήρες project σε ένα αρχείο `.retrotools.json`: αντίγραφο ασφαλείας, μεταφορά ανάμεσα
σε εγκαταστάσεις, ή αρχείο δίπλα στον κώδικα του παιχνιδιού μέσα στο git.

Αρχές σχεδίασης:

| Απόφαση | Γιατί |
|---|---|
| Pixels ως base64 του **ωμού indexed buffer**, όχι RSPR | Δημόσια μορφή ανταλλαγής· δεν πρέπει να δεσμεύεται από την εσωτερική μας κωδικοποίηση αποθήκευσης |
| Τα `id` είναι **τοπικά του εγγράφου** | Ένα αρχείο δεν μπορεί να δείξει σε δεδομένα άλλου χρήστη· αντιστοιχίζονται σε νέα κατά την εισαγωγή |
| Η εισαγωγή δημιουργεί **πάντα νέο** project | Δεν αντικαθιστά ποτέ δουλειά· ο ιδιοκτήτης είναι πάντα ο ανεβάζων, ό,τι κι αν λέει το αρχείο |
| `format` και `version` **χωρίς προεπιλογή στο μοντέλο** | Αν έμπαιναν από initializer, η αποσειριοποίηση θα τα συμπλήρωνε μόνη της και **οποιοδήποτε JSON** θα περνούσε για project του RetroTools |
| Άγνωστη έκδοση → απόρριψη | Μια σιωπηλά μισοδιαβασμένη δουλειά είναι χειρότερη από ένα καθαρό σφάλμα |
| Όλα τα σφάλματα μαζί | Ο χρήστης διορθώνει μία φορά, όχι επτά |
| Ρητά ανώτατα όρια (2048 sprites, 256 καρέ, 32 MB) | Χωρίς αυτά ένα κακόβουλο αρχείο εξαντλεί τη μνήμη πριν καν φτάσουμε στην επικύρωση |

Ο validator ελέγχει επίσης ό,τι και το API: διαστάσεις έναντι `SpriteSizeRule`, τιμές
pixel έναντι `MaxPixelValue`, μήκος attributes, ακεραιότητα αναφορών (ομάδες, κελιά →
sprites) και μοναδικότητα αναγνωριστικών.

#### PNG ✅

Ο [`PngReader`](src/RetroTools.Core/Imaging/PngReader.cs) δέχεται και τους πέντε τύπους
φίλτρων (None/Sub/Up/Average/Paeth), colour types 0/2/3/4/6 και βάθη 1 έως 16 bit.
Το interlace (Adam7) **απορρίπτεται ρητά** με μήνυμα που λέει τι να κάνει ο χρήστης —
είναι σπάνιο σε pixel art και θα διπλασίαζε την πολυπλοκότητα.

Ο [`ImageQuantizer`](src/RetroTools.Core/Imaging/ImageQuantizer.cs) έχει δύο στρατηγικές:

| Στρατηγική | Συμπεριφορά |
|---|---|
| **AutoAssign** (προεπιλογή) | Διαλέγει τα χρώματα υλικού που καλύπτουν καλύτερα την εικόνα. Πρώτα στρογγυλοποιεί κάθε χρώμα στο πλησιέστερο χρώμα υλικού και **μετά** μετρά συχνότητες — αλλιώς κοντινές αποχρώσεις που καταλήγουν στο ίδιο χρώμα υλικού θα σπαταλούσαν δύο slots |
| **UseProjectPalette** | Η εικόνα προσαρμόζεται στα υπάρχοντα slots· η παλέτα δεν αλλάζει |

Η απόσταση μετριέται σε **γραμμικό** χώρο με συντελεστές φωτεινότητας: στον χώρο sRGB
το 0x80 δεν είναι οπτικά μισό, οπότε μια απλή ευκλείδεια απόσταση θα διάλεγε συστηματικά
λάθος αποχρώσεις — ακριβώς στα μεσαία επίπεδα του CPC.

Τα διαφανή pixels πάνε στο slot διαφάνειας του mode· και το αντίστροφο, ένα αδιαφανές
pixel δεν επιτρέπεται να καταλήξει εκεί επειδή έτυχε να μοιάζει με ό,τι έχει ανατεθεί.

#### Raw `.bin` — **εκκρεμεί**

Με ρητή δήλωση mode και διαστάσεων. Χαμηλής προτεραιότητας: το PNG και το JSON
καλύπτουν τις πραγματικές ροές εργασίας.

### 8.3 Attribute clash: πού ελέγχεται πραγματικά

Το M5 δήλωνε «live attribute-clash overlay» στον editor. **Ήταν λάθος** και ο έλεγχος
αφαιρέθηκε: σε per-cell modes το indexed buffer κρατά ακριβώς `MaxColorsPerCell` δυνατές
τιμές (0 = PAPER, 1 = INK στο Spectrum). Ένα κελί **δεν μπορεί** να αποκτήσει τρίτο χρώμα —
το μοντέλο το αποκλείει εξ ορισμού, που είναι χαρακτηριστικό και όχι έλλειψη: το εργαλείο
δεν μπορεί να παραγάγει sprite που δεν τρέχει.

Η πραγματική απώλεια χρωμάτων συμβαίνει στην **εισαγωγή εικόνας** και αναφέρεται εκεί,
με αριθμούς: πόσα κελιά της πηγαίας εικόνας είχαν πάνω από το όριο και ποιο ήταν το χειρότερο.

---

## 9. Κανόνες Validation ανά πλατφόρμα

| Κανόνας | ZX | C64 | CPC |
|---|---|---|---|
| Width alignment | %8 | %8 (HW sprite: ακριβώς 24 ή 12) | %2 / %4 / %8 ανά mode |
| Height | ελεύθερο | HW sprite: ακριβώς 21 | ελεύθερο |
| Max χρώματα ανά 8×8 | 2 (+bright κοινό) | — | — |
| Max χρώματα ανά sprite | — | 2 (hires) / 4 (MC) | 16 / 4 / 2 ανά mode |
| Κοινά χρώματα | — | MC0/MC1 κοινά σε όλα τα sprites | — |
| Δείκτης παλέτας εντός ορίων | ✔ | ✔ | ✔ |

Τα warnings είναι **μη-μπλοκαριστικά** (ο χρήστης μπορεί να σχεδιάσει ελεύθερα) αλλά **μπλοκάρουν το export** εκτός αν επιλέξει "force".

---

## 10. Φάσεις υλοποίησης

| Φάση | Παραδοτέο | Ορισμός "τελείωσε" |
|---|---|---|
| **M0 – Setup** ✅ | Solution, 5 projects, `Directory.Build.props`, `.gitignore`, user-secrets, hosting για service/reverse-proxy, smoke tests MariaDB | ✅ `dotnet build` καθαρό (0 warnings)· 3/3 DB tests πράσινα· η εφαρμογή σηκώνεται και συνδέεται |
| **M1 – Platform catalog** ✅ | `PlatformCatalog` με όλα τα δεδομένα του §3, palette profiles, `PixelSlot` roles | ✅ 121 unit tests πράσινα: 27 CPC χρώματα + base-3 invariant, 32 HW inks→27 FW, 15 ZX μοναδικά, 16 C64 Pepto, διάταξη μνήμης ZX, κοινοί καταχωρητές C64 |
| **M2 – Codecs** ✅ | CPC Mode 0/1/2/3 interleaved packing, C64 hires/MC 63-byte, ZX bitmap + attributes + mask, `RsprContainer` | ✅ 204 tests πράσινα: εξαντλητικός έλεγχος 256 συνδυασμών ανά CPC mode έναντι ανεξάρτητου τύπου αναφοράς, round-trips, διατάξεις μνήμης CPC/ZX |
| **M3 – Data layer** ✅ | 12 EF Core entities (μαζί με `users`/`user_logins`), migration `InitialSchema`, seeder από `PlatformCatalog`, global query filters ιδιοκτησίας σε **όλες** τις οντότητες | ✅ Το migration εφαρμόστηκε στη ζωντανή `retrotools`· 21 integration tests πράσινα, περιλαμβανομένων: ο Β δεν βλέπει project/sprite/frame του Α, BLOB round-trip, cascade, concurrency, utf8mb4 |
| **M3.5 – Auth** ✅ | Cookie auth + GitHub & Google OAuth, `UserProvisioningService`, `/api/me`, `/account/*` | ✅ 12 tests provisioning· και οι δύο challenges ανακατευθύνουν σωστά (επαληθευμένο με ψεύτικα κλειδιά)· χωρίς κλειδιά η εφαρμογή σηκώνεται και αναφέρει `providers: false`. **Δεν** γίνεται αυτόματο δέσιμο λογαριασμών βάσει email — βλ. §7.5 |
| **M4 – REST API** ✅ | `/api/platforms`, `/api/projects`, `/api/sprites`(+frames), `/api/spritemaps`, DTOs, validation έναντι `PlatformCatalog`, `ProjectAccess` | ✅ 21 integration tests πάνω από πραγματική HTTP pipeline: 401 αντί redirect, 404 αντί 403, **δημόσιο project αναγνώσιμο αλλά ποτέ εγγράψιμο**, επιβολή διαστάσεων & ορίων χρωμάτων |
| **M5 – Pixel editor** ✅ | `pixel-canvas.js` + Blazor, 7 εργαλεία, undo/redo, autosave, palette panel, clash overlay, σελίδες projects/sprites | ✅ Επαληθευμένο στον browser: σχεδίαση → autosave → reload → τα pixels είναι εκεί· undo/redo ανά πινελιά· canvas 384×192 για sprite 16×16 (αναλογία 2:1 του Mode 0) |
| **M6 – Groups & Spritemaps** ✅ | Ομάδες, spritemap composer με flip flags, `PngWriter` για μικρογραφίες | ✅ Επαληθευμένο: 4×4 spritemap, τοποθέτηση sprites, save, reload — τα κελιά είναι εκεί. Οι μικρογραφίες αποκωδικοποιούνται από τον browser (96×48, 176 bytes) |
| **M7 – Export** ✅ | `.bin`, Z80 (rasm), 6502 (ACME), `.prg`, C header, PNG· API `/api/export` με φιλτράρισμα ανά πλατφόρμα· κουμπιά στον editor | ✅ 26 tests. C64 sprite → ακριβώς 63 bytes με σωστά bits· `.prg` με little-endian διεύθυνση φόρτωσης· CPC `.asm` με `defb &AA` και τιμές Gate Array στα σχόλια· επαληθευμένο κατέβασμα από τον browser |
| **M7b – JSON project** ✅ | `ProjectDocument` + validator + serializer, `/api/projects/{id}/document`, `/api/projects/import`, UI | ✅ 31 tests. Round trip μέσω API με πανομοιότυπα pixels· ο ιδιοκτήτης είναι πάντα ο ανεβάζων· αλλοιωμένο αρχείο απορρίπτεται χωρίς να δημιουργηθεί τίποτα |
| **M7c – Import PNG** ✅ | `PngReader` (5 φίλτρα, colour types 0/2/3/4/6, βάθη 1–16 bit), `ImageQuantizer` με αυτόματη ανάθεση παλέτας, API + UI | ✅ 38 tests. Κάθε τύπος φίλτρου δίνει πανομοιότυπη εικόνα· round trip με τον δικό μας writer· επαληθευμένο στον browser: gradient 256 χρωμάτων → 13 pens CPC |
| **M8 – Deployment & polish** | systemd unit + Windows service + nginx δείγματα, data-protection keys, animation preview, clash overlay, tags/search, `.spd` | Η εφαρμογή τρέχει ως service πίσω από proxy με λειτουργικό OAuth |

Κάθε φάση κλείνει με πράσινα tests και commit.

---

## 11. Testing

- **Unit (xUnit):** codecs (round-trip + γνωστά byte patterns), palette mapping, validators, RSPR container.
- **Integration:** EF Core σε πραγματική MariaDB (ξεχωριστό ξεχωριστό schema δοκιμών ή transaction rollback ανά test).
- **Χειροκίνητη επαλήθευση:** εξαγωγή C64 sprite → φόρτωμα σε VICE· CPC Mode 0 sprite → WinAPE· ZX → Fuse. Το πιο αξιόπιστο acceptance test.

---

## 12. Απόδοση & όρια

- Max μέγεθος sprite: 128×128 (16 KB indexed) — αρκετά πάνω από ό,τι έχει νόημα εποχιακά.
- Max frames ανά sprite: 64. Max sprites ανά project: 1024 (soft limits, configurable).
- Autosave debounce 2 s· delta batching ανά frame· χωρίς `StateHasChanged` ανά pixel.
- Response caching στο `/api/platforms` (στατικά δεδομένα).

---

## 13. Ρίσκα & μετριασμοί

| Ρίσκο | Μετριασμός |
|---|---|
| Blazor Server latency στο ζωγράφισμα | JS canvas κατέχει το input loop· ο server βλέπει μόνο batched deltas |
| Σύνδεση σε remote MariaDB (latency/διακοπές) | Connection resiliency (`EnableRetryOnFailure`), autosave με retry, τοπικό dirty state |
| `LangVersion 10` σε .NET 10 templates | ✅ Επιβεβαιώθηκε στο M0: build καθαρό, 0 warnings· απαγορευμένα features καταγράφονται στο README |
| **EF Core 9 πάνω σε .NET 10** (δεν υπάρχει Pomelo για EF 10) | ✅ Επαληθεύτηκε με live queries στη MariaDB 11.4.3. Τα πακέτα είναι καρφωμένα στο 9.0.x με σχόλιο στο `.csproj`. Έξοδος διαφυγής αν χρειαστεί: MySqlConnector + Dapper με DbUp migrations |
| OAuth redirect URIs πίσω από proxy | `UseForwardedHeaders` με ρητούς `KnownProxies`· τεκμηρίωση στο `docs/deploy/` |
| Blazor Server μέσω proxy χωρίς WebSockets | Δείγμα nginx config με upgrade headers· fallback long-polling τεκμηριωμένο ως γνωστό πρόβλημα |
| Απόκλιση παλετών από τους emulators | Palette profiles ως ρύθμιση προβολής· τα δεδομένα αποθηκεύονται πάντα ως hardware indices |
| Ακρίβεια CPC hardware ink table | Ο πίνακας §3.3 επαληθεύτηκε από cpctech/Grimware· καλύπτεται με unit test (32→27, 5 διπλότυπα) |

---

## 14. Μελλοντικά extensions

- ZX: ULAplus (64 χρώματα), Timex hi-colour/hi-res, ZX Spectrum Next (256 χρώματα, hardware sprites 16×16).
- C64: char/tile editor, SpritePad + CharPad import/export, sprite multiplexer preview.
- CPC: CPC Plus (4096 χρώματα, 16 hardware sprites), rasters/split palettes, OCP Art Studio.
- Γενικά: κοινόχρηστη βιβλιοθήκη sprites, versioning/history, εξαγωγή σε tilemap engines, PWA/offline.

---

## 15. Αποφάσεις (κλειστά ερωτήματα)

| # | Ερώτημα | Απόφαση | Επίπτωση στο πλάνο |
|---|---|---|---|
| 1 | Λογαριασμοί χρηστών | **Multi-user από την αρχή**, login με **GitHub** και **Google** | Νέοι πίνακες `users` / `user_logins` (§5.1)· νέα φάση **M3.5**· `owner_id` γίνεται `NOT NULL`· global query filter ιδιοκτησίας· **χωρίς** ASP.NET Identity (θα έσπαγε το Pomelo) |
| 2 | Target framework | **net10.0** + `LangVersion 10.0` | EF Core καρφωμένο στο 9.0.x — επαληθευμένο ότι δουλεύει (§2) |
| 3 | Προτεραιότητα πλατφόρμας | **Και οι τρεις παράλληλα** ανά φάση | Καμία — ήταν ήδη η υπόθεση του πλάνου |
| 4 | Assembler διάλεκτοι | Z80 → **rasm**· C64 → **VICE** | §8.1 |
| 5 | Deployment | **Self-hosted service** (Windows Service / systemd) πίσω από **reverse proxy** | Νέο §2.2· `UseWindowsService()` + `UseSystemd()` + forwarded headers + PathBase — υλοποιημένα στο M0· δείγματα configs στο M8 |

### 15.1 Διευκρίνιση για το C64 export

Ο **VICE είναι emulator, όχι assembler** — δεν υπάρχει "διάλεκτος VICE" για πηγαίο κώδικα.
Ερμηνεύω την απάντηση ως *«το export πρέπει να δουλεύει με τον VICE»* και παραδίδω **δύο** πράγματα:

1. **`.prg`** — δυαδικό με 2-byte load address, φορτώνει κατευθείαν στον VICE (drag & drop ή `LOAD"*",8,1`).
   Αυτό είναι το «τρέχει στον VICE» παραδοτέο.
2. **`.asm` σε ACME** — η πιο διαδεδομένη ανοιχτή διάλεκτος 6502 στη σκηνή του C64, για όποιον
   θέλει πηγαίο κώδικα.

Αν εννοούσες συγκεκριμένα **KickAssembler** ή τη σύνταξη του **VICE monitor** (`a c000 lda #$00`),
πες το και προσθέτω τον exporter — είναι μικρή δουλειά αφού τα bytes παράγονται ήδη από τα codecs.

### 15.2 Τι χρειάζομαι από εσένα για το M3.5 (auth)

Δύο OAuth applications — τα δημιουργείς εσύ, τα ClientId/ClientSecret μπαίνουν σε user-secrets:

- **GitHub:** Settings → Developer settings → OAuth Apps → New. Callback URL: `https://<domain>/signin-github`
- **Google:** Google Cloud Console → APIs & Services → Credentials → OAuth client ID (Web).
  Authorized redirect URI: `https://<domain>/signin-google`

Για τοπική ανάπτυξη χρησιμοποίησε `https://localhost:7xxx/signin-github` κ.λπ.
Μέχρι να τα δώσεις, η εφαρμογή σηκώνεται κανονικά — απλώς δεν εμφανίζει τα κουμπιά σύνδεσης.


---

## Πηγές

- [Gate Array – cpctech](https://cpctech.cpcwiki.de/docs/garray.html) (πίνακας hardware ink 0x40–0x5F ↔ firmware 0–26)
- [Gate Array – Grimware](https://www.grimware.org/doku.php/documentations/devices/gatearray) (pixel bit encoding Mode 0/1/2)
- [Calculating the color palette of the VIC-II – Pepto](https://www.pepto.de/projects/colorvic/) (C64 παλέτα)
- [ZX Spectrum Palette – Lospec](https://lospec.com/palette-list/zx-spectrum)
- [ZX Spectrum graphic modes – Wikipedia](https://en.wikipedia.org/wiki/ZX_Spectrum_graphic_modes)
- [The ZX Spectrum Color Palette, Resolution and Attributes – retrotechlab](https://www.retrotechlab.com/the-zx-spectrum-color-palette-resolution-and-attributes/)
- [Amstrad CPC – Wikipedia](https://en.wikipedia.org/wiki/Amstrad_CPC)

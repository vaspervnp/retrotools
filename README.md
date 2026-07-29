# RetroTools — Sprite & Spritemap Studio

Web εργαλείο σχεδίασης **sprites** και **spritemaps** για τους 8-bit υπολογιστές
**Amstrad CPC**, **Commodore 64** και **ZX Spectrum**, με σεβασμό στους αυθεντικούς
περιορισμούς κάθε μηχανής (παλέτες υλικού, γραφικά modes, byte alignment, attribute clash).

> **Κατάσταση: πλήρες** — φτιάχνεις project, σχεδιάζεις sprites ή **τα εισάγεις από PNG**,
> τα οργανώνεις σε ομάδες και spritemaps, τα **εξάγεις σε κώδικα που τρέχει**, και παίρνεις
> **πλήρες αντίγραφο ασφαλείας σε JSON**.
> **369 tests πράσινα**, 82 πάνω σε πραγματική MariaDB.
> Δες το [plan.md](plan.md) για τη μελέτη και ό,τι απομένει.

---

## Τι κάνει

- **Pixel editor** με ζουμ, εργαλεία σχεδίασης, undo/redo, frames & animation preview,
  και σωστό **pixel aspect ratio** ανά mode (τα CPC Mode 0 pixels *είναι* φαρδιά).
- **Παλέτες υλικού**: 27 χρώματα CPC (με programmable pens), 16 σταθερά C64 (Pepto),
  15 ZX Spectrum (8 βασικά × BRIGHT).
- **Εισαγωγή από PNG** με αυτόματη επιλογή των καταλληλότερων χρωμάτων υλικού, και
  ειλικρινή αναφορά τι χάθηκε: πόσα χρώματα στρογγυλοποιήθηκαν, πόσα κελιά ξεπερνούσαν
  το όριο του Spectrum.
- **Επιβολή περιορισμών υλικού**: το εργαλείο δεν σε αφήνει να φτιάξεις sprite που δεν
  τρέχει — byte alignment ανά mode, καρφωμένες διαστάσεις για τα hardware sprites του C64,
  όρια χρωμάτων ανά mode.
- **Groups & Spritemaps**: οργάνωση sprites σε ομάδες και σε πλέγματα (animation strips,
  tilesets, character sets).
- **Save / Load** σε MariaDB.
- **Export** σε Z80 `defb` (rasm), 6502 (ACME), `.prg` που φορτώνει σε VICE, raw `.bin`,
  C headers και PNG. Κάθε πλατφόρμα βλέπει μόνο τις μορφές που της ταιριάζουν.
  Ο πηγαίος κώδικας φέρει σχόλια με τις **τιμές υλικού** της παλέτας — ό,τι χρειάζεται
  ο προγραμματιστής για να στήσει την οθόνη.
- **Αντίγραφο ασφαλείας σε JSON**: ολόκληρο project (sprites, καρέ, παλέτα, ομάδες,
  spritemaps) σε ένα αρχείο `.retrotools.json` που μπαίνει σε git δίπλα στον κώδικα
  του παιχνιδιού και ξαναφορτώνεται όποτε θες.
- **Multi-user** με σύνδεση GitHub / Google· κάθε project ανήκει στον χρήστη του.

## Υποστηριζόμενες πλατφόρμες — περίληψη

| | ZX Spectrum | Commodore 64 | Amstrad CPC |
|---|---|---|---|
| Παλέτα | 15 χρώματα | 16 σταθερά | 27 (16 pens επιλέξιμα) |
| Modes | 256×192, attribute 8×8 | hires 320×200 · multicolor 160×200 | Mode 0 160×200/16 · Mode 1 320×200/4 · Mode 2 640×200/2 |
| Hardware sprites | — | 8 × 24×21 (hires) / 12×21 (multicolor) | — |
| Sprite alignment | πλάτος %8 | πλάτος %8 (HW: 24) | πλάτος %2 / %4 / %8 ανά mode |

Αναλυτικά (bit layouts, διευθύνσεις μνήμης, πίνακες χρωμάτων): [plan.md §3](plan.md).

---

## Stack

- **C# 10** (`LangVersion 10.0`) σε **.NET 10**
- **ASP.NET Core MVC** (site + REST API) + **Blazor** Interactive Server (editor)
- **HTML canvas** + JS module για το per-pixel input loop
- **EF Core 9** + **Pomelo.EntityFrameworkCore.MySql 9.0.0** → **MariaDB 11**
- **Cookie auth + OAuth** (GitHub, Google) — χωρίς ASP.NET Identity
- **xUnit** για tests
- Self-hosted ως **Windows Service / systemd**, πίσω από reverse proxy

> ⚠ Τα πακέτα EF Core είναι **καρφωμένα στο 9.0.x**. Το Pomelo δεν έχει build για EF Core 10·
> αναβάθμιση θα σπάσει τον provider στο runtime. Δες [plan.md §2](plan.md).

---

## Quick start

### Προαπαιτούμενα

- .NET SDK 10 ([download](https://dotnet.microsoft.com/download))
- Πρόσβαση σε MariaDB 11 με μια αποκλειστική βάση για την εφαρμογή
- Git

### Εγκατάσταση

```bash
git clone <repo-url> retrotools
```

```bash
cd retrotools && dotnet restore
```

### Ρύθμιση σύνδεσης βάσης

Το connection string **δεν βρίσκεται ποτέ μέσα στο repository**. Δώσ' το με έναν από
τους παρακάτω τρόπους (η σειρά προτεραιότητας είναι από κάτω προς τα πάνω):

**1. User secrets (συνιστάται για development)**

```bash
dotnet user-secrets set "ConnectionStrings:RetroTools" "Server=YOUR_HOST;Port=3306;Database=DB_NAME;User ID=YOUR_USER;Password=YOUR_PASSWORD;" --project src/RetroTools.Web
```

**2. Environment variable (για deployment)**

```bash
export ConnectionStrings__RetroTools="Server=YOUR_HOST;Port=3306;Database=DB_NAME;User ID=YOUR_USER;Password=YOUR_PASSWORD;"
```

**3. `appsettings.Local.json`** — αντίγραψε το `appsettings.Local.json.example`,
συμπλήρωσε τις τιμές. Το αρχείο είναι στο `.gitignore`.

Αν λείπει το connection string, η εφαρμογή σταματά στο startup με ρητό μήνυμα.

### Ρύθμιση σύνδεσης GitHub / Google (προαιρετικό στο development)

Δημιούργησε δύο OAuth applications και δώσε τα κλειδιά με user-secrets:

- **GitHub** — Settings → Developer settings → OAuth Apps → New OAuth App.
  Authorization callback URL: `https://localhost:7xxx/signin-github`
- **Google** — Google Cloud Console → APIs & Services → Credentials → OAuth client ID (Web application).
  Authorized redirect URI: `https://localhost:7xxx/signin-google`

```bash
dotnet user-secrets set "Authentication:GitHub:ClientId" "YOUR_ID" --project src/RetroTools.Web
```

```bash
dotnet user-secrets set "Authentication:GitHub:ClientSecret" "YOUR_SECRET" --project src/RetroTools.Web
```

Το ίδιο για `Authentication:Google:ClientId` / `:ClientSecret`.
Αν λείπουν, ο αντίστοιχος provider απλώς δεν εμφανίζεται — η εφαρμογή σηκώνεται κανονικά.

#### Τοπική σύνδεση χωρίς OAuth

Για να δουλέψεις στο UI χωρίς να στήσεις OAuth apps, υπάρχει η διαδρομή
`/account/dev/signin` που σε συνδέει ως τοπικό δοκιμαστικό χρήστη.

> ⚠️ **Απαιτεί διπλή ενεργοποίηση:** περιβάλλον `Development` **και**
> `RetroTools:EnableDevSignIn = true` στο `appsettings.Development.json`
> (που είναι gitignored). Αν λείπει οποιοδήποτε από τα δύο, η διαδρομή επιστρέφει
> **404** σαν να μην υπάρχει. Μην ενεργοποιήσεις ποτέ αυτή τη ρύθμιση σε server.

### Δημιουργία σχήματος

```bash
dotnet ef database update --project src/RetroTools.Data --startup-project src/RetroTools.Web
```

### Εκτέλεση

```bash
dotnet run --project src/RetroTools.Web
```

### Tests

```bash
dotnet test
```

Τα integration tests που απαιτούν βάση γίνονται **skip** αυτόματα αν δεν υπάρχει
connection string — δεν αποτυγχάνουν σε CI χωρίς secrets.

---

## Deployment

Self-hosted ως service, πίσω από reverse proxy (nginx / Apache / IIS ARR / Caddy).
Οι σχετικές ρυθμίσεις είναι στο section `RetroTools` του `appsettings`:

| Ρύθμιση | Τι κάνει |
|---|---|
| `BehindReverseProxy` | Ενεργοποιεί το `X-Forwarded-*` processing (**απαραίτητο**, αλλιώς σπάει το OAuth callback) |
| `KnownProxies` / `KnownNetworks` | Ποιους proxies εμπιστευόμαστε (IP ή CIDR). Χωρίς αυτούς τα headers αγνοούνται — είναι spoofable |
| `TrustAnyProxy` | Παρακάμπτει τον παραπάνω έλεγχο. Μόνο αν η Kestrel δεν εκτίθεται |
| `PathBase` | Φιλοξενία κάτω από sub-path, π.χ. `/spritestudio` |
| `EnableHttpsRedirection` | Βάλ' το `false` όταν το TLS τερματίζει ο proxy |

Το `UseWindowsService()` / `UseSystemd()` ενεργοποιούνται μόνα τους όταν η εφαρμογή τρέχει
ως service· από κονσόλα είναι no-op.

> Ο **Blazor Server χρειάζεται WebSockets**. Ο proxy πρέπει να επιτρέπει το upgrade
> (`Upgrade` / `Connection` headers σε nginx), αλλιώς ο editor πέφτει σε long-polling.

---

## Δομή repository

```
retrotools/
├─ src/
│  ├─ RetroTools.Core/   # παλέτες, modes, codecs, validation — καθαρό domain, χωρίς εξαρτήσεις
│  ├─ RetroTools.Data/   # EF Core entities, DbContext, migrations
│  └─ RetroTools.Web/    # MVC controllers/views, REST API, Blazor editor, wwwroot
├─ tests/
├─ docs/
├─ plan.md               # τεχνική μελέτη + roadmap
└─ README.md
```

---

## Ασφάλεια & credentials

Στο repository **δεν μπαίνουν ποτέ**: connection strings, hostnames βάσης, usernames,
passwords. Τα `.gitignore` entries που το εγγυώνται:

```
appsettings.Local.json
appsettings.*.Local.json
appsettings.Development.json
.env
.env.*
secrets/
```

Αν χρειαστεί να προστεθεί νέα ρύθμιση με μυστικό, πηγαίνει σε user-secrets ή env var —
ποτέ σε committed αρχείο. Τα committed `*.example` αρχεία περιέχουν **μόνο placeholders**.

---

## Roadmap

Φάσεις M0 → M8, αναλυτικά στο [plan.md §10](plan.md). Συνοπτικά:
setup → platform catalog → codecs → data layer → CRUD/API → pixel editor →
spritemaps → export/import → polish (auth, animation, SpritePad συμβατότητα).

## Συνεισφορά

Ο κώδικας γράφεται σε **C# 10** — δεν επιτρέπονται features νεότερων εκδόσεων
(raw string literals, `required` members, primary constructors, collection expressions).

## Άδεια

TBD.

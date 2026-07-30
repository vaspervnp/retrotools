# Creating the sign-in keys (GitHub & Google)

> **Language:** English · [Ελληνικά](oauth-setup.el.md)

The application **has no passwords of its own**. Sign-in happens exclusively through
GitHub and Google, so you need one pair of keys per provider.

You can set up **just one** — the application starts normally and shows only the buttons
for the providers that have keys.

> For local development **with no OAuth at all** there is the `/account/dev/signin` route.
> See [Local sign-in without OAuth](../README.md#local-sign-in-without-oauth).

---

## What the callback URL is

Both providers will ask you for a return address. It is where they send the user back
after they approve the sign-in. The application's paths are **fixed**:

| Provider | Path |
|---|---|
| GitHub | `/signin-github` |
| Google | `/signin-google` |

So, for local development and for production respectively:

```
https://localhost:7042/signin-github
https://sprites.example.com/signin-github
```

Three things that break this often:

- **The scheme matters.** `http` and `https` are different URLs to the providers.
- **The port matters.** Check the real port in
  `src/RetroTools.Web/Properties/launchSettings.json` or in the "Now listening on…"
  message when you run the application.
- **No trailing `/`.**

You can register **several** callback URLs with the same provider (local and production),
so you do not need separate applications — unless you want separate keys for isolation,
which is good practice.

---

## GitHub

1. Go to <https://github.com/settings/developers> → **OAuth Apps** → **New OAuth App**.
2. Fill in:
   - **Application name**: what the user sees on the approval screen, e.g.
     `RetroTools Sprite Studio`
   - **Homepage URL**: `https://sprites.example.com` (or `https://localhost:7042`)
   - **Authorization callback URL**: `https://sprites.example.com/signin-github`
3. **Register application**.
4. Copy the **Client ID**.
5. Press **Generate a new client secret** and copy it **immediately** — GitHub never shows
   it again. If you lose it, generate a new one and delete the old.

You do not need to configure scopes: the application requests `user:email` itself, so that
it can read the user's email address.

> GitHub allows **one** callback URL per OAuth App. For local and production you need
> **two separate** applications — unlike Google.

---

## Google

1. Go to <https://console.cloud.google.com/>.
2. Select or create a **project** (top left).
3. **APIs & Services** → **OAuth consent screen**. This is a mandatory step before you can
   create credentials:
   - **User type**: `External` (unless you have Google Workspace and want to limit it to
     your organisation)
   - Fill in the application name, support email and contact email
   - **Scopes**: add `openid`, `.../auth/userinfo.email`, `.../auth/userinfo.profile`
   - Either leave it in **Testing** and add yourself to **Test users**, or press
     **Publish app** so anyone can sign in
4. **APIs & Services** → **Credentials** → **Create Credentials** → **OAuth client ID**.
5. **Application type**: `Web application`.
6. Under **Authorized redirect URIs** add the `/signin-google` path — you can put both the
   local and the production one in the same application:
   ```
   https://localhost:7042/signin-google
   https://sprites.example.com/signin-google
   ```
7. **Create**. The dialog shows the **Client ID** and **Client Secret** — copy both.

> While the consent screen stays in **Testing**, only the accounts you added as test users
> can sign in. Everyone else gets an `access_denied` error that does not explain why.

---

## Storing the keys

**Never in a file that goes into git.** Pick according to the environment:

### Locally, with the .NET SDK

```bash
dotnet user-secrets set "Authentication:GitHub:ClientId" "Iv1.xxxxxxxx" --project src/RetroTools.Web
```

```bash
dotnet user-secrets set "Authentication:GitHub:ClientSecret" "xxxxxxxx" --project src/RetroTools.Web
```

The same for `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret`.

### On a server, without the SDK

With [`retrotools-secrets`](../README.md#configuring-a-server-without-the-net-sdk):

```bash
./retrotools-secrets set "Authentication:GitHub:ClientSecret"
```

With no value on the command line it reads from stdin — **so the secret never lands in
your shell history**.

### With environment variables

The colon becomes a **double underscore**:

```bash
Authentication__GitHub__ClientId=Iv1.xxxxxxxx
Authentication__GitHub__ClientSecret=xxxxxxxx
```

---

## Verifying

```bash
./retrotools-secrets check
```

It checks the keys **in pairs**. A ClientId without a ClientSecret is not half a
configuration — the provider simply does not appear, and you go looking for why:

```
• Ο provider GitHub είναι μισο-ρυθμισμένος — λείπει: Authentication:GitHub:ClientSecret.
  Ο provider δεν θα ενεργοποιηθεί.
```

Then start the application and check:

```bash
curl https://sprites.example.com/account/providers
```

```json
{"github":true,"google":true}
```

---

## Common errors

| What you see | What is wrong |
|---|---|
| `redirect_uri_mismatch` (Google) | The URI in the console does not match **exactly** — scheme, port, path, trailing `/` |
| `The redirect_uri MUST match` (GitHub) | Same problem; GitHub accepts only one callback URL per application |
| `access_denied` on Google | The consent screen is in Testing and the account is not a test user |
| The sign-in button does not appear | The ClientId or ClientSecret is missing — run `check` |
| 400 "provider not available" | Same problem, from the API's side |
| The callback ends up on `http://` behind a proxy | `BehindReverseProxy: true` or `KnownProxies` is missing; see [Deployment](../README.md#deployment) |
| "An account with this email already exists" | Expected. Sign in with the original provider and link the second one from your settings — we do not merge accounts by email automatically, because that is an account-takeover route |

---

## Rotating or replacing a leaked key

Client secrets are revocable. If one leaks:

1. Create a new secret at the provider.
2. Update it with `retrotools-secrets set …`.
3. Restart the application — the keys are read at startup.
4. **Delete the old one** at the provider.

The order matters: if you delete first, sign-in breaks in the gap. A leaked client secret
lets a third party impersonate your application on an approval screen — treat it as a
password, not as an identifier.

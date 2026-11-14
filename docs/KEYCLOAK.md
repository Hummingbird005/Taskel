# Signing in with Keycloak

Taskel signs people in through Keycloak with OpenID Connect (authorization code flow with PKCE). This guide
sets up the Keycloak side and connects Taskel to it. How Taskel treats the accounts (creation, linking, roles,
groups, sign-out) is described in the README, under *Keycloak sign-in*.

## Try it locally

The repository has a development realm with a client, roles, groups and four users:

```bash
docker compose --profile keycloak up -d   # Keycloak on http://localhost:8080, realm "taskel"
```

Then, with the API and the frontend running (see *Running locally* in the README):

1. Sign in to Taskel as the demo administrator (`admin@taskel.example` / `Admin-Demo-2026`).
2. Open **Setări sistem → Keycloak** and fill in:

   | Field | Value |
   |---|---|
   | Permite conectarea cu Keycloak | on |
   | Adresa realm-ului | `http://localhost:8080/realms/taskel` |
   | Client ID | `taskel` |
   | Secretul clientului | `taskel-dev-secret` |
   | Cere HTTPS pentru Keycloak | **off** (the development Keycloak is served over http) |
   | Rol obligatoriu | `taskel-user` |
   | Rolul administratorilor / managerilor | `taskel-admin` / `taskel-manager` |
   | Adu grupurile din Keycloak | on |

3. **Testează conexiunea** should report the realm, a working service account and that it can read the users.
   **Salvează**, then, if you like, **Importă utilizatorii**.
4. Sign out. The sign-in screen now has **Conectează-te cu …**; sign in as `mihai.manager` /
   `Keycloak-Demo-2026` (a manager in the Vânzări department, member of the *Vanzari* group).

The accounts of the development realm are listed in [DEMO-ACCOUNTS.md](DEMO-ACCOUNTS.md). The development
`appsettings.Development.json` sets the interface's address to `http://localhost:5173`, so Keycloak sends the
browser back through the Vite server.

## Set up a real Keycloak

Below, `https://taskel.example.com` is where people open Taskel and `https://sso.example.com/realms/company` is
the realm.

### 1. The client

In the realm, **Clients → Create client**:

- **Client type**: OpenID Connect. **Client ID**: `taskel`.
- **Client authentication**: on (a confidential client). **Standard flow**: on. Direct access grants: off.
- **Service accounts roles**: on, if you want to import users (step 4).
- **Valid redirect URIs**: `https://taskel.example.com/api/auth/keycloak/callback`
- **Valid post logout redirect URIs**: `https://taskel.example.com/`
- **Web origins**: `https://taskel.example.com`
- Under **Advanced**, *Proof Key for Code Exchange Code Challenge Method*: `S256`.

Copy the secret from the **Credentials** tab. The settings page in Taskel shows the two addresses it expects,
built from the interface's address (set in the email panel, or `Email__Notifications__PublicUrl`).

A public client (no secret) also works for signing in, but then the user import is not available.

### 2. Roles (optional)

Create realm roles, or roles on the `taskel` client, for example `taskel-user`, `taskel-manager` and
`taskel-admin`, and give them to users or groups. In Taskel, name them in **Rol obligatoriu**, **Rolul
managerilor** and **Rolul administratorilor**. Keycloak puts roles in the access token by default (the *roles*
client scope), which is where Taskel reads them.

A configured role is applied at every sign-in, both ways: removing `taskel-admin` in Keycloak removes the
administrator role in Taskel at that person's next sign-in. Leave a field empty to manage that role in Taskel.

### 3. Department and groups (optional)

On the client, **Client scopes → taskel-dedicated → Add mapper → By configuration**:

- **User Attribute**: user attribute `department`, token claim name `department`, type String, added to the ID
  token. In Keycloak 24 and later, declare the attribute first under **Realm settings → User profile**.
- **Group Membership**: token claim name `groups`, *Full group path* on, added to the ID token. Then switch on
  **Adu grupurile din Keycloak** in Taskel.

### 4. User import (optional)

**Clients → taskel → Service accounts roles → Assign role → Filter by clients → `realm-management` `view-users`**.
**Importă utilizatorii** in Taskel then creates an account for every enabled user with an email, so tasks can
be given to them before they first sign in.

### 5. Taskel

Fill in **Setări sistem → Keycloak** as in *Try it locally*, with **Cere HTTPS pentru Keycloak** left on, or set
the same values in the server configuration (`Keycloak__Authority`, `Keycloak__ClientId`,
`Keycloak__ClientSecret`, …; the README lists them all). The secret can stay in an environment variable: the
settings page uses it when none is saved there.

## Troubleshooting

| What you see | Why, and what to do |
|---|---|
| Keycloak says *Invalid parameter: redirect_uri* | The callback address is not among the client's valid redirect URIs, or Taskel builds a different one. Compare with the one on the settings page; set the interface's address if Taskel runs behind a proxy. |
| The test says the realm name differs | The address entered is not exactly the issuer Keycloak announces (another host name, a trailing path). Use the issuer the test shows. |
| *Conectarea cu Keycloak nu a reușit* | The server log (`Keycloak sign-in failed`) has the reason: a clock difference, an expired attempt (more than 10 minutes), a refused code. |
| *Există deja un cont local cu adresa ta* | A local Taskel account uses the address, and Keycloak has not verified it. Verify the email in Keycloak (*Email verified*), or remove the local account. |
| *Contul tău din Keycloak nu are acces la Taskel* | The user lacks the required role. |
| Department or groups do not arrive | The mappers in step 3 are missing or not added to the ID token, or the claim names differ from the settings. |
| The import is refused (`view-users`) | Step 4: the service account needs that role. |
| Signing out does not end the Keycloak session | The post logout redirect URI is not registered on the client, so Keycloak stops on its own page. |

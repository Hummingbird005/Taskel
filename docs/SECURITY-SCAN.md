# Security scan

How Taskel's code was scanned for security problems, what was found and what was done about it. Run the scan
again before a release, and after changes to authentication, file handling or the API's input.

## Last scan: 13 November 2026

| Tool | What it covers | Result |
|---|---|---|
| **SonarQube Community** 25.1, SonarScanner for .NET 12.1 | Backend C#: vulnerabilities, security hotspots, bugs, code smells | Security **A**, reliability **A**, maintainability **A**. 0 vulnerabilities, 0 bugs, 3 hotspots reviewed |
| **SonarQube Community** 25.1, SonarScanner CLI 11 | Frontend TypeScript, CSS, HTML; the CI workflow | Security **A**, reliability **A** (was **C**), maintainability **A**. 0 vulnerabilities, 0 bugs (were 6) |
| **Semgrep** 1.185 with the open `semgrep-rules` (C#, TypeScript, JavaScript, HTML, secrets, GitHub Actions) | Injection patterns, insecure APIs, secrets, ReDoS, CI supply chain | 5 warnings, all handled below; the rest informational |
| `dotnet list package --vulnerable --include-transitive` | Known CVEs in NuGet packages, direct and transitive | None |
| `npm audit` | Known advisories in npm packages | None |

SonarQube Community does not do taint analysis (following user input through the code to find injections):
that is in the paid editions. Semgrep's rules and the review below cover that ground in part.

## Findings and what was done

### Fixed

1. **Under-posting on admin and task updates** (SonarQube S6964, reported as a code smell, a real security and
   data-integrity issue). Some update requests had value fields (`bool`, enums) that silently took their
   default when left out of the JSON:
   - `PUT /api/admin/users/{id}` without `role` set the role to *User*: an administrator changing someone's
     department alone would have demoted a manager or another administrator.
   - `PUT /api/tasks/{id}` without `isCompleted` reopened a completed task.
   - `PUT /api/notifications` without `emailOnNewTask` turned emails off.

   These fields are now required (`[JsonRequired]`): a request without them is refused with `400`. The
   interface always sends them, so nothing changes for users. Tests: `UnderPostingTests`.

2. **Demo accounts on a production server** (SonarQube S2068 hotspots, hard-coded passwords in `DemoData`). The
   passwords are public on purpose (`docs/DEMO-ACCOUNTS.md`) and seeding was already off by default and limited
   to an empty database. It is now also **refused in the Production environment** even when `Seed:DemoData` is
   set, with an error in the log. The three hotspots were reviewed as safe on that basis. Test:
   `DemoDataTests.A_Production_Server_Never_Gets_The_Demo_Accounts_Even_When_Asked`.

3. **CI actions referenced by mutable tags** (Semgrep `github-actions-mutable-action-tag`, 4 places). A tag such
   as `actions/checkout@v4` can be moved by whoever controls the action, changing the code CI runs. The actions
   are now pinned to commit SHAs (with the version in a comment), and the workflow's token is read-only
   (`permissions: contents: read`).

4. **No check for vulnerable dependencies.** CI has a new `dependencies` job that fails on any vulnerable
   NuGet package and on npm advisories rated high or critical.

5. Smaller items:
   - The frontend's control-character check used a regular expression with control characters in it (6 SonarQube
     bugs, S6324). It now checks character codes; behaviour is unchanged.
   - Two errors were logged without their exception (S6667); a file-deletion failure was swallowed by an empty
     block (S108). Both fixed.

### Reviewed, not changed

- **ReDoS in the email pattern** (Semgrep `detect-redos`, `frontend/src/lib/validation.ts`). Measured: the
  pattern runs in linear time (under 1 ms on 50,000-character inputs built to cause backtracking), and the
  input is capped at 254 characters before the pattern runs, in the browser and on the server
  (`StrictEmailAttribute`). Not exploitable.
- **Informational Semgrep findings**: interface text not internationalised (the app is in Romanian on purpose),
  props spread onto HTML elements in `Button` and the empty state (typed props, no user data), a string that
  looks like a template literal (it is a format string). No security impact.
- **Remaining SonarQube code smells** (27 backend, 102 frontend): complexity, nested ternaries, `void`
  operators, accessibility suggestions. None has a security impact; they are left for ordinary refactoring.

## Reviewed by hand

What the tools above do not reach, checked against the code:

| Area | How it is handled |
|---|---|
| SQL injection | All queries go through EF Core LINQ, which parameterises; no raw SQL |
| LDAP injection | User names are parsed to a strict character set (`DirectoryNames.TryParse`) and escaped in filters (`EscapeFilterValue`); sign-in binds as the user, no service account |
| XSS | React escapes all text; there is no `dangerouslySetInnerHTML`. Uploaded files are always downloaded, never shown, with `nosniff` and `Content-Security-Policy: sandbox` |
| Path traversal | Attachments are stored under generated 32-hex-digit names; the uploaded name is cleaned and only kept in the database |
| CSRF | The session cookie is `HttpOnly` and `SameSite=Lax`, so other sites' forms do not carry it; the API reads JSON or multipart only |
| Password guessing | Sign-in is rate-limited per client address (`Auth:LoginAttemptsPerMinute`) |
| Passwords and tokens at rest | Passwords hashed with ASP.NET Core Identity's hasher; API tokens stored as SHA-256 hashes, shown once |
| Access control | Every task read goes through `ITaskRepository.VisibleTo`; outsiders get `404`. Roles are read from the database on each request |
| Upload size | Enforced while the file arrives; the request body limit is raised only for the upload endpoint |
| Secrets in the repository | None found by Semgrep's secret rules; SMTP and directory passwords are read from configuration, never committed |

## Running the scan again

SonarQube needs Docker. On a machine where Elasticsearch's disk check would trip (less than 10% free), keep
SonarQube's data in memory as below.

```bash
# 1. SonarQube server (Community edition)
sudo sysctl -w vm.max_map_count=262144
docker run -d --name sonar -p 9000:9000 \
  --tmpfs /opt/sonarqube/data:size=6g,uid=1000,gid=0,mode=0775 \
  -e SONAR_ES_BOOTSTRAP_CHECKS_DISABLE=true sonarqube:25.1.0.102122-community
# Open http://localhost:9000, sign in as admin/admin, set a new password, then
# My Account → Security → generate a "Global Analysis Token" and export it:
export SONAR_TOKEN=...

# 2. Backend (C#)
dotnet tool install --global dotnet-sonarscanner
cd backend
dotnet sonarscanner begin /k:taskel-backend /d:sonar.host.url=http://localhost:9000 \
  /d:sonar.token="$SONAR_TOKEN" /d:sonar.exclusions="**/Migrations/**"
dotnet build Taskel.slnx --no-incremental
dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
cd ..

# 3. Frontend, Outlook macro folder and CI workflow
docker run --rm --network host -e SONAR_HOST_URL=http://localhost:9000 -e SONAR_TOKEN \
  -v "$PWD:/usr/src" sonarsource/sonar-scanner-cli:11 \
  -Dsonar.projectKey=taskel-frontend \
  -Dsonar.sources=frontend/src,frontend/index.html,integrations,.github \
  -Dsonar.exclusions="**/node_modules/**,**/dist/**"

# 4. Dependencies (also run by CI)
(cd backend && dotnet list Taskel.slnx package --vulnerable --include-transitive)
(cd frontend && npm audit)
```

The results are under *Projects* in SonarQube: *Security Hotspots* need a decision (safe, fixed or
acknowledged); *Issues* filtered by *Software quality: Security* show the vulnerabilities.

Semgrep, optionally: `pip install semgrep`, then
`semgrep scan --metrics=off --config p/csharp --config p/typescript --config p/secrets --config p/github-actions .`
(or, where semgrep.dev is unreachable, point `--config` at a clone of
`https://github.com/semgrep/semgrep-rules`).

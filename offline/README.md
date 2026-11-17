# Offline packages

Everything needed to build, test and run Taskel without the internet, apart from the installers of the tools
themselves (see the end).

| Folder | What is in it |
|---|---|
| `nuget/` | Every NuGet package of the solution: the API, the tests and the `dotnet-ef` tool (110 packages, 108 MB) |
| `npm/` | An npm cache with every package of `frontend/package-lock.json`, including the native parts for **Linux and Windows (x64)** (81 MB) |
| `NuGet.Config` | Makes `dotnet` restore from `nuget/` only |
| `windows/web.config` | The IIS configuration for running Taskel on Windows |
| `actualizeaza-pachete.sh` | Refills `nuget/` and `npm/` after a dependency changes |

The CI job **offline** restores the solution and the interface from this folder alone, into empty caches, for
Linux and for Windows; it fails as soon as a package is missing.

## Building without the internet

From the repository root, on Windows (PowerShell) or Linux:

```bash
cd backend
dotnet tool restore --configfile ../offline/NuGet.Config
dotnet restore Taskel.slnx --configfile ../offline/NuGet.Config
dotnet build Taskel.slnx --no-restore
dotnet test --no-build                 # needs PostgreSQL, as described in the main README
dotnet publish src/Taskel.Api -c Release --no-restore -o ../publish/api

cd ../frontend
npm ci --offline --cache ../offline/npm
npm run lint
npm run build                          # the interface is in frontend/dist
npm run dev                            # or the development server, with the API on port 5000
```

After the first restore, `dotnet build`, `dotnet run` and `dotnet test` work as usual: the packages are in the
machine's NuGet cache. Restore with `--configfile` again whenever a package is added.

On Windows, keep the repository in a short path (for example `C:\src\Taskel`): the npm cache has long file
names, and paths over 260 characters fail unless long paths are enabled.

## Adding or updating a dependency

On a machine with internet access, after changing a `.csproj`, `backend/dotnet-tools.json` or
`frontend/package-lock.json`:

```bash
offline/actualizeaza-pachete.sh        # on Windows, in Git Bash
git add offline && git commit
```

The script starts from empty caches, so the folder holds exactly what the current dependencies need, and old
versions disappear from it. The native npm parts are fetched for Linux and Windows x64; add another platform
(macOS, ARM) to the `for os in` line of the script if someone develops on it.

## What is not in the folder

The tools themselves, which are installers of their own. Download them on a connected machine and carry them
over with the repository:

| Tool | Needed for | Where |
|---|---|---|
| .NET 10 SDK | building | https://dotnet.microsoft.com/download/dotnet/10.0 |
| ASP.NET Core 10 Hosting Bundle | running under IIS (Windows) | same page, "Hosting Bundle" |
| Node.js 22 LTS | building the interface | https://nodejs.org/en/download |
| PostgreSQL 16 or later | running, and the backend tests | https://www.postgresql.org/download/ |
| Git | cloning | https://git-scm.com/downloads |

The installation guide for Windows (`docs/manuale/5-ghid-instalare-windows.pdf`) and for Linux
(`docs/manuale/4-ghid-instalare.pdf`) describe the whole setup.

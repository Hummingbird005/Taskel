#!/usr/bin/env bash
# Refills offline/nuget and offline/npm with every package Taskel needs, for Linux and Windows.
# Run it on a machine with internet access after changing a dependency (a .csproj, dotnet-tools.json or
# package-lock.json), then commit the result. CI (job "offline") fails while the folder is incomplete.
# Needs bash, the .NET 10 SDK and Node.js 22; on Windows it runs in Git Bash.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "NuGet: restoring the solution, the tools and a publish into an empty package folder"
export NUGET_PACKAGES="$work/nuget-packages"
# A separate CLI home too: otherwise "dotnet tool restore" finds the tool in an earlier cache and downloads nothing.
export DOTNET_CLI_HOME="$work/cli-home"
(
  cd "$root/backend"
  dotnet restore Taskel.slnx
  dotnet tool restore
  dotnet publish src/Taskel.Api -c Release -o "$work/publish" > /dev/null
)
rm -rf "$root/offline/nuget"
mkdir -p "$root/offline/nuget"
# Each package sits in <id>/<version>/<id>.<version>.nupkg; other .nupkg copies there are skipped.
find "$NUGET_PACKAGES" -name '*.nupkg' | while read -r file; do
  version="$(basename "$(dirname "$file")")"
  id="$(basename "$(dirname "$(dirname "$file")")")"
  if [ "$(basename "$file")" = "$id.$version.nupkg" ]; then cp "$file" "$root/offline/nuget/"; fi
done

echo "npm: caching the packages of package-lock.json, with the native parts for Linux and Windows (x64)"
rm -rf "$root/offline/npm"
cp "$root/frontend/package.json" "$root/frontend/package-lock.json" "$work/"
for os in linux win32; do
  (cd "$work" && rm -rf node_modules && npm ci --cache "$root/offline/npm" --os="$os" --cpu=x64 \
    --ignore-scripts --no-audit --no-fund > /dev/null)
done
rm -rf "$root/offline/npm/_logs" "$root/offline/npm/_update-notifier-last-checked"

echo "Done: $(ls "$root/offline/nuget" | wc -l) NuGet packages ($(du -sh "$root/offline/nuget" | cut -f1)), npm cache $(du -sh "$root/offline/npm" | cut -f1)."

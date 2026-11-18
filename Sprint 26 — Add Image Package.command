#!/bin/bash
#
# Sprint 26, step 1 — resolve the image library version instead of guessing it.
#
# Rendition generation needs a codec: .NET has no cross-platform image encoder
# in the BCL. SkiaSharp is the choice because it is MIT licensed. The obvious
# alternative, SixLabors.ImageSharp, moved to the Six Labors Split License at
# v3 — free for open source and small companies, commercial licence required
# above a revenue threshold. That is a decision with a cost attached and it is
# not mine to take quietly.
#
# This script does NOT guess a version. `dotnet add package` resolves the latest
# stable from nuget.org and writes it into Directory.Packages.props (central
# package management), so the pin is a fact rather than an inference. Sprint 15
# inferred two pins blind and got lucky; that is a habit worth not repeating.
#
set -u
cd "$(dirname "$0")" || exit 1

if ! command -v dotnet >/dev/null; then
  for d in /usr/local/share/dotnet "$HOME/.dotnet" /opt/homebrew/opt/dotnet/libexec; do
    [ -x "$d/dotnet" ] && export PATH="$d:$PATH" && break
  done
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

cd services/catalog || exit 1

echo "== Before =="
grep -n "PackageVersion" Directory.Packages.props | tail -5
echo

echo "== Adding SkiaSharp to Pim.Catalog.Infrastructure =="
dotnet add src/Pim.Catalog.Infrastructure/Pim.Catalog.Infrastructure.csproj package SkiaSharp
RC1=$?

# Native binaries. The main package carries macOS and Windows; Linux needs its
# own, and the CI workflow (when it is installed) runs on Linux. Added now so
# the first CI run is not the thing that discovers it.
echo
echo "== Adding SkiaSharp.NativeAssets.Linux (for CI, harmless on macOS) =="
dotnet add src/Pim.Catalog.Infrastructure/Pim.Catalog.Infrastructure.csproj package SkiaSharp.NativeAssets.Linux
RC2=$?

echo
echo "== After =="
grep -n "Skia" Directory.Packages.props src/Pim.Catalog.Infrastructure/Pim.Catalog.Infrastructure.csproj

echo
echo "add exit codes: $RC1 / $RC2   (0 = resolved)"
echo
if [ $RC1 -ne 0 ] || [ $RC2 -ne 0 ]; then
  echo "One or both failed. Send the output above back to Claude — do not guess a version."
  exit 1
fi

echo "Resolved. Next: Claude writes the ingest pipeline against these versions."
echo "You can close this window."

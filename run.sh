#!/bin/sh
set -eu
cd -- "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
if [ ! -f Kalkulace.Api.dll ]; then
    echo 'Run this script from artifacts/kalkulace-release or an extracted kalkulace-release.zip.' >&2
    exit 1
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo 'Install the ASP.NET Core 9 or 10 runtime to run this app.' >&2
    exit 1
fi
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:5080}"
exec dotnet Kalkulace.Api.dll "$@"

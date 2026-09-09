#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$SCRIPT_DIR"
if [[ -n "${1:-}" ]]; then
  exec dotnet DataPilot.Api.dll --create-certificate true --certificate-hosts "$1"
else
  exec dotnet DataPilot.Api.dll --create-certificate true
fi

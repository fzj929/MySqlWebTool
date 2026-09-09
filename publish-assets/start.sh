#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
PORT=${1:-8088}

cd "$SCRIPT_DIR"
echo "DataPilot: https://localhost:${PORT}"
echo "Press Ctrl+C to stop."
exec dotnet DataPilot.Api.dll --environment Production --https-port "$PORT"

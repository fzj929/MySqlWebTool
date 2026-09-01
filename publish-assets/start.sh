#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
PORT=${1:-5080}

cd "$SCRIPT_DIR"
echo "MySQL Web Tool: http://localhost:${PORT}"
echo "Press Ctrl+C to stop."
exec dotnet MySqlTool.Api.dll --urls "http://0.0.0.0:${PORT}"

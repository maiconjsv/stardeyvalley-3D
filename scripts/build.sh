#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../src/StardewValley3D"
dotnet build -c "${1:-Debug}"

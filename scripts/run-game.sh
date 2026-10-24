#!/usr/bin/env bash
# Compila o mod, inicia o jogo pelo SMAPI e deixa o log do SMAPI na tela.
set -euo pipefail

GAME_DIR="${GAME_DIR:-$HOME/.steam/debian-installation/steamapps/common/Stardew Valley}"
LOG="$HOME/.config/StardewValley/ErrorLogs/SMAPI-latest.txt"

echo "==> build"
"$(dirname "$0")/build.sh" "${1:-Debug}" | tail -3

echo "==> iniciando o jogo pelo SMAPI"
cd "$GAME_DIR"
./StardewModdingAPI 2>&1 | grep --line-buffered -iE "StardewValley3D|3D|error|warn" || true

echo
echo "Log completo: $LOG"

#!/usr/bin/env bash
# Prepara o ambiente de desenvolvimento. Rode uma vez.
set -euo pipefail

GAME_DIR="${GAME_DIR:-$HOME/.steam/debian-installation/steamapps/common/Stardew Valley}"

echo "==> 1/3 .NET SDK"
if ! command -v dotnet >/dev/null; then
  echo "Instalando dotnet-sdk-8.0 (requer sudo)..."
  sudo apt-get update
  sudo apt-get install -y dotnet-sdk-8.0
else
  echo "ja instalado: $(dotnet --version)"
fi

echo "==> 2/3 SMAPI"
if [ -f "$GAME_DIR/StardewModdingAPI.dll" ]; then
  echo "SMAPI ja instalado."
else
  cat <<'MSG'
SMAPI nao encontrado. Baixe a versao mais recente em:
    https://smapi.io
Depois:
    unzip SMAPI-*.zip -d /tmp/smapi && cd /tmp/smapi/SMAPI*/ && ./install\ on\ Linux.sh
E ajuste as opcoes de inicializacao do Steam para:
    "<pasta do jogo>/StardewModdingAPI" %command%
MSG
  exit 1
fi

echo "==> 3/3 build"
cd "$(dirname "$0")/../src/StardewValley3D"
dotnet build -c Debug
echo "Pronto. O mod foi copiado para $GAME_DIR/Mods/StardewValley3D"

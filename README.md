# Stardew Valley 3D

Mod que transforma Stardew Valley 1.6 em um jogo **3D em primeira pessoa** no qual os assets
2D originais são **extrudados em volumes 3D**, na linha de Doom e Warhammer 40k: Boltgun —
mantendo **toda a lógica original do jogo** e adaptando as mecânicas para o espaço 3D.

**O escopo completo, dividido em fases, está em [docs/SCOPE.md](docs/SCOPE.md).**
A arquitetura técnica está em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Estado atual

Fase 0 concluída (scaffold) e Fase 1 iniciada. O que já existe em código:

| Arquivo | Papel |
|---|---|
| `ModEntry.cs` | ciclo de vida, config, decisão por tick de renderizar em 3D ou cair no 2D |
| `Rendering/Camera3D.cs` | câmera orbital em 3ª pessoa + raycast da crosshair para tile |
| `Rendering/Renderer3D.cs` | orquestra o frame: device state, malha estática, billboards, luz ambiente |
| `Rendering/WorldGeometryBuilder.cs` | mapa xTile → malha de chão + paredes extrudadas, em chunks de 16×16 |
| `Rendering/SpriteMeshBuilder.cs` | **extrusão**: converte sprites 2D em volumes 3D com espessura |
| `Rendering/SpriteMeshBatch.cs` | desenha os volumes com alpha test e depth buffer |
| `Rendering/SpriteCollector.cs` | varre a localização e emite uma instância por entidade |
| `World/GeometryEditor.cs` | editor de cenário em jogo (F7) |
| `World/LocationGeometry.cs` | o "WAD": heurística de altura + setores autorais em JSON |
| `Input/FirstPersonInput.cs` | mouse-look, movimento relativo à câmera, mira |
| `Patches/DrawPatches.cs` | desvia `Game1._draw` para o renderizador 3D |
| `Patches/CursorPatches.cs` | faz a crosshair substituir o mouse nas coordenadas de mundo |

## Como isso funciona (resumo em um parágrafo)

O jogo continua rodando inteiro, sem alteração: tempo, cultivos, NPCs, economia, save.
Um patch Harmony intercepta o desenho do mundo e, em vez do `SpriteBatch` ortográfico, monta
uma cena 3D a partir do mesmo estado: o mapa vira malha, cada sprite vira um quad voltado à
câmera. Um segundo patch faz a crosshair substituir o mouse nas coordenadas de mundo — e como **toda**
ferramenta do Stardew resolve o alvo a partir da posição do mouse, regar, cavar, minerar e
atacar passam a funcionar em 3D sem que a lógica de nenhuma delas seja tocada.

## Pré-requisitos (ainda não instalados nesta máquina)

```bash
./scripts/setup-dev.sh     # instala .NET SDK 8 e verifica o SMAPI
```

1. **.NET SDK 8** — `sudo apt install dotnet-sdk-8.0` (o mod compila para `net6.0`, que o SDK 8 suporta)
2. **SMAPI 4.x** — https://smapi.io, instalado na pasta do jogo

## Build

```bash
./scripts/build.sh          # copia o resultado direto para <jogo>/Mods/
```

## Controles (modo 3D)

| Tecla | Ação |
|---|---|
| `F9` | liga/desliga o renderizador 3D |
| `F10` | overlay de debug |
| `F7` | editor de cenário |
| `WASD` | movimento relativo à câmera |
| mouse | olhar |
| scroll | primeira ↔ terceira pessoa |

### Editor de cenário (`F7`)

Mira no tile e corrige a geometria ao vivo; a malha é reconstruída a cada tecla.

| Tecla | Ação |
|---|---|
| `Q` / `E` | altura do chão − / + (passo de ¼ de tile) |
| `Z` / `C` | altura da parede − / + |
| `X` | limpa o tile (volta à heurística) |
| `F` / `G` | copia / cola os valores do tile |
| `[` / `]` | tamanho do pincel (1×1 até 9×9) |
| `Ctrl+S` | grava em `assets/geometry/<Localizacao>.json` |
| `F5` | recarrega o JSON do disco |

Todo o resto permanece vanilla.

## Como testar

```bash
./scripts/run-game.sh
```

Compila, inicia o jogo pelo SMAPI e filtra o log nas linhas que interessam.
Para iniciar sem rebuild, rode direto o launcher do SMAPI:

```bash
cd ~/.steam/debian-installation/steamapps/common/Stardew\ Valley && ./StardewModdingAPI
```

Pela Steam, ajuste as opções de inicialização do jogo para:

```
"/home/maicon/.steam/debian-installation/steamapps/common/Stardew Valley/StardewModdingAPI" %command%
```

### Roteiro de teste da Fase 0/1

1. **O mod carrega?** No console do SMAPI, logo no início, deve aparecer
   `Stardew Valley 3D 0.1.0 by maicon` na lista de mods, e depois
   `Stardew Valley 3D carregado. F9 alterna o renderizador.`
   Nenhum `ERROR` com `StardewValley3D` no nome.
2. **Carregue um save** (os dois existentes servem) e fique na Fazenda.
3. **`F9`** — aparece a mensagem `Modo 3D: ON` no HUD e a tela muda para a vista 3D.
4. **`F10`** — overlay de debug com posição da câmera, tile mirado e contagem de triângulos.
5. **Mouse / WASD / scroll** — olhar, mover relativo à câmera, afastar a câmera.
6. **`F9` de novo** a qualquer momento volta ao jogo 2D normal.

O log fica em `~/.config/StardewValley/ErrorLogs/SMAPI-latest.txt`.

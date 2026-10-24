# Arquitetura técnica

## Alvo

- Stardew Valley **1.6**, .NET 6, MonoGame DesktopGL (confirmado na instalação local)
- SMAPI 4.x + Harmony (vem embutido no SMAPI)
- Linux x64 (a mesma build funciona em Windows/macOS)

## Convenções de unidade

| Conceito | Valor |
|---|---|
| 1 tile do Stardew | 64 px → **1.0 unidade de mundo** |
| eixo X | leste (igual ao X em pixels) |
| eixo **Z** | **sul** (equivale ao Y em pixels do jogo) |
| eixo Y | **altura** (não existe no jogo original — é o que inventamos) |
| escala de sprite | 1 px de sprite = 1/64 unidade, ×4 para bater com o zoom vanilla |

A troca de Y por Z é o detalhe que mais causa bug no projeto. Toda conversão passa por
`Camera3D.WorldFromPixels()` — nunca converta à mão.

## Fluxo de um frame

```
Game1.Update          (vanilla, intocado)
  └─ ModEntry.OnUpdateTicked
       ├─ ThirdPersonInput.Update   mouse-look → Yaw/Pitch; WASD → Farmer.setMoving()
       │                            crosshair  → AimTile
       └─ Renderer3D.Update         Camera3D: View, Projection, Frustum

Game1._draw
  └─ DrawPatches.Draw_Prefix        → ShouldRender3D()? senão devolve ao vanilla
       ├─ Renderer3D.DrawWorld
       │    ├─ LocationGeometry     heurística de altura + setores autorais (cache)
       │    ├─ WorldGeometryBuilder malha estática em chunks 16×16 (cache)
       │    ├─ DrawStaticMesh       frustum culling por chunk
       │    └─ DrawBillboards       SpriteCollector → BillboardBatch
       └─ SpriteBatch 2D            HUD/menus vanilla + crosshair + debug
```

Enquanto isso, `Game1.currentCursorTile` devolve o tile da crosshair (`CursorPatches`), e é
por aí que as mecânicas vanilla continuam funcionando.

## Por que patchar o cursor em vez de reescrever as mecânicas

Praticamente toda ação do Stardew — `Tool.DoFunction`, `GameLocation.checkAction`,
`Utility.playerCanPlaceItemHere`, mira de slingshot, highlight de tile — resolve o alvo a
partir do tile sob o cursor. Ao sobrescrever esse único valor, herdamos de graça: arar,
plantar, regar, colher, minerar, cortar árvore, pescar (lançamento), posicionar objeto,
alimentar animal, interagir com NPC e abrir porta.

É a diferença entre "adaptar as mecânicas" (semanas) e "reescrever o jogo" (anos). É o
pilar do projeto, e qualquer mecânica que não se encaixe nele vira exceção explícita na
Fase 5, não regra nova.

## Estratégia de risco: superfície de contato mínima

Tudo que depende de internals do jogo vive em `Patches/` (dois arquivos, dois patches). O
resto do mod conversa só com abstrações nossas (`Camera3D`, `LocationGeometry`,
`BillboardBatch`). Quando o Stardew atualizar, o conserto é local.

Toda falha no caminho 3D é capturada e cai de volta no render vanilla, em vez de crashar o
jogo — `DrawPatches.Draw_Prefix` devolve `true` no `catch`.

## Geometria: o problema central

O Stardew é 2D puro: **não existe dado de altura em lugar nenhum**. Três camadas resolvem:

1. **Heurística** (`LocationGeometry.Bake`): camada `Buildings` sem `Passable` → parede de
   2.5 tiles; propriedade `Water` → chão a −0.35. Cobre 100% das localizações, incluindo as
   de outros mods, com qualidade medíocre mas jogável.
2. **Setores autorais** (`assets/geometry/<Location>.json`): no espírito dos setores do Doom,
   sobrepõem a heurística onde ela erra. Ver `assets/geometry/FarmHouse.json`.
3. **Editor** (`tools/geometry-editor/`, Fase 2): pintar alturas sobre o `.tmx` e exportar o
   JSON — é o que torna o trabalho de conteúdo colaborável.

## Performance

- Malha estática em `VertexBuffer` por chunk de 16×16 tiles, construída uma vez por
  localização e cacheada; faces internas de parede são omitidas.
- Billboards: um draw call por textura, ordenados por textura no passe opaco (o depth buffer
  resolve a ordem) e por distância só no passe translúcido.
- Culling por frustum em chunk; billboards ganham culling na Fase 8.
- Alvo: 60 fps em GPU integrada, inclusive na Town (centenas de sprites).

## O que ainda é TODO no código

Marcado com `TODO Fase N` nos arquivos. Os três maiores:

- `WorldGeometryBuilder.EmitFloor` — resolver tilesheet/tile index reais e agrupar a malha
  por textura (hoje emite UV de tile cheio, só para validar câmera e layout)
- `DrawPatches.DrawVanillaUi` — chamar `drawHUD` e cia. do `Game1` via reflection
- `SpriteCollector` — faltam objects, clumps, buildings, debris, projectiles, animais, cultivos

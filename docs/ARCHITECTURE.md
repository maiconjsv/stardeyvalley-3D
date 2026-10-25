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

Enquanto isso, os acessores de mouse do jogo devolvem a posição da crosshair em coordenadas
de mundo (`CursorPatches`), e é por aí que as mecânicas vanilla continuam funcionando.

## Por que patchar o mouse em vez de reescrever as mecânicas

Verificado no IL do jogo 1.6: tanto `Game1.pressUseToolButton` quanto `Game1.pressActionButton`
derivam o alvo de **`getOldMouseX() + Game1.viewport.X`**. Sobrescrevendo esses acessores,
herdamos de graça: arar, plantar, regar, colher, minerar, cortar árvore, pescar (lançamento),
posicionar objeto, alimentar animal, interagir com NPC e abrir porta.

É a diferença entre "adaptar as mecânicas" (semanas) e "reescrever o jogo" (anos). É o pilar
do projeto, e qualquer mecânica que não se encaixe nele vira exceção explícita na Fase 5, não
regra nova.

Dois detalhes que só a inspeção do IL revelou, e que valem registrar porque custaram tempo:

- **`Game1.currentCursorTile` não serve.** É um campo estático (não propriedade, logo Harmony
  não patcha) e é lido apenas por `updateCursorTileHint` — alimenta o destaque visual do
  cursor, não a mira das ferramentas. A primeira versão do mod patchava esse campo e
  falhava no carregamento.
- **O parâmetro `ui_scale` dos acessores separa mundo de UI com precisão.** `true` é usado por
  menus e HUD, `false` pelo mundo. Sobrescrevemos só o segundo, e é por isso que os menus
  continuam respondendo ao mouse real sem nenhum trabalho extra.
- **`Game1.game1.IsMouseVisible` tem de continuar `true`.** O jogo lê
  `wasMouseVisibleThisFrame` para escolher entre o alvo do mouse e o alvo de gamepad; com ele
  `false`, a nossa mira é ignorada. O cursor é escondido por `Game1.mouseCursorTransparency`.

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

## Alpha test, e por que ele não é um detalhe

Tanto a malha do mundo quanto os sprites volumétricos são desenhados com `AlphaTestEffect`,
nunca com alpha blending. Três coisas dependem disso:

1. **Transparência correta.** As camadas `Buildings` e `Front` são quase todas transparentes.
   Sem recorte, cada pixel vazio vira preto opaco — foi o que transformou as casas da Fazenda
   em lajes pretas.
2. **Profundidade correta.** Um pixel transparente desenhado como opaco também escreve no
   depth buffer e esconde tudo que estiver atrás. Com alpha test, o pixel é descartado antes
   disso, e a regra "o que está mais perto aparece na frente" passa a valer por pixel, de
   graça, sem ordenar nada.
3. **Sem ordenação.** É por isso que não existe sort por distância no caminho opaco: o depth
   buffer resolve, inclusive entre sprites que se interpenetram.

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

# Stardew Valley 3D — Escopo do projeto

Mod que converte Stardew Valley 1.6 em um jogo **3D em terceira pessoa com sprites 2D
billboard** (estética Doom / Warhammer 40k: Boltgun), **mantendo toda a lógica original
do jogo intacta** e adaptando as mecânicas para o espaço 3D.

## Princípio arquitetural central

> **Não reescrevemos o jogo. Substituímos apenas o renderizador e a camada de input.**

O `Game1` continua rodando normalmente: tempo, estações, cultivos, NPCs, economia,
quests, save/load — tudo vanilla. O mod:

1. **intercepta o desenho** do mundo (`Game1._draw`) e, em vez do `SpriteBatch` ortográfico,
   monta uma cena 3D a partir do mesmo estado de jogo;
2. **remapeia o input** para câmera em terceira pessoa, traduzindo movimento relativo à
   câmera de volta para o `Farmer` (que só entende 4 direções);
3. **adapta a mira** das mecânicas que dependiam do cursor do mouse em coordenadas de tile
   (ferramentas, armas, interação) para um raycast a partir da crosshair.

Consequências diretas:
- Compatível com saves vanilla (nenhum dado novo no save).
- Compatível, em princípio, com mods de conteúdo (Content Patcher), porque continuamos
  lendo as mesmas texturas e os mesmos mapas.
- Toda localização não mapeada manualmente ainda funciona, via geração procedural de
  geometria (ver Fase 2) ou fallback para render 2D top-down.

## Como a estética Doom/Boltgun se traduz aqui

| Elemento | Doom / Boltgun | Nosso equivalente |
|---|---|---|
| Chão / teto | setores com altura | camadas `Back`/`Front` do mapa xTile extrudadas em quads |
| Paredes | linedefs | derivadas de colisão (`Buildings` layer) + height map autoral |
| Inimigos/objetos | sprites billboard | todo sprite do SDV (NPC, árvore, cultivo, objeto, debris) como quad voltado à câmera |
| Sprites direcionais | 8 ângulos | SDV tem 4 (N/S/L/O) — escolhemos o frame pelo ângulo relativo à câmera |
| Iluminação | setor + fog | luz do dia/estação do SDV + fog de distância + luzes pontuais (lanterna, tochas) |
| Filtro | low-res + dither | render target em resolução reduzida + nearest-neighbor + paleta opcional |

---

# Fases

Cada fase é entregável e jogável por si. Nada de big bang.

## Fase 0 — Toolchain e esqueleto do mod ✅ (scaffold criado)
**Objetivo:** um mod SMAPI que carrega, tem config, tecla de toggle e overlay de debug.

- [x] Estrutura de projeto, `manifest.json`, `.csproj` com `Pathoschild.Stardew.ModBuildConfig`
- [x] `ModConfig` + persistência
- [x] Tecla de toggle (padrão `F9`) entre render vanilla e render 3D
- [x] HUD de debug (FPS, nº de draw calls, localização, posição da câmera)
- [ ] **Pendências de ambiente (requer ação sua):** instalar .NET SDK 8 e SMAPI — `scripts/setup-dev.sh`

**Aceite:** mod aparece no log do SMAPI, toggle alterna um clear de tela colorido.

## Fase 1 — Renderizador 3D base (chão + câmera)
**Objetivo:** andar pela Fazenda em 3ª pessoa vendo o chão do mapa em 3D.

- Harmony prefix em `Game1._draw` que desvia o desenho do mundo
- Estado de device 3D: depth buffer, `DepthStencilState.Default`, alpha test, `SamplerState.PointClamp`
- `Camera3D`: terceira pessoa com órbita (yaw/pitch/distância), colisão simples de câmera, smoothing
- `TileAtlas`: mapeia cada tile index da tilesheet para UVs, com padding anti-bleeding
- `WorldGeometryBuilder`: converte camadas `Back`/`Front` em um mesh estático de quads por localização, com cache invalidado em mudança de mapa/estação
- Frustum culling por chunk (16×16 tiles)
- HUD vanilla ainda desenhada por cima, em 2D

**Aceite:** 60 fps na Fazenda, chão correto, câmera atrás do jogador, HUD legível.

## Fase 2 — Geometria vertical (o "WAD" do Stardew)
**Objetivo:** o mundo deixa de ser um tapete plano. Paredes, cliffs, prédios com volume.

O SDV **não tem dado de altura**. Esta é a maior fase de conteúdo. Estratégia em 3 camadas:

1. **Heurística automática** (cobre 100% das localizações, inclusive de mods):
   - tile sólido na `Buildings` layer → parede extrudada (altura padrão por tipo)
   - propriedade `Water` → plano de água rebaixado
   - `NoFurniture`/vazio fora do mapa → void com skybox
2. **Height map autoral por localização** (`assets/geometry/<Location>.json`):
   setores no estilo Doom (`floorHeight`, `ceilHeight`, textura de parede, offset)
   sobrepondo a heurística onde ela erra — Farm, Town, Beach, Mountain, Mines, interiores.
3. **Ferramenta de autoria** (`tools/geometry-editor/`): editor web que carrega o `.tmx`
   e deixa pintar alturas por tile, exportando o JSON.

- Interiores: teto fechado + iluminação de ambiente (crítico para o look Boltgun)
- Minas: já são grid procedural → gerador de geometria dedicado, o melhor caso de uso
- Transições de altura: escadas/rampas derivadas de `Warp` e tiles de escada

**Aceite:** Farm, Town, FarmHouse, SeedShop e Mines(1-20) com volume correto e sem buracos.

## Fase 3 — Billboards (tudo que é sprite)
**Objetivo:** jogador, NPCs, árvores, cultivos, objetos e partículas no espaço 3D.

- `BillboardBatch`: quads Y-axis-aligned (como Doom), um draw call por atlas, ordenados por distância
- Alpha test (não alpha blend) para evitar artefato de depth sort nos sprites
- Mapeamento de todo sprite desenhado: `Farmer`, `NPC`, `Monster`, `Object`, `TerrainFeature`
  (árvore, grama, cultivo), `ResourceClump`, `Debris`, `Projectile`, `FarmAnimal`, `Building`
- **Sprites direcionais:** escolher entre os 4 frames de direção do SDV pelo ângulo
  entre o facing do personagem e a câmera (é exatamente o truque do Doom, com 4 ângulos)
- Sombra de blob projetada no chão
- Sprites "altos" (árvore, prédio) como quad vertical escalado; opcional cross-billboard
  (2 quads em X) para árvores, que lê muito melhor em 3ª pessoa

**Aceite:** nenhum sprite flutuando/afundado; árvores e NPCs legíveis de qualquer ângulo.

## Fase 4 — Câmera e input em terceira pessoa
**Objetivo:** controle que parece um jogo 3D, mas que o `Farmer` entende.

- Mouse-look (yaw/pitch), scroll = distância da câmera, invertível
- Movimento relativo à câmera → tradução para `Farmer.setMoving()` / `movementDirections`
  (inclui diagonal, que o SDV suporta)
- Facing do jogador: segue a câmera quando há mira ativa, segue o movimento caso contrário
- **Mira por raycast:** crosshair → ray → interseção com o plano do tile → `Game1.currentCursorTile`.
  É o ponto-chave: todas as ferramentas do SDV usam o tile sob o cursor, então ao sobrescrever
  esse valor, farming/mining/combate "simplesmente funcionam".
- Gamepad: layout de shooter 3ª pessoa
- Travamento de câmera em diálogo/menu/cutscene, com cursor liberado

**Aceite:** regar, cavar, derrubar árvore e atacar slime usando só crosshair + WASD + mouse.

## Fase 5 — Adaptação das mecânicas principais
**Objetivo:** cada mecânica do jogo revisada no contexto 3D. Nada removido.

| Mecânica | Adaptação |
|---|---|
| Farming (arar, plantar, regar, colher) | mira por raycast; preview do tile alvo em 3D; AoE das ferramentas upgradadas desenhada no chão |
| Mineração / foraging | igual, com feedback de alcance; pedras/nós como billboards com sombra |
| Combate (minas, Skull Cavern) | mira 3D real; arco/slingshot com projétil balístico; sweep da espada como cone no chão |
| Pesca | lançamento com mira 3D; **minigame mantido em overlay 2D** (não compensa converter) |
| Animais / pets | billboards com pathing vanilla; carinho por raycast |
| NPCs, presentes, diálogos | interação por raycast + proximidade; retrato 2D vanilla no diálogo |
| Lojas e todos os menus | mantidos 2D, com blur/escurecimento do mundo 3D atrás |
| Casamento, filhos, eventos de coração | cutscenes usam **fallback top-down 2D** (Fase 8) ou câmera cinematográfica scriptada |
| Festivais | fallback 2D inicialmente; conversão por festival é escopo pós-1.0 |
| Construção (Robin / Carpenter) | menu 2D para escolher, posicionamento no mundo 3D com ghost mesh |
| Mina procedural, elevador, escadas | gerador da Fase 2 + transição de andar |
| Multiplayer | render é 100% client-side → funciona; jogadores remotos como billboards |
| Clima (chuva, neve, raio) | sistema de partículas 3D substituindo o overlay 2D |

**Aceite:** um ano in-game jogável de ponta a ponta sem precisar voltar ao modo 2D,
exceto em festivais.

## Fase 6 — Iluminação e atmosfera (o "feel" Boltgun)
- Luz ambiente derivada da cor de tempo/estação do SDV
- Luzes pontuais do SDV (`Game1.currentLightSources`) como luzes 3D reais
- Fog de distância por localização (as minas ganham muito com isso)
- Shader de água, emissivos, lanterna/Glow Ring como spotlight
- Post-process: render a 320×200 upscalado, dithering opcional, vinheta, grain
- Paleta: modo "fiel ao SDV" vs modo "grimdark"

## Fase 7 — HUD e UX
- HUD vanilla preservada + crosshair, bússola, indicador de tile alvo
- Tutorial de controles, menu de configuração via GMCM (todas as opções em jogo)
- Acessibilidade: FOV, intensidade de head-bob, motion sickness (câmera rígida), snap-turn

## Fase 8 — Compatibilidade, performance, release
- Fallback automático: localização sem geometria e sem heurística confiável → render 2D
- Hot reload de geometria em debug
- Perfilagem: alvo 60 fps em GPU integrada; batching, instancing, LOD de billboard
- Testes com os mods populares (Content Patcher, SVE, Automate)
- Página Nexus, i18n (PT-BR/EN), compat SMAPI version-gated

---

## Riscos conhecidos (e mitigação)

1. **Ausência de dado de altura** → maior custo do projeto. Mitigado por heurística +
   editor de geometria (Fase 2), que permite colaboração da comunidade.
2. **`Game1._draw` é monolítico** → o prefix precisa reimplementar a ordem de desenho de
   HUD/menus. Mitigado chamando os métodos privados do vanilla via reflection, em vez de
   reescrevê-los.
3. **Cutscenes/festivais assumem câmera top-down** → fallback 2D por evento, detectado via
   `Game1.CurrentEvent`.
4. **Performance de billboards** (a Town tem centenas de sprites) → atlas único + um draw
   call por atlas + culling agressivo.
5. **Quebra a cada update do SDV** → isolar todo acesso a internals em uma camada fina
   (`Patches/`), o resto do mod fala só com abstrações nossas.

## Ordem de execução sugerida

Fase 0 → 1 → 3 (billboards planos, mundo ainda chato mas já é "Doom") → 4 (já é jogável e
divertido) → 2 (o mundo ganha volume) → 6 → 5 → 7 → 8.

Trocar 2 e 3/4 de ordem é deliberado: billboards + câmera em um mundo plano já entrega um
protótipo jogável e demonstrável em uma fração do esforço da geometria vertical.

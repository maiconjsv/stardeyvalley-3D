using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace StardewValley3D.Rendering;

/// <summary>
/// Varre o estado da localizacao e emite uma instancia volumetrica por sprite.
///
/// A regra do projeto e que tudo que existe no 2D apareca no 3D sem alteracao. Cada tipo de
/// entidade do Stardew precisa, portanto, de um mapeamento explicito aqui.
///
/// Duas formas de montagem, conforme o tipo:
///   - <b>personagens</b>: fatia extrudada que gira para encarar a camera, com o frame
///     direcional escolhido pelo angulo -- o truque do Doom, agora com espessura real;
///   - <b>cenario</b>: duas fatias perpendiculares (cross), com orientacao fixa, que tem
///     volume de qualquer angulo.
/// </summary>
public sealed class SpriteCollector
{
    /// <summary>Espessura extra para construcoes, que precisam parecer solidas.</summary>
    private const int BuildingThickness = 10;

    public void Collect(GameLocation location, Camera3D camera, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        this.CollectFarmers(location, camera, config, builder, batch);
        this.CollectCharacters(location, camera, config, builder, batch);
        this.CollectBuildings(location, config, builder, batch);
        this.CollectTerrainFeatures(location, config, builder, batch);
        this.CollectResourceClumps(location, config, builder, batch);
        this.CollectObjects(location, config, builder, batch);
        this.CollectFurniture(location, config, builder, batch);

        if (config.MapBorderForest)
            this.CollectMapBorder(location, camera, config, builder, batch);

        // TODO Fase 3: debris, projeteis, cercas, Bush, arvores frutiferas.
    }

    // ---------------------------------------------------------------- personagens

    private void CollectFarmers(GameLocation location, Camera3D camera, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach (Farmer farmer in location.farmers)
        {
            // Em primeira pessoa nao desenhamos o proprio corpo.
            // TODO Fase 7: viewmodel da ferramenta na mao, no estilo Doom.
            if (camera.IsFirstPerson && farmer.IsLocalPlayer)
                continue;

            Texture2D? texture = GetFarmerTexture(farmer);
            if (texture is null)
                continue;

            SpriteMesh? mesh = builder.GetOrBuild(texture, new Rectangle(0, 0, 16, 32), config.SpriteThicknessPixels, cross: false);
            if (mesh is null)
                continue;

            batch.Add(new SpriteInstance
            {
                Mesh = mesh,
                Anchor = Camera3D.WorldFromPixels(farmer.getStandingPosition()),
                Yaw = camera.Yaw,
                Tint = Color.White
            });
        }
    }

    private void CollectCharacters(GameLocation location, Camera3D camera, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach (NPC npc in location.characters)
        {
            if (npc.Sprite?.Texture is null || npc.IsInvisible)
                continue;

            SpriteMesh? mesh = builder.GetOrBuild(npc.Sprite.Texture, npc.Sprite.SourceRect, config.SpriteThicknessPixels, cross: false);
            if (mesh is null)
                continue;

            batch.Add(new SpriteInstance
            {
                Mesh = mesh,
                Anchor = Camera3D.WorldFromPixels(npc.getStandingPosition()),
                Yaw = camera.Yaw,
                Tint = Color.White
            });
        }

        foreach (FarmAnimal animal in location.animals.Values)
        {
            if (animal.Sprite?.Texture is null)
                continue;

            SpriteMesh? mesh = builder.GetOrBuild(animal.Sprite.Texture, animal.Sprite.SourceRect, config.SpriteThicknessPixels, cross: false);
            if (mesh is null)
                continue;

            batch.Add(new SpriteInstance
            {
                Mesh = mesh,
                Anchor = Camera3D.WorldFromPixels(animal.getStandingPosition()),
                Yaw = camera.Yaw,
                Tint = Color.White
            });
        }
    }

    // ---------------------------------------------------------------- construcoes

    /// <summary>
    /// Casas, celeiros e estufas. Sao fachadas: nao usam cross, porque duas fachadas
    /// perpendiculares nao formam um predio -- formam uma confusao. Ficam com orientacao
    /// fixa, voltadas para o sul, como no 2D, e com espessura maior para parecerem solidas.
    /// TODO Fase 2: substituir por geometria de caixa com paredes e telhado reais.
    /// </summary>
    private void CollectBuildings(GameLocation location, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach (Building building in location.buildings)
        {
            Texture2D? texture = building.texture?.Value;
            if (texture is null)
                continue;

            Rectangle source;
            try
            {
                source = building.getSourceRect();
            }
            catch (Exception)
            {
                continue;
            }

            SpriteMesh? mesh = builder.GetOrBuild(texture, source, BuildingThickness, cross: false);
            if (mesh is null)
                continue;

            batch.Add(new SpriteInstance
            {
                Mesh = mesh,
                Anchor = new Vector3(
                    building.tileX.Value + building.tilesWide.Value * 0.5f,
                    0f,
                    building.tileY.Value + building.tilesHigh.Value),
                Yaw = 0f,
                Tint = Color.White
            });
        }
    }

    // ---------------------------------------------------------------- terreno

    private void CollectTerrainFeatures(GameLocation location, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach ((Vector2 tile, TerrainFeature feature) in location.terrainFeatures.Pairs)
        {
            switch (feature)
            {
                case Tree tree when tree.texture.Value is not null && tree.growthStage.Value >= 5:
                    this.AddProp(batch, builder, config, tree.texture.Value, Tree.treeTopSourceRect,
                        new Vector3(tile.X + 0.5f, 0f, tile.Y + 1f));
                    break;

                case HoeDirt dirt when dirt.crop is not null && !dirt.crop.dead.Value:
                {
                    Texture2D? texture = GetCropTexture(dirt.crop);
                    if (texture is null)
                        break;

                    Rectangle source;
                    try
                    {
                        source = dirt.crop.getSourceRect(0);
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    this.AddProp(batch, builder, config, texture, source,
                        new Vector3(tile.X + 0.5f, 0f, tile.Y + 1f));
                    break;
                }

                case Grass grass when grass.texture.Value is not null:
                {
                    int row = Math.Max(0, grass.grassType.Value - 1);
                    this.AddProp(batch, builder, config, grass.texture.Value,
                        new Rectangle(0, row * 20, 15, 20),
                        new Vector3(tile.X + 0.5f, 0f, tile.Y + 1f));
                    break;
                }

                // TODO Fase 3: FruitTree, Bush, Flooring.
            }
        }
    }

    /// <summary>Tocos, pedregulhos e meteoritos: ocupam varios tiles, logo a ancora e o centro da base.</summary>
    private void CollectResourceClumps(GameLocation location, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach (ResourceClump clump in location.resourceClumps)
        {
            Texture2D? texture = LoadTexture(clump.textureName.Value);
            if (texture is null)
                continue;

            Rectangle source = Game1.getSourceRectForStandardTileSheet(texture, clump.parentSheetIndex.Value, 16, 16);
            source.Width = clump.width.Value * 16;
            source.Height = clump.height.Value * 16;

            this.AddProp(batch, builder, config, texture, source, new Vector3(
                clump.Tile.X + clump.width.Value * 0.5f,
                0f,
                clump.Tile.Y + clump.height.Value));
        }
    }

    // ---------------------------------------------------------------- itens

    private void CollectObjects(GameLocation location, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach ((Vector2 _, SObject obj) in location.objects.Pairs)
        {
            if (TryGetItemSprite(obj.QualifiedItemId, out Texture2D? texture, out Rectangle source))
                this.AddProp(batch, builder, config, texture!, source, AnchorFromBounds(obj.boundingBox.Value));
        }
    }

    private void CollectFurniture(GameLocation location, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach (Furniture furniture in location.furniture)
        {
            if (TryGetItemSprite(furniture.QualifiedItemId, out Texture2D? texture, out Rectangle source))
                this.AddProp(batch, builder, config, texture!, source, AnchorFromBounds(furniture.boundingBox.Value));
        }
    }

    // ---------------------------------------------------------------- borda do mapa

    /// <summary>
    /// Paredao de arvores em volta do mapa. No 2D a borda nunca era um problema porque a
    /// camera simplesmente nao chegava la; em primeira pessoa o vazio fica a vista. Em vez de
    /// travar o olhar, preenchemos o fundo com mata, que some no fog e ganha paralaxe
    /// natural da perspectiva.
    /// </summary>
    private void CollectMapBorder(GameLocation location, Camera3D camera, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        if (!location.IsOutdoors || location.Map?.Layers.Count is null or 0)
            return;

        Texture2D? texture = LoadTexture($"TerrainFeatures\\tree1_{Game1.currentSeason}");
        if (texture is null)
            return;

        SpriteMesh? mesh = builder.GetOrBuild(texture, Tree.treeTopSourceRect, config.SpriteThicknessPixels, cross: true);
        if (mesh is null)
            return;

        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;

        const int depth = 5;
        const int stride = 2;
        float maxDistanceSq = config.MapBorderDistance * config.MapBorderDistance;
        Vector3 eye = camera.Position;

        for (int ring = 1; ring <= depth; ring++)
        {
            int minX = -ring * stride;
            int maxX = width + ring * stride;
            int minZ = -ring * stride;
            int maxZ = height + ring * stride;

            for (int x = minX; x <= maxX; x += stride)
            {
                this.TryAddBorderTree(batch, mesh, eye, maxDistanceSq, x, minZ);
                this.TryAddBorderTree(batch, mesh, eye, maxDistanceSq, x, maxZ);
            }

            for (int z = minZ + stride; z < maxZ; z += stride)
            {
                this.TryAddBorderTree(batch, mesh, eye, maxDistanceSq, minX, z);
                this.TryAddBorderTree(batch, mesh, eye, maxDistanceSq, maxX, z);
            }
        }
    }

    private void TryAddBorderTree(SpriteMeshBatch batch, SpriteMesh mesh, Vector3 eye, float maxDistanceSq, int x, int z)
    {
        // Hash estavel: a mata nao pode tremer entre frames.
        int hash = HashCode.Combine(x, z);
        if ((hash & 3) == 0)
            return;

        float jitterX = ((hash >> 4) & 7) / 8f;
        float jitterZ = ((hash >> 8) & 7) / 8f;
        var anchor = new Vector3(x + jitterX, 0f, z + jitterZ);

        if (Vector3.DistanceSquared(anchor, eye) > maxDistanceSq)
            return;

        batch.Add(new SpriteInstance
        {
            Mesh = mesh,
            Anchor = anchor,
            Yaw = ((hash >> 12) & 3) * MathHelper.PiOver2,
            Tint = Color.White
        });
    }

    // ---------------------------------------------------------------- apoio

    private void AddProp(SpriteMeshBatch batch, SpriteMeshBuilder builder, ModConfig config, Texture2D texture, Rectangle source, Vector3 anchor)
    {
        SpriteMesh? mesh = builder.GetOrBuild(texture, source, config.SpriteThicknessPixels, config.CrossMeshProps);
        if (mesh is not null)
            batch.Add(new SpriteInstance { Mesh = mesh, Anchor = anchor, Tint = Color.White });
    }

    /// <summary>
    /// Ancora um movel ou objeto pela base da sua caixa de colisao, nao pelo tile de origem.
    /// Um sofa ocupa varios tiles: ancorado no tile de origem, ele flutua e fica deslocado
    /// para o norte, que e exatamente o que acontecia.
    /// </summary>
    private static Vector3 AnchorFromBounds(Rectangle bounds)
        => new(bounds.Center.X / 64f, 0f, bounds.Bottom / 64f);

    private static bool TryGetItemSprite(string qualifiedItemId, out Texture2D? texture, out Rectangle source)
    {
        texture = null;
        source = Rectangle.Empty;

        try
        {
            var data = ItemRegistry.GetDataOrErrorItem(qualifiedItemId);
            texture = data.GetTexture();
            source = data.GetSourceRect();
            return texture is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Texture2D? LoadTexture(string? assetName)
    {
        if (string.IsNullOrEmpty(assetName))
            return null;

        try
        {
            return Game1.content.Load<Texture2D>(assetName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Texture2D? GetCropTexture(Crop crop)
    {
        try
        {
            return crop.DrawnCropTexture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Texture2D? GetFarmerTexture(Farmer farmer)
    {
        try
        {
            string? name = farmer.FarmerRenderer?.textureName.Value;
            return name is null ? null : Game1.content.Load<Texture2D>(name);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

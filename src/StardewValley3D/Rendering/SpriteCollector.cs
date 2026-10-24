using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace StardewValley3D.Rendering;

/// <summary>
/// Varre o estado da localizacao e emite uma instancia volumetrica por sprite.
///
/// Duas formas de montagem, conforme o tipo:
///   - <b>personagens</b> (jogador, NPC, monstro, animal): fatia extrudada que gira para
///     encarar a camera, com o frame direcional escolhido pelo angulo -- o truque do Doom,
///     agora com espessura real;
///   - <b>cenario</b> (arvore, objeto, pedra): duas fatias perpendiculares (cross), com
///     orientacao fixa, que tem volume de qualquer angulo.
/// </summary>
public sealed class SpriteCollector
{
    public void Collect(GameLocation location, Camera3D camera, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        this.CollectFarmers(location, camera, config, builder, batch);
        this.CollectCharacters(location, camera, config, builder, batch);
        this.CollectTerrainFeatures(location, config, builder, batch);
        this.CollectObjects(location, config, builder, batch);

        // TODO Fase 3: resource clumps, buildings, debris, projectiles, moveis.
    }

    private void CollectFarmers(GameLocation location, Camera3D camera, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach (Farmer farmer in location.farmers)
        {
            // Em primeira pessoa nao desenhamos o proprio corpo.
            // TODO Fase 7: viewmodel da ferramenta na mao, no estilo Doom.
            if (camera.IsFirstPerson && farmer.IsLocalPlayer)
                continue;

            if (farmer.FarmerRenderer is null)
                continue;

            Texture2D? texture = GetFarmerTexture(farmer);
            if (texture is null)
                continue;

            var source = new Rectangle(0, 0, 16, 32);
            SpriteMesh? mesh = builder.GetOrBuild(texture, source, config.SpriteThicknessPixels, cross: false);
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

            Rectangle source = npc.Sprite.SourceRect;
            SpriteMesh? mesh = builder.GetOrBuild(npc.Sprite.Texture, source, config.SpriteThicknessPixels, cross: false);
            if (mesh is null)
                continue;

            batch.Add(new SpriteInstance
            {
                Mesh = mesh,
                Anchor = Camera3D.WorldFromPixels(npc.getStandingPosition()),
                Yaw = camera.Yaw,
                Tint = Color.White,
                FlipHorizontal = npc.Sprite.CurrentAnimation is null && npc.FacingDirection == 3
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

    private void CollectTerrainFeatures(GameLocation location, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach ((Vector2 tile, TerrainFeature feature) in location.terrainFeatures.Pairs)
        {
            switch (feature)
            {
                case Tree tree when tree.texture.Value is not null && tree.growthStage.Value >= 5:
                {
                    SpriteMesh? mesh = builder.GetOrBuild(
                        tree.texture.Value, Tree.treeTopSourceRect, config.SpriteThicknessPixels, config.CrossMeshProps);

                    if (mesh is not null)
                    {
                        batch.Add(new SpriteInstance
                        {
                            Mesh = mesh,
                            Anchor = new Vector3(tile.X + 0.5f, 0f, tile.Y + 1f),
                            Tint = Color.White
                        });
                    }
                    break;
                }

                // TODO Fase 3: HoeDirt (cultivo por estagio), Grass, FruitTree, Flooring.
            }
        }
    }

    private void CollectObjects(GameLocation location, ModConfig config, SpriteMeshBuilder builder, SpriteMeshBatch batch)
    {
        foreach ((Vector2 tile, SObject obj) in location.objects.Pairs)
        {
            Texture2D texture;
            Rectangle source;

            try
            {
                var data = StardewValley.ItemRegistry.GetDataOrErrorItem(obj.QualifiedItemId);
                texture = data.GetTexture();
                source = data.GetSourceRect();
            }
            catch (Exception)
            {
                continue;
            }

            SpriteMesh? mesh = builder.GetOrBuild(texture, source, config.SpriteThicknessPixels, config.CrossMeshProps);
            if (mesh is null)
                continue;

            batch.Add(new SpriteInstance
            {
                Mesh = mesh,
                Anchor = new Vector3(tile.X + 0.5f, 0f, tile.Y + 0.5f),
                Tint = Color.White
            });
        }
    }

    private static Texture2D? GetFarmerTexture(Farmer farmer)
    {
        try
        {
            string? name = farmer.FarmerRenderer.textureName.Value;
            return name is null ? null : Game1.content.Load<Texture2D>(name);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

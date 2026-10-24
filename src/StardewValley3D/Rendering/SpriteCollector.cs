using System;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace StardewValley3D.Rendering;

/// <summary>
/// Varre o estado da localizacao e emite um billboard por sprite. Este e o coracao da
/// Fase 3: cada tipo de entidade do Stardew precisa de um mapeamento aqui.
///
/// Cobertura planejada: Farmer (local e remotos), NPC, Monster, FarmAnimal, Object,
/// TerrainFeature (arvore, grama, cultivo, HoeDirt), ResourceClump, Building, Debris,
/// Projectile, Furniture, CrabPot, Fence.
/// </summary>
public sealed class SpriteCollector
{
    /// <summary>Um tile do Stardew tem 64 px; sprites sao medidos nessa escala.</summary>
    private const float Px = 1f / 64f;

    public void Collect(GameLocation location, Camera3D camera, ModConfig config, BillboardBatch batch)
    {
        this.CollectFarmers(location, camera, config, batch);
        this.CollectCharacters(location, camera, config, batch);
        this.CollectTerrainFeatures(location, config, batch);

        // TODO Fase 3: objects, resource clumps, buildings, debris, projectiles, animals.
    }

    private void CollectFarmers(GameLocation location, Camera3D camera, ModConfig config, BillboardBatch batch)
    {
        foreach (Farmer farmer in location.farmers)
        {
            Vector3 anchor = Camera3D.WorldFromPixels(farmer.getStandingPosition());
            batch.Add(new Billboard
            {
                Anchor = anchor,
                Texture = farmer.FarmerRenderer.textureName.Value is null
                    ? Game1.staminaRect
                    : Game1.content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>(farmer.FarmerRenderer.textureName.Value),
                Source = new Rectangle(0, 0, 16, 32),
                Size = new Vector2(16 * Px * 4f, 32 * Px * 4f),
                Tint = Color.White,
                FlipHorizontal = this.ShouldFlip(farmer, camera)
            });
        }
    }

    private void CollectCharacters(GameLocation location, Camera3D camera, ModConfig config, BillboardBatch batch)
    {
        foreach (NPC npc in location.characters)
        {
            if (npc.Sprite?.Texture is null)
                continue;

            Vector3 anchor = Camera3D.WorldFromPixels(npc.getStandingPosition());
            Rectangle source = npc.Sprite.SourceRect;

            batch.Add(new Billboard
            {
                Anchor = anchor,
                Texture = npc.Sprite.Texture,
                Source = source,
                Size = new Vector2(source.Width * Px * 4f, source.Height * Px * 4f),
                Tint = Color.White,
                FlipHorizontal = this.ShouldFlip(npc, camera)
            });
        }
    }

    private void CollectTerrainFeatures(GameLocation location, ModConfig config, BillboardBatch batch)
    {
        foreach ((Vector2 tile, TerrainFeature feature) in location.terrainFeatures.Pairs)
        {
            switch (feature)
            {
                case Tree tree when tree.texture.Value is not null:
                    batch.Add(new Billboard
                    {
                        Anchor = new Vector3(tile.X + 0.5f, 0f, tile.Y + 0.5f),
                        Texture = tree.texture.Value,
                        Source = Tree.treeTopSourceRect,
                        Size = new Vector2(3f, 5f),
                        Tint = Color.White,
                        Cross = config.CrossBillboardTrees
                    });
                    break;

                // TODO Fase 3: HoeDirt (cultivo + estagio), Grass, FruitTree, Flooring.
            }
        }
    }

    /// <summary>
    /// O Stardew tem 4 direcoes de sprite; a camera pode estar em qualquer angulo. Espelhamos
    /// quando a camera ve o personagem pelo lado oposto ao do frame disponivel -- o mesmo
    /// truque que o Doom faz com seus 8 angulos.
    /// </summary>
    private bool ShouldFlip(Character character, Camera3D camera)
    {
        // facingDirection: 0=norte, 1=leste, 2=sul, 3=oeste
        if (character.FacingDirection is not (1 or 3))
            return false;

        Vector2 right = camera.FlatRight;
        bool cameraSeesEast = right.X >= 0f;
        return character.FacingDirection == 1 ? !cameraSeesEast : cameraSeesEast;
    }
}

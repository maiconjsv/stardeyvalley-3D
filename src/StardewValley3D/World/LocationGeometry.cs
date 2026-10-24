using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using xTile.Dimensions;
using xTile.Tiles;

namespace StardewValley3D.World;

/// <summary>
/// Dado de altura de uma localizacao: o "WAD" do Stardew. Resolve, por tile, a altura do
/// chao e o topo da parede, combinando (em ordem de prioridade):
///
///   1. setores autorais vindos de assets/geometry/&lt;Location&gt;.json
///   2. heuristica sobre o mapa xTile (camada Buildings = solido, propriedade Water, etc.)
///
/// Isso garante que qualquer localizacao -- inclusive de outros mods -- tenha geometria
/// utilizavel sem trabalho manual, e que possamos corrigir a mao onde a heuristica erra.
/// </summary>
public sealed class LocationGeometry
{
    /// <summary>Altura padrao de uma parede derivada por heuristica, em tiles.</summary>
    public const float DefaultWallHeight = 2.5f;
    public const float WaterDepth = -0.35f;

    private readonly GameLocation location;
    private readonly int width;
    private readonly int height;

    private readonly float[] floor;
    private readonly float[] wallTop;

    /// <summary>Setores autorais, aplicados por cima da heuristica.</summary>
    public List<GeometrySector> Sectors { get; init; } = new();

    /// <summary>True quando ha dado autoral; usado pelo fallback da config.</summary>
    public bool IsAuthored { get; private set; }

    public LocationGeometry(GameLocation location)
    {
        this.location = location;
        this.width = location.Map.Layers[0].LayerWidth;
        this.height = location.Map.Layers[0].LayerHeight;
        this.floor = new float[this.width * this.height];
        this.wallTop = new float[this.width * this.height];
    }

    public float FloorHeightAt(int x, int z)
        => this.InBounds(x, z) ? this.floor[z * this.width + x] : 0f;

    public float WallTopAt(int x, int z)
        => this.InBounds(x, z) ? this.wallTop[z * this.width + x] : 0f;

    private bool InBounds(int x, int z)
        => x >= 0 && z >= 0 && x < this.width && z < this.height;

    /// <summary>Roda a heuristica sobre o mapa e depois aplica os setores autorais.</summary>
    public LocationGeometry Bake()
    {
        var buildings = this.location.Map.GetLayer("Buildings");
        var back = this.location.Map.GetLayer("Back");

        for (int z = 0; z < this.height; z++)
        {
            for (int x = 0; x < this.width; x++)
            {
                int i = z * this.width + x;
                float f = 0f;
                float top = 0f;

                // Agua: chao rebaixado, sem parede.
                if (back is not null && this.HasTileProperty(back, x, z, "Water"))
                    f = WaterDepth;

                // Camada Buildings com tile e sem passagem: parede solida.
                Tile? tile = buildings?.Tiles[new Location(x, z)];
                if (tile is not null && !this.IsPassable(tile))
                    top = DefaultWallHeight;

                this.floor[i] = f;
                this.wallTop[i] = top;
            }
        }

        foreach (GeometrySector sector in this.Sectors)
        {
            this.IsAuthored = true;
            for (int z = sector.Area.Top; z < sector.Area.Bottom; z++)
            {
                for (int x = sector.Area.Left; x < sector.Area.Right; x++)
                {
                    if (!this.InBounds(x, z))
                        continue;

                    int i = z * this.width + x;
                    if (sector.FloorHeight.HasValue)
                        this.floor[i] = sector.FloorHeight.Value;
                    if (sector.WallTop.HasValue)
                        this.wallTop[i] = sector.WallTop.Value;
                }
            }
        }

        return this;
    }

    private bool HasTileProperty(xTile.Layers.Layer layer, int x, int z, string property)
    {
        Tile? tile = layer.Tiles[new Location(x, z)];
        return tile is not null && tile.TileIndexProperties.ContainsKey(property);
    }

    private bool IsPassable(Tile tile)
        => tile.Properties.ContainsKey("Passable") || tile.TileIndexProperties.ContainsKey("Passable");
}

/// <summary>Setor autoral, no espirito dos setores do Doom. Serializado em JSON.</summary>
public sealed class GeometrySector
{
    /// <summary>Area em tiles (X, Y, largura, altura).</summary>
    public Rectangle Area { get; set; }
    public float? FloorHeight { get; set; }
    public float? WallTop { get; set; }
    /// <summary>Nome da tilesheet usada nas faces verticais; null = herda do chao.</summary>
    public string? WallTexture { get; set; }
    public string? Comment { get; set; }
}

/// <summary>Formato do arquivo assets/geometry/&lt;Location&gt;.json.</summary>
public sealed class GeometryFile
{
    public string Location { get; set; } = "";
    public float CeilingHeight { get; set; }
    public bool Interior { get; set; }
    public List<GeometrySector> Sectors { get; set; } = new();
}

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StardewValley3D.Rendering;

/// <summary>
/// Resolve UVs de tiles e sprites dentro de uma textura. Aplica meio-pixel de inset para
/// evitar bleeding entre tiles vizinhos (artefato classico ao texturizar quads com atlas).
/// </summary>
public sealed class TileAtlas
{
    private readonly Dictionary<Rectangle, Vector4> cache = new();

    public Texture2D Texture { get; }

    public TileAtlas(Texture2D texture) => this.Texture = texture;

    /// <summary>Retorna (u0, v0, u1, v1) normalizados para um retangulo em pixels.</summary>
    public Vector4 GetUv(Rectangle source)
    {
        if (this.cache.TryGetValue(source, out Vector4 cached))
            return cached;

        const float inset = 0.5f;
        float w = this.Texture.Width;
        float h = this.Texture.Height;

        Vector4 uv = new(
            (source.X + inset) / w,
            (source.Y + inset) / h,
            (source.Right - inset) / w,
            (source.Bottom - inset) / h);

        this.cache[source] = uv;
        return uv;
    }

    /// <summary>Retangulo em pixels de um tile index em uma tilesheet de grade regular.</summary>
    public static Rectangle TileRect(int index, int tileWidth, int tileHeight, int sheetWidth, int margin = 0, int spacing = 0)
    {
        int columns = (sheetWidth - margin * 2 + spacing) / (tileWidth + spacing);
        if (columns <= 0)
            columns = 1;

        int col = index % columns;
        int row = index / columns;

        return new Rectangle(
            margin + col * (tileWidth + spacing),
            margin + row * (tileHeight + spacing),
            tileWidth,
            tileHeight);
    }
}

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StardewValley3D.Rendering;

/// <summary>Um sprite do mundo 2D promovido a quad no espaco 3D.</summary>
public struct Billboard
{
    /// <summary>Posicao do ponto de ancoragem, em unidades de mundo (base do sprite, no chao).</summary>
    public Vector3 Anchor;
    public Texture2D Texture;
    public Rectangle Source;
    /// <summary>Tamanho em tiles (largura, altura).</summary>
    public Vector2 Size;
    public Color Tint;
    public bool FlipHorizontal;
    /// <summary>Emite um segundo quad rotacionado 90 graus (arvores leem muito melhor assim).</summary>
    public bool Cross;
}

/// <summary>
/// Desenha sprites como quads alinhados ao eixo Y: rotacionam em torno de Y para encarar a
/// camera, mas nunca inclinam. E exatamente a tecnica do Doom, e e o que preserva a leitura
/// dos sprites originais do Stardew.
///
/// Usa alpha test (nao alpha blend) para que o depth buffer resolva a ordenacao sozinho;
/// so os sprites realmente translucidos entram no passe ordenado por distancia.
/// </summary>
public sealed class BillboardBatch
{
    private readonly List<Billboard> opaque = new(1024);
    private readonly List<Billboard> transparent = new(256);
    private readonly Dictionary<Texture2D, TileAtlas> atlases = new();
    private VertexPositionColorTexture[] scratch = new VertexPositionColorTexture[4096];

    public int LastQuadCount { get; private set; }

    public void Begin()
    {
        this.opaque.Clear();
        this.transparent.Clear();
    }

    public void Add(in Billboard billboard)
    {
        if (billboard.Tint.A < 255)
            this.transparent.Add(billboard);
        else
            this.opaque.Add(billboard);
    }

    public void End(GraphicsDevice device, BasicEffect effect, Camera3D camera)
    {
        this.LastQuadCount = 0;

        // Opacos: agrupados por textura, sem ordenacao (o depth buffer resolve).
        this.opaque.Sort(static (a, b) => a.Texture.GetHashCode().CompareTo(b.Texture.GetHashCode()));
        this.Flush(device, effect, camera, this.opaque, alphaBlend: false);

        // Translucidos: do mais distante para o mais proximo.
        Vector3 eye = camera.Position;
        this.transparent.Sort((a, b) =>
            Vector3.DistanceSquared(b.Anchor, eye).CompareTo(Vector3.DistanceSquared(a.Anchor, eye)));
        this.Flush(device, effect, camera, this.transparent, alphaBlend: true);
    }

    private void Flush(GraphicsDevice device, BasicEffect effect, Camera3D camera, List<Billboard> list, bool alphaBlend)
    {
        if (list.Count == 0)
            return;

        device.BlendState = alphaBlend ? BlendState.AlphaBlend : BlendState.Opaque;
        device.DepthStencilState = alphaBlend ? DepthStencilState.DepthRead : DepthStencilState.Default;
        device.SamplerStates[0] = SamplerState.PointClamp;
        device.RasterizerState = RasterizerState.CullNone;

        // Billboards encaram a camera girando apenas em Y.
        Vector2 flat = camera.FlatForward;
        Vector3 right = Vector3.Normalize(new Vector3(flat.Y, 0f, -flat.X));
        Vector3 crossRight = Vector3.Normalize(new Vector3(flat.X, 0f, flat.Y));

        int index = 0;
        Texture2D? current = null;

        foreach (Billboard billboard in list)
        {
            if (current is not null && (billboard.Texture != current || index + 12 > this.scratch.Length))
            {
                this.Draw(device, effect, current, index);
                index = 0;
            }

            current = billboard.Texture;
            if (!this.atlases.TryGetValue(billboard.Texture, out TileAtlas? atlas))
                this.atlases[billboard.Texture] = atlas = new TileAtlas(billboard.Texture);

            Vector4 uv = atlas.GetUv(billboard.Source);

            index = this.AppendQuad(billboard, right, uv, index);
            if (billboard.Cross)
                index = this.AppendQuad(billboard, crossRight, uv, index);
        }

        if (current is not null && index > 0)
            this.Draw(device, effect, current, index);
    }

    private int AppendQuad(in Billboard b, Vector3 right, Vector4 uv, int index)
    {
        Vector3 halfRight = right * (b.Size.X * 0.5f);
        Vector3 up = Vector3.Up * b.Size.Y;

        Vector3 bl = b.Anchor - halfRight;
        Vector3 br = b.Anchor + halfRight;
        Vector3 tl = bl + up;
        Vector3 tr = br + up;

        float u0 = b.FlipHorizontal ? uv.Z : uv.X;
        float u1 = b.FlipHorizontal ? uv.X : uv.Z;

        this.EnsureCapacity(index + 6);

        this.scratch[index++] = new VertexPositionColorTexture(bl, b.Tint, new Vector2(u0, uv.W));
        this.scratch[index++] = new VertexPositionColorTexture(tl, b.Tint, new Vector2(u0, uv.Y));
        this.scratch[index++] = new VertexPositionColorTexture(tr, b.Tint, new Vector2(u1, uv.Y));

        this.scratch[index++] = new VertexPositionColorTexture(bl, b.Tint, new Vector2(u0, uv.W));
        this.scratch[index++] = new VertexPositionColorTexture(tr, b.Tint, new Vector2(u1, uv.Y));
        this.scratch[index++] = new VertexPositionColorTexture(br, b.Tint, new Vector2(u1, uv.W));

        return index;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= this.scratch.Length)
            return;

        Array.Resize(ref this.scratch, Math.Max(needed, this.scratch.Length * 2));
    }

    private void Draw(GraphicsDevice device, BasicEffect effect, Texture2D texture, int vertexCount)
    {
        effect.Texture = texture;
        effect.TextureEnabled = true;
        effect.VertexColorEnabled = true;

        foreach (EffectPass pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, this.scratch, 0, vertexCount / 3);
        }

        this.LastQuadCount += vertexCount / 6;
    }
}

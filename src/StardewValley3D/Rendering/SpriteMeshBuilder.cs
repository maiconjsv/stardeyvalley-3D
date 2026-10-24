using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace StardewValley3D.Rendering;

/// <summary>Malha 3D gerada a partir de um sprite 2D.</summary>
public sealed class SpriteMesh
{
    public Texture2D Texture { get; }
    public VertexBuffer Buffer { get; }
    public int TriangleCount { get; }
    /// <summary>Tamanho em unidades de mundo (tiles).</summary>
    public Vector2 Size { get; }

    public SpriteMesh(Texture2D texture, VertexBuffer buffer, int triangleCount, Vector2 size)
    {
        this.Texture = texture;
        this.Buffer = buffer;
        this.TriangleCount = triangleCount;
        this.Size = size;
    }

    public void Dispose() => this.Buffer.Dispose();
}

/// <summary>
/// Converte sprites 2D em volumes 3D por extrusao da silhueta -- o processo que da
/// profundidade aos assets originais do jogo.
///
/// Para cada sprite:
///   - a face frontal e a traseira sao quads texturizados, recortados por alpha test;
///   - cada aresta de fronteira da silhueta (pixel opaco vizinho de pixel transparente)
///     vira um quad lateral com a cor daquele pixel, dando espessura real ao contorno.
///
/// As arestas sao agrupadas em corridas (runs) antes de virar geometria: um contorno de
/// personagem 16x32 cai de ~200 quads para algumas dezenas.
///
/// Duas formas de montagem:
///   - <b>slab</b>: uma fatia extrudada; usada em personagens, combinada com a escolha do
///     frame direcional e rotacao para encarar a camera;
///   - <b>cross</b>: duas fatias perpendiculares; usada em arvores, arbustos e objetos, que
///     assim tem volume de qualquer angulo sem depender da camera.
/// </summary>
public sealed class SpriteMeshBuilder
{
    /// <summary>O jogo desenha sprites com zoom 4x: 16 px de sprite = 1 tile.</summary>
    public const float PixelsPerUnit = 16f;

    private const byte AlphaThreshold = 128;

    private readonly IMonitor monitor;
    private readonly GraphicsDevice device;
    private readonly Dictionary<(Texture2D, Rectangle, int, bool), SpriteMesh?> cache = new();
    private readonly Dictionary<Texture2D, Color[]?> pixelCache = new();

    public SpriteMeshBuilder(GraphicsDevice device, IMonitor monitor)
    {
        this.device = device;
        this.monitor = monitor;
    }

    public int CachedMeshCount => this.cache.Count;

    public void Clear()
    {
        foreach (SpriteMesh? mesh in this.cache.Values)
            mesh?.Dispose();

        this.cache.Clear();
        this.pixelCache.Clear();
    }

    /// <summary>Obtem (ou constroi) a malha de um sprite. Retorna null se o sprite for vazio.</summary>
    public SpriteMesh? GetOrBuild(Texture2D texture, Rectangle source, int thicknessPixels, bool cross)
    {
        var key = (texture, source, thicknessPixels, cross);
        if (this.cache.TryGetValue(key, out SpriteMesh? cached))
            return cached;

        SpriteMesh? mesh = this.Build(texture, source, thicknessPixels, cross);
        this.cache[key] = mesh;
        return mesh;
    }

    private SpriteMesh? Build(Texture2D texture, Rectangle source, int thicknessPixels, bool cross)
    {
        Color[]? pixels = this.GetPixels(texture);
        if (pixels is null)
            return null;

        int w = source.Width;
        int h = source.Height;
        if (w <= 0 || h <= 0)
            return null;

        bool[] solid = new bool[w * h];
        bool any = false;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int sx = source.X + x;
                int sy = source.Y + y;
                if (sx < 0 || sy < 0 || sx >= texture.Width || sy >= texture.Height)
                    continue;

                bool opaque = pixels[sy * texture.Width + sx].A >= AlphaThreshold;
                solid[y * w + x] = opaque;
                any |= opaque;
            }
        }

        if (!any)
            return null;

        float unit = 1f / PixelsPerUnit;
        var size = new Vector2(w * unit, h * unit);
        float thickness = Math.Max(thicknessPixels, 1) * unit;

        var vertices = new List<VertexPositionTexture>(256);
        this.EmitSlab(vertices, texture, source, solid, w, h, unit, thickness, rotated: false);
        if (cross)
            this.EmitSlab(vertices, texture, source, solid, w, h, unit, thickness, rotated: true);

        if (vertices.Count == 0)
            return null;

        var buffer = new VertexBuffer(this.device, typeof(VertexPositionTexture), vertices.Count, BufferUsage.WriteOnly);
        buffer.SetData(vertices.ToArray());

        return new SpriteMesh(texture, buffer, vertices.Count / 3, size);
    }

    /// <summary>
    /// Emite uma fatia: faces frontal e traseira mais as laterais da silhueta. A origem local
    /// fica na base, no centro -- e o ponto que encosta no chao.
    /// </summary>
    private void EmitSlab(
        List<VertexPositionTexture> vertices,
        Texture2D texture, Rectangle source, bool[] solid,
        int w, int h, float unit, float thickness, bool rotated)
    {
        float halfW = w * unit * 0.5f;
        float halfT = thickness * 0.5f;

        // Converte um ponto do plano do sprite (x para a direita, z para a frente) aplicando
        // o giro de 90 graus da segunda fatia do cross.
        Vector3 Point(float x, float y, float z)
            => rotated ? new Vector3(z, y, -x) : new Vector3(x, y, z);

        Vector2 Uv(float px, float py)
            => new((source.X + px) / texture.Width, (source.Y + py) / texture.Height);

        // --- faces frontal e traseira ---
        foreach (float z in new[] { halfT, -halfT })
        {
            Vector3 bl = Point(-halfW, 0f, z);
            Vector3 br = Point(halfW, 0f, z);
            Vector3 tl = Point(-halfW, h * unit, z);
            Vector3 tr = Point(halfW, h * unit, z);

            Vector2 uvTl = Uv(0f, 0f);
            Vector2 uvTr = Uv(w, 0f);
            Vector2 uvBl = Uv(0f, h);
            Vector2 uvBr = Uv(w, h);

            vertices.Add(new VertexPositionTexture(bl, uvBl));
            vertices.Add(new VertexPositionTexture(tl, uvTl));
            vertices.Add(new VertexPositionTexture(tr, uvTr));

            vertices.Add(new VertexPositionTexture(bl, uvBl));
            vertices.Add(new VertexPositionTexture(tr, uvTr));
            vertices.Add(new VertexPositionTexture(br, uvBr));
        }

        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && solid[y * w + x];

        // --- arestas horizontais (topo e base da silhueta), agrupadas por corrida ---
        for (int y = 0; y < h; y++)
        {
            for (int dy = -1; dy <= 1; dy += 2)
            {
                int x = 0;
                while (x < w)
                {
                    if (!Solid(x, y) || Solid(x, y + dy))
                    {
                        x++;
                        continue;
                    }

                    int start = x;
                    while (x < w && Solid(x, y) && !Solid(x, y + dy))
                        x++;

                    // dy == -1: aresta superior do pixel; dy == +1: inferior.
                    float py = dy < 0 ? y : y + 1;
                    float wy = (h - py) * unit;
                    float x0 = start * unit - halfW;
                    float x1 = x * unit - halfW;

                    Vector2 uv0 = Uv(start, y + 0.5f);
                    Vector2 uv1 = Uv(x, y + 0.5f);

                    this.EmitQuad(vertices,
                        Point(x0, wy, -halfT), Point(x1, wy, -halfT),
                        Point(x1, wy, halfT), Point(x0, wy, halfT),
                        uv0, uv1, uv1, uv0);
                }
            }
        }

        // --- arestas verticais (laterais da silhueta) ---
        for (int x = 0; x < w; x++)
        {
            for (int dx = -1; dx <= 1; dx += 2)
            {
                int y = 0;
                while (y < h)
                {
                    if (!Solid(x, y) || Solid(x + dx, y))
                    {
                        y++;
                        continue;
                    }

                    int start = y;
                    while (y < h && Solid(x, y) && !Solid(x + dx, y))
                        y++;

                    float px = dx < 0 ? x : x + 1;
                    float wx = px * unit - halfW;
                    float y0 = (h - start) * unit;
                    float y1 = (h - y) * unit;

                    Vector2 uv0 = Uv(x + 0.5f, start);
                    Vector2 uv1 = Uv(x + 0.5f, y);

                    this.EmitQuad(vertices,
                        Point(wx, y0, -halfT), Point(wx, y1, -halfT),
                        Point(wx, y1, halfT), Point(wx, y0, halfT),
                        uv0, uv1, uv1, uv0);
                }
            }
        }
    }

    private void EmitQuad(
        List<VertexPositionTexture> vertices,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        vertices.Add(new VertexPositionTexture(a, ua));
        vertices.Add(new VertexPositionTexture(b, ub));
        vertices.Add(new VertexPositionTexture(c, uc));

        vertices.Add(new VertexPositionTexture(a, ua));
        vertices.Add(new VertexPositionTexture(c, uc));
        vertices.Add(new VertexPositionTexture(d, ud));
    }

    /// <summary>
    /// Le os pixels de uma textura uma unica vez. Texturas de render target ou comprimidas
    /// podem recusar a leitura; nesse caso o sprite e simplesmente ignorado, sem derrubar o
    /// frame.
    /// </summary>
    private Color[]? GetPixels(Texture2D texture)
    {
        if (this.pixelCache.TryGetValue(texture, out Color[]? cached))
            return cached;

        Color[]? pixels = null;
        try
        {
            pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);
        }
        catch (Exception ex)
        {
            pixels = null;
            this.monitor.Log($"Nao foi possivel ler os pixels de uma textura ({texture.Width}x{texture.Height}): {ex.Message}", LogLevel.Trace);
        }

        this.pixelCache[texture] = pixels;
        return pixels;
    }
}

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley3D.World;
using xTile;
using xTile.Layers;
using xTile.Tiles;

namespace StardewValley3D.Rendering;

/// <summary>
/// Converte um mapa xTile em malha 3D estatica: quads de chao a partir das camadas
/// horizontais e paredes extrudadas a partir da colisao/height map.
///
/// A malha e agrupada por textura (uma submalha por tilesheet dentro de cada chunk), que e o
/// que permite desenhar uma localizacao inteira em poucas dezenas de draw calls.
/// O resultado e cacheado por localizacao e invalidado em warp/mudanca de estacao.
/// </summary>
public sealed class WorldGeometryBuilder
{
    /// <summary>Chunk de 16x16 tiles: unidade de culling e de rebuild parcial.</summary>
    public const int ChunkSize = 16;

    /// <summary>Separacao vertical entre camadas, para evitar z-fighting.</summary>
    private const float LayerStep = 0.01f;

    private readonly IMonitor monitor;
    private readonly Dictionary<string, LocationMesh> cache = new();
    private readonly Dictionary<string, Texture2D?> textureCache = new();
    private Texture2D? voidTexture;

    public WorldGeometryBuilder(IMonitor monitor) => this.monitor = monitor;

    public void InvalidateAll()
    {
        foreach (LocationMesh mesh in this.cache.Values)
            mesh.Dispose();
        this.cache.Clear();
    }

    public void Invalidate(string locationName)
    {
        if (this.cache.Remove(locationName, out LocationMesh? mesh))
            mesh.Dispose();
    }

    public LocationMesh GetOrBuild(GraphicsDevice device, GameLocation location, LocationGeometry geometry)
    {
        string key = location.NameOrUniqueName;
        if (this.cache.TryGetValue(key, out LocationMesh? cached))
            return cached;

        LocationMesh mesh = this.Build(device, location, geometry);
        this.cache[key] = mesh;
        this.monitor.Log(
            $"Geometria construida para '{key}': {mesh.ChunkCount} chunks, {mesh.SubmeshCount} submalhas, {mesh.TriangleCount} triangulos.",
            LogLevel.Debug);
        return mesh;
    }

    private LocationMesh Build(GraphicsDevice device, GameLocation location, LocationGeometry geometry)
    {
        Map map = location.Map;
        int width = map.Layers[0].LayerWidth;
        int height = map.Layers[0].LayerHeight;

        Layer? back = map.GetLayer("Back");
        Layer? buildings = map.GetLayer("Buildings");
        Layer? front = map.GetLayer("Front");

        var chunks = new List<MeshChunk>();

        for (int cz = 0; cz < height; cz += ChunkSize)
        {
            for (int cx = 0; cx < width; cx += ChunkSize)
            {
                // Uma lista de vertices por textura: vira uma submalha no fim do chunk.
                var byTexture = new Dictionary<Texture2D, List<VertexPositionTexture>>();

                int maxX = Math.Min(cx + ChunkSize, width);
                int maxZ = Math.Min(cz + ChunkSize, height);

                for (int z = cz; z < maxZ; z++)
                {
                    for (int x = cx; x < maxX; x++)
                    {
                        float floor = geometry.FloorHeightAt(x, z);
                        float wallTop = geometry.WallTopAt(x, z);

                        this.EmitFloor(byTexture, back, x, z, floor);

                        Tile? buildingTile = GetTile(buildings, x, z);
                        if (buildingTile is not null)
                        {
                            if (wallTop > floor)
                                this.EmitWalls(byTexture, geometry, buildingTile, x, z, floor, wallTop);
                            else
                                this.EmitFloor(byTexture, buildings, x, z, floor + LayerStep);
                        }

                        this.EmitFloor(byTexture, front, x, z, floor + LayerStep * 2f);
                    }
                }

                if (byTexture.Count == 0)
                    continue;

                var submeshes = new List<Submesh>(byTexture.Count);
                foreach ((Texture2D texture, List<VertexPositionTexture> vertices) in byTexture)
                {
                    if (vertices.Count == 0)
                        continue;

                    var buffer = new VertexBuffer(device, typeof(VertexPositionTexture), vertices.Count, BufferUsage.WriteOnly);
                    buffer.SetData(vertices.ToArray());
                    submeshes.Add(new Submesh(texture, buffer, vertices.Count / 3));
                }

                if (submeshes.Count == 0)
                    continue;

                chunks.Add(new MeshChunk(
                    submeshes,
                    new BoundingBox(
                        new Vector3(cx, -4f, cz),
                        new Vector3(maxX, 12f, maxZ))));
            }
        }

        if (!location.IsOutdoors)
            this.EmitInteriorEnclosure(device, chunks, width, height);

        return new LocationMesh(chunks);
    }

    /// <summary>
    /// Fecha o interior com uma caixa escura nos limites do mapa.
    ///
    /// Em 2D a area fora do mapa nunca era vista: a camera nao saia da sala. Em primeira
    /// pessoa ela aparece como buracos pretos nas laterais. Esta caixa nao tenta imitar
    /// parede -- so impede que o vazio fique a mostra ate a Fase 2 dar paredes de verdade
    /// a cada interior.
    /// </summary>
    private void EmitInteriorEnclosure(GraphicsDevice device, List<MeshChunk> chunks, int width, int height)
    {
        Texture2D texture = this.EnsureVoidTexture(device);

        const float bottom = -3f;
        const float top = 8f;
        float x0 = -0.5f, z0 = -0.5f;
        float x1 = width + 0.5f, z1 = height + 0.5f;

        var vertices = new List<VertexPositionTexture>(24);

        void Wall(Vector3 a, Vector3 b)
        {
            Vector3 bl = new(a.X, bottom, a.Z);
            Vector3 br = new(b.X, bottom, b.Z);
            Vector3 tl = new(a.X, top, a.Z);
            Vector3 tr = new(b.X, top, b.Z);

            vertices.Add(new VertexPositionTexture(bl, new Vector2(0, 1)));
            vertices.Add(new VertexPositionTexture(tl, new Vector2(0, 0)));
            vertices.Add(new VertexPositionTexture(tr, new Vector2(1, 0)));

            vertices.Add(new VertexPositionTexture(bl, new Vector2(0, 1)));
            vertices.Add(new VertexPositionTexture(tr, new Vector2(1, 0)));
            vertices.Add(new VertexPositionTexture(br, new Vector2(1, 1)));
        }

        Wall(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0));
        Wall(new Vector3(x1, 0, z0), new Vector3(x1, 0, z1));
        Wall(new Vector3(x1, 0, z1), new Vector3(x0, 0, z1));
        Wall(new Vector3(x0, 0, z1), new Vector3(x0, 0, z0));

        var buffer = new VertexBuffer(device, typeof(VertexPositionTexture), vertices.Count, BufferUsage.WriteOnly);
        buffer.SetData(vertices.ToArray());

        chunks.Add(new MeshChunk(
            new[] { new Submesh(texture, buffer, vertices.Count / 3) },
            new BoundingBox(new Vector3(x0, bottom, z0), new Vector3(x1, top, z1))));
    }

    /// <summary>Textura opaca de uma cor so, usada para tapar o vazio.</summary>
    private Texture2D EnsureVoidTexture(GraphicsDevice device)
    {
        if (this.voidTexture is not null && !this.voidTexture.IsDisposed)
            return this.voidTexture;

        this.voidTexture = new Texture2D(device, 1, 1);
        this.voidTexture.SetData(new[] { new Color(26, 20, 28) });
        return this.voidTexture;
    }

    /// <summary>Emite o quad horizontal de um tile, texturizado com a tilesheet real do mapa.</summary>
    private void EmitFloor(Dictionary<Texture2D, List<VertexPositionTexture>> byTexture, Layer? layer, int x, int z, float y)
    {
        Tile? tile = GetTile(layer, x, z);
        if (tile is null)
            return;

        if (!this.TryGetTileUv(tile, out Texture2D? texture, out Vector4 uv))
            return;

        List<VertexPositionTexture> vertices = GetList(byTexture, texture!);

        Vector3 a = new(x, y, z);
        Vector3 b = new(x + 1, y, z);
        Vector3 c = new(x + 1, y, z + 1);
        Vector3 d = new(x, y, z + 1);

        // V cresce para o sul, igual ao eixo Z: a tilesheet nao sai espelhada.
        Vector2 uvA = new(uv.X, uv.Y);
        Vector2 uvB = new(uv.Z, uv.Y);
        Vector2 uvC = new(uv.Z, uv.W);
        Vector2 uvD = new(uv.X, uv.W);

        vertices.Add(new VertexPositionTexture(a, uvA));
        vertices.Add(new VertexPositionTexture(c, uvC));
        vertices.Add(new VertexPositionTexture(b, uvB));

        vertices.Add(new VertexPositionTexture(a, uvA));
        vertices.Add(new VertexPositionTexture(d, uvD));
        vertices.Add(new VertexPositionTexture(c, uvC));
    }

    /// <summary>
    /// Extruda as 4 faces de um tile-parede, omitindo faces internas (vizinho tambem solido),
    /// que e o que mantem a contagem de triangulos sob controle em interiores.
    /// </summary>
    private void EmitWalls(
        Dictionary<Texture2D, List<VertexPositionTexture>> byTexture,
        LocationGeometry geometry,
        Tile tile,
        int x, int z, float bottom, float top)
    {
        if (!this.TryGetTileUv(tile, out Texture2D? texture, out Vector4 uv))
            return;

        List<VertexPositionTexture> vertices = GetList(byTexture, texture!);

        ReadOnlySpan<int> dxs = stackalloc int[] { 0, 1, 0, -1 };
        ReadOnlySpan<int> dzs = stackalloc int[] { -1, 0, 1, 0 };

        for (int i = 0; i < 4; i++)
        {
            if (geometry.WallTopAt(x + dxs[i], z + dzs[i]) >= top)
                continue;

            Vector3 p0, p1;
            switch (i)
            {
                case 0: p0 = new Vector3(x, 0, z); p1 = new Vector3(x + 1, 0, z); break;
                case 1: p0 = new Vector3(x + 1, 0, z); p1 = new Vector3(x + 1, 0, z + 1); break;
                case 2: p0 = new Vector3(x + 1, 0, z + 1); p1 = new Vector3(x, 0, z + 1); break;
                default: p0 = new Vector3(x, 0, z + 1); p1 = new Vector3(x, 0, z); break;
            }

            Vector3 bl = new(p0.X, bottom, p0.Z);
            Vector3 br = new(p1.X, bottom, p1.Z);
            Vector3 tl = new(p0.X, top, p0.Z);
            Vector3 tr = new(p1.X, top, p1.Z);

            Vector2 uvBl = new(uv.X, uv.W);
            Vector2 uvBr = new(uv.Z, uv.W);
            Vector2 uvTl = new(uv.X, uv.Y);
            Vector2 uvTr = new(uv.Z, uv.Y);

            vertices.Add(new VertexPositionTexture(bl, uvBl));
            vertices.Add(new VertexPositionTexture(tl, uvTl));
            vertices.Add(new VertexPositionTexture(tr, uvTr));

            vertices.Add(new VertexPositionTexture(bl, uvBl));
            vertices.Add(new VertexPositionTexture(tr, uvTr));
            vertices.Add(new VertexPositionTexture(br, uvBr));
        }
    }

    private static List<VertexPositionTexture> GetList(Dictionary<Texture2D, List<VertexPositionTexture>> byTexture, Texture2D texture)
    {
        if (!byTexture.TryGetValue(texture, out List<VertexPositionTexture>? list))
            byTexture[texture] = list = new List<VertexPositionTexture>(512);

        return list;
    }

    private static Tile? GetTile(Layer? layer, int x, int z)
    {
        if (layer is null)
            return null;

        var position = new xTile.Dimensions.Location(x, z);
        return layer.IsValidTileLocation(position) ? layer.Tiles[position] : null;
    }

    /// <summary>Resolve a textura e as UVs de um tile a partir da sua tilesheet.</summary>
    private bool TryGetTileUv(Tile tile, out Texture2D? texture, out Vector4 uv)
    {
        uv = Vector4.Zero;
        texture = this.GetTileSheetTexture(tile.TileSheet);
        if (texture is null)
            return false;

        // AnimatedTile expoe o indice do frame corrente; a malha estatica congela o frame
        // atual (TODO Fase 6: animar agua e tochas).
        xTile.Dimensions.Rectangle bounds = tile.TileSheet.GetTileImageBounds(tile.TileIndex);

        const float inset = 0.5f;
        uv = new Vector4(
            (bounds.X + inset) / texture.Width,
            (bounds.Y + inset) / texture.Height,
            (bounds.X + bounds.Width - inset) / texture.Width,
            (bounds.Y + bounds.Height - inset) / texture.Height);

        return true;
    }

    /// <summary>
    /// Carrega a textura de uma tilesheet do mesmo jeito que o display device do jogo faz
    /// (<c>content.Load&lt;Texture2D&gt;(sheet.ImageSource)</c>), com cache e tolerancia a
    /// tilesheets quebradas, que nao devem derrubar a localizacao inteira.
    /// </summary>
    private Texture2D? GetTileSheetTexture(TileSheet sheet)
    {
        string source = sheet.ImageSource;
        if (this.textureCache.TryGetValue(source, out Texture2D? cached))
            return cached;

        Texture2D? texture = null;
        try
        {
            texture = Game1.content.Load<Texture2D>(source);
        }
        catch (Exception ex)
        {
            this.monitor.Log($"Nao foi possivel carregar a tilesheet '{source}': {ex.Message}", LogLevel.Warn);
        }

        this.textureCache[source] = texture;
        return texture;
    }
}

public sealed record Submesh(Texture2D Texture, VertexBuffer Buffer, int TriangleCount);

public sealed record MeshChunk(IReadOnlyList<Submesh> Submeshes, BoundingBox Bounds);

public sealed class LocationMesh
{
    public IReadOnlyList<MeshChunk> Chunks { get; }
    public int ChunkCount => this.Chunks.Count;
    public int SubmeshCount { get; }
    public int TriangleCount { get; }

    public LocationMesh(IReadOnlyList<MeshChunk> chunks)
    {
        this.Chunks = chunks;
        foreach (MeshChunk chunk in chunks)
        {
            this.SubmeshCount += chunk.Submeshes.Count;
            foreach (Submesh submesh in chunk.Submeshes)
                this.TriangleCount += submesh.TriangleCount;
        }
    }

    public void Dispose()
    {
        foreach (MeshChunk chunk in this.Chunks)
        {
            foreach (Submesh submesh in chunk.Submeshes)
                submesh.Buffer.Dispose();
        }
    }
}

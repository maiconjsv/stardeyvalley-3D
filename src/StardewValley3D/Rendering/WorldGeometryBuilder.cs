using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley3D.World;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace StardewValley3D.Rendering;

/// <summary>
/// Converte um mapa xTile em malha 3D estatica: quads de chao a partir das camadas
/// horizontais e paredes extrudadas a partir da colisao/height map.
/// O resultado e cacheado por localizacao e invalidado em warp/mudanca de estacao.
/// </summary>
public sealed class WorldGeometryBuilder
{
    /// <summary>Chunk de 16x16 tiles: unidade de culling e de rebuild parcial.</summary>
    public const int ChunkSize = 16;

    private readonly IMonitor monitor;
    private readonly Dictionary<string, LocationMesh> cache = new();

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
        this.monitor.Log($"Geometria construida para '{key}': {mesh.ChunkCount} chunks, {mesh.TriangleCount} triangulos.", LogLevel.Trace);
        return mesh;
    }

    private LocationMesh Build(GraphicsDevice device, GameLocation location, LocationGeometry geometry)
    {
        Map map = location.Map;
        int width = map.Layers[0].LayerWidth;
        int height = map.Layers[0].LayerHeight;

        var chunks = new List<MeshChunk>();

        for (int cz = 0; cz < height; cz += ChunkSize)
        {
            for (int cx = 0; cx < width; cx += ChunkSize)
            {
                var vertices = new List<VertexPositionTexture>();

                for (int z = cz; z < System.Math.Min(cz + ChunkSize, height); z++)
                {
                    for (int x = cx; x < System.Math.Min(cx + ChunkSize, width); x++)
                    {
                        float floor = geometry.FloorHeightAt(x, z);
                        this.EmitFloor(map, vertices, x, z, floor);

                        float wallTop = geometry.WallTopAt(x, z);
                        if (wallTop > floor)
                            this.EmitWalls(geometry, vertices, x, z, floor, wallTop);
                    }
                }

                if (vertices.Count == 0)
                    continue;

                var buffer = new VertexBuffer(device, typeof(VertexPositionTexture), vertices.Count, BufferUsage.WriteOnly);
                buffer.SetData(vertices.ToArray());

                chunks.Add(new MeshChunk(
                    buffer,
                    vertices.Count / 3,
                    new BoundingBox(
                        new Vector3(cx, -4f, cz),
                        new Vector3(cx + ChunkSize, 12f, cz + ChunkSize))));
            }
        }

        return new LocationMesh(chunks);
    }

    /// <summary>Emite o quad horizontal de um tile. UVs vem da tilesheet real do mapa.</summary>
    private void EmitFloor(Map map, List<VertexPositionTexture> vertices, int x, int z, float y)
    {
        // TODO Fase 1: resolver a tilesheet/tile index reais e agrupar por textura.
        // Hoje emitimos a geometria com UV de tile cheio para validar camera e layout.
        Vector2 uv0 = Vector2.Zero;
        Vector2 uv1 = Vector2.One;

        Vector3 a = new(x, y, z);
        Vector3 b = new(x + 1, y, z);
        Vector3 c = new(x + 1, y, z + 1);
        Vector3 d = new(x, y, z + 1);

        vertices.Add(new VertexPositionTexture(a, uv0));
        vertices.Add(new VertexPositionTexture(b, new Vector2(uv1.X, uv0.Y)));
        vertices.Add(new VertexPositionTexture(c, uv1));

        vertices.Add(new VertexPositionTexture(a, uv0));
        vertices.Add(new VertexPositionTexture(c, uv1));
        vertices.Add(new VertexPositionTexture(d, new Vector2(uv0.X, uv1.Y)));
    }

    /// <summary>
    /// Extruda as 4 faces de um tile-parede, omitindo faces internas (vizinho tambem solido),
    /// que e o que mantem a contagem de triangulos sob controle em interiores.
    /// </summary>
    private void EmitWalls(LocationGeometry geometry, List<VertexPositionTexture> vertices, int x, int z, float bottom, float top)
    {
        Span<(int dx, int dz)> neighbors = stackalloc (int, int)[] { (0, -1), (1, 0), (0, 1), (-1, 0) };

        for (int i = 0; i < neighbors.Length; i++)
        {
            (int dx, int dz) = neighbors[i];
            if (geometry.WallTopAt(x + dx, z + dz) >= top)
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

            vertices.Add(new VertexPositionTexture(bl, new Vector2(0, 1)));
            vertices.Add(new VertexPositionTexture(tl, new Vector2(0, 0)));
            vertices.Add(new VertexPositionTexture(tr, new Vector2(1, 0)));

            vertices.Add(new VertexPositionTexture(bl, new Vector2(0, 1)));
            vertices.Add(new VertexPositionTexture(tr, new Vector2(1, 0)));
            vertices.Add(new VertexPositionTexture(br, new Vector2(1, 1)));
        }
    }
}

public sealed record MeshChunk(VertexBuffer Buffer, int TriangleCount, BoundingBox Bounds);

public sealed class LocationMesh
{
    public IReadOnlyList<MeshChunk> Chunks { get; }
    public int ChunkCount => this.Chunks.Count;
    public int TriangleCount { get; }

    public LocationMesh(IReadOnlyList<MeshChunk> chunks)
    {
        this.Chunks = chunks;
        foreach (MeshChunk chunk in chunks)
            this.TriangleCount += chunk.TriangleCount;
    }

    public void Dispose()
    {
        foreach (MeshChunk chunk in this.Chunks)
            chunk.Buffer.Dispose();
    }
}

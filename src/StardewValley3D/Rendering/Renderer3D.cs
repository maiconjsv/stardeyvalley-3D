using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley3D.World;

namespace StardewValley3D.Rendering;

/// <summary>
/// Orquestra o frame 3D: estado do device, malha estatica da localizacao, billboards e
/// iluminacao. Nao sabe nada sobre SMAPI/Harmony -- recebe o comando de desenhar.
/// </summary>
public sealed class Renderer3D
{
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly WorldGeometryBuilder geometryBuilder;
    private readonly BillboardBatch billboards = new();
    private readonly SpriteCollector collector = new();
    private readonly Dictionary<string, LocationGeometry> geometryByLocation = new();

    private GraphicsDevice device = null!;
    private BasicEffect effect = null!;
    private IModHelper helper = null!;

    public Camera3D Camera { get; }
    public int LastTriangleCount { get; private set; }
    public int LastBillboardCount { get; private set; }

    public Renderer3D(IMonitor monitor, ModConfig config)
    {
        this.monitor = monitor;
        this.config = config;
        this.Camera = new Camera3D(config);
        this.geometryBuilder = new WorldGeometryBuilder(monitor);
    }

    public void Initialize(GraphicsDevice device, IModHelper helper)
    {
        this.device = device;
        this.helper = helper;
        this.effect = new BasicEffect(device)
        {
            TextureEnabled = true,
            VertexColorEnabled = false,
            LightingEnabled = false,
            FogEnabled = this.config.DistanceFog,
            FogStart = 12f,
            FogEnd = 44f,
            FogColor = Color.Black.ToVector3()
        };
    }

    public void InvalidateAll()
    {
        this.geometryBuilder.InvalidateAll();
        this.geometryByLocation.Clear();
    }

    public void OnWarped(GameLocation? location)
    {
        if (location is not null)
            this.geometryBuilder.Invalidate(location.NameOrUniqueName);
    }

    /// <summary>Consultado pelo fallback: a localizacao tem geometria utilizavel?</summary>
    public bool HasGeometryFor(GameLocation location)
        => location.Map?.Layers.Count > 0;

    public void Update(GameTime time)
    {
        float aspect = this.device.Viewport.AspectRatio;
        this.Camera.Update((float)time.ElapsedGameTime.TotalSeconds, aspect);
    }

    /// <summary>Desenha o mundo em 3D. Chamado pelo prefix de Game1._draw.</summary>
    public void DrawWorld()
    {
        GameLocation? location = Game1.currentLocation;
        if (location?.Map is null)
            return;

        LocationGeometry geometry = this.GetGeometry(location);
        LocationMesh mesh = this.geometryBuilder.GetOrBuild(this.device, location, geometry);

        this.device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, this.AmbientColor(), 1f, 0);
        this.device.DepthStencilState = DepthStencilState.Default;
        this.device.RasterizerState = RasterizerState.CullCounterClockwise;
        this.device.SamplerStates[0] = SamplerState.PointClamp;
        this.device.BlendState = BlendState.Opaque;

        this.effect.World = Matrix.Identity;
        this.effect.View = this.Camera.View;
        this.effect.Projection = this.Camera.Projection;
        this.effect.FogEnabled = this.config.DistanceFog;
        this.effect.DiffuseColor = this.AmbientColor().ToVector3();

        this.DrawStaticMesh(mesh);
        this.DrawBillboards(location);
    }

    private void DrawStaticMesh(LocationMesh mesh)
    {
        this.LastTriangleCount = 0;
        this.effect.VertexColorEnabled = false;

        foreach (MeshChunk chunk in mesh.Chunks)
        {
            if (this.Camera.Frustum.Contains(chunk.Bounds) == ContainmentType.Disjoint)
                continue;

            this.device.SetVertexBuffer(chunk.Buffer);
            foreach (EffectPass pass in this.effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                this.device.DrawPrimitives(PrimitiveType.TriangleList, 0, chunk.TriangleCount);
            }

            this.LastTriangleCount += chunk.TriangleCount;
        }

        this.device.SetVertexBuffer(null);
    }

    private void DrawBillboards(GameLocation location)
    {
        this.billboards.Begin();
        this.collector.Collect(location, this.Camera, this.config, this.billboards);
        this.billboards.End(this.device, this.effect, this.Camera);
        this.LastBillboardCount = this.billboards.LastQuadCount;
    }

    /// <summary>
    /// Luz ambiente derivada do proprio estado do jogo, para que dia/noite/estacao/clima
    /// continuem valendo sem duplicarmos regra nenhuma.
    /// </summary>
    private Color AmbientColor()
    {
        Color ambient = Game1.ambientLight;
        return new Color(
            (byte)(255 - ambient.R),
            (byte)(255 - ambient.G),
            (byte)(255 - ambient.B));
    }

    private LocationGeometry GetGeometry(GameLocation location)
    {
        string key = location.NameOrUniqueName;
        if (this.geometryByLocation.TryGetValue(key, out LocationGeometry? cached))
            return cached;

        var geometry = new LocationGeometry(location);

        // Dado autoral opcional: assets/geometry/<Location>.json (ver Fase 2).
        GeometryFile? file = this.helper.Data.ReadJsonFile<GeometryFile>($"assets/geometry/{key}.json");
        if (file is not null)
        {
            geometry.Sectors.AddRange(file.Sectors);
            this.monitor.Log($"Geometria autoral carregada para '{key}' ({file.Sectors.Count} setores).", LogLevel.Trace);
        }

        geometry.Bake();
        this.geometryByLocation[key] = geometry;
        return geometry;
    }
}

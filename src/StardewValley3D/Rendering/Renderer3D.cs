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
    /// <summary>Resumo da malha da localizacao atual, para diagnostico na tela.</summary>
    public string LastMeshSummary { get; private set; } = "-";

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

        Color horizon = this.HorizonColor();

        this.device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, horizon, 1f, 0);
        this.device.DepthStencilState = DepthStencilState.Default;

        // CullNone na Fase 1: a orientacao das faces ainda nao esta validada, e uma face
        // invertida custa a tela inteira preta. Ligar o culling e tarefa da Fase 8.
        this.device.RasterizerState = RasterizerState.CullNone;
        this.device.SamplerStates[0] = SamplerState.PointClamp;
        this.device.BlendState = BlendState.Opaque;

        this.effect.World = Matrix.Identity;
        this.effect.View = this.Camera.View;
        this.effect.Projection = this.Camera.Projection;
        this.effect.TextureEnabled = true;
        this.effect.LightingEnabled = false;

        // A cor do mundo vem da textura; tingir aqui escureceria tudo.
        this.effect.DiffuseColor = Vector3.One;
        this.effect.Alpha = 1f;

        // O fog recebe a cor do horizonte para que a distancia dissolva no ceu, em vez de
        // virar uma parede preta.
        this.effect.FogEnabled = this.config.DistanceFog;
        this.effect.FogColor = horizon.ToVector3();

        this.LastMeshSummary = $"chunks={mesh.ChunkCount} submalhas={mesh.SubmeshCount} tris={mesh.TriangleCount}";

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

            foreach (Submesh submesh in chunk.Submeshes)
            {
                this.effect.Texture = submesh.Texture;
                this.device.SetVertexBuffer(submesh.Buffer);

                foreach (EffectPass pass in this.effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    this.device.DrawPrimitives(PrimitiveType.TriangleList, 0, submesh.TriangleCount);
                }

                this.LastTriangleCount += submesh.TriangleCount;
            }
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
    /// Cor do ceu/horizonte, usada no clear e no fog. Derivada da hora do jogo para que
    /// dia e noite continuem valendo sem duplicarmos regra nenhuma.
    /// TODO Fase 6: substituir por iluminacao real a partir de Game1.outdoorLight e das
    /// luzes pontuais em Game1.currentLightSources.
    /// </summary>
    private Color HorizonColor()
    {
        if (Game1.currentLocation?.IsOutdoors != true)
            return new Color(12, 10, 16);

        // timeOfDay vai de 600 (6h) a 2600 (2h da madrugada seguinte).
        float t = MathHelper.Clamp((Game1.timeOfDay - 600) / 1400f, 0f, 1f);
        return Color.Lerp(new Color(132, 186, 227), new Color(18, 20, 48), t);
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

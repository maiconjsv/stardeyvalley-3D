using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley3D.World;

namespace StardewValley3D.Rendering;

/// <summary>
/// Orquestra o frame 3D: estado do device, malha estatica da localizacao, sprites
/// volumetricos e iluminacao. Nao sabe nada sobre SMAPI/Harmony -- recebe o comando de
/// desenhar.
/// </summary>
public sealed class Renderer3D
{
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly WorldGeometryBuilder geometryBuilder;
    private readonly SpriteCollector collector = new();
    private readonly SpriteMeshBatch sprites = new();
    private readonly Dictionary<string, LocationGeometry> geometryByLocation = new();

    private GraphicsDevice device = null!;
    private BasicEffect effect = null!;
    private AlphaTestEffect worldEffect = null!;
    private IModHelper helper = null!;
    private SpriteMeshBuilder spriteMeshes = null!;

    public Camera3D Camera { get; }
    public int LastTriangleCount { get; private set; }
    public int LastSpriteCount { get; private set; }
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
        this.spriteMeshes = new SpriteMeshBuilder(device, this.monitor);
        this.sprites.Initialize(device);

        // A malha do mundo precisa de alpha test: as camadas Buildings e Front sao quase
        // todas transparentes, e sem recorte cada pixel vazio vira preto opaco -- e ainda
        // escreve no depth buffer, escondendo o que estiver atras.
        this.worldEffect = new AlphaTestEffect(device)
        {
            AlphaFunction = CompareFunction.Greater,
            ReferenceAlpha = 128,
            VertexColorEnabled = false
        };

        // BasicEffect fica so para as linhas do editor, que nao tem textura.
        this.effect = new BasicEffect(device)
        {
            TextureEnabled = false,
            VertexColorEnabled = true,
            LightingEnabled = false
        };
    }

    public void InvalidateAll()
    {
        this.geometryBuilder.InvalidateAll();
        this.geometryByLocation.Clear();
        this.spriteMeshes?.Clear();
    }

    public void OnWarped(GameLocation? location)
    {
        if (location is not null)
            this.geometryBuilder.Invalidate(location.NameOrUniqueName);
    }

    /// <summary>Forca a reconstrucao da malha apos uma edicao no editor de cenario.</summary>
    public void InvalidateMesh(GameLocation location)
        => this.geometryBuilder.Invalidate(location.NameOrUniqueName);

    /// <summary>Descarta a geometria da localizacao para recarregar o JSON do disco.</summary>
    public void ReloadGeometry(GameLocation location)
    {
        this.geometryByLocation.Remove(location.NameOrUniqueName);
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

        this.worldEffect.World = Matrix.Identity;
        this.worldEffect.View = this.Camera.View;
        this.worldEffect.Projection = this.Camera.Projection;

        // A cor do mundo vem da textura; tingir aqui escureceria tudo.
        this.worldEffect.DiffuseColor = Vector3.One;
        this.worldEffect.Alpha = 1f;

        this.ApplyFog(this.worldEffect.FogEnabled, horizon);
        this.worldEffect.FogEnabled = this.config.DistanceFog;
        this.worldEffect.FogColor = horizon.ToVector3();
        this.worldEffect.FogStart = this.FogStart;
        this.worldEffect.FogEnd = this.FogEnd;

        this.effect.View = this.Camera.View;
        this.effect.Projection = this.Camera.Projection;

        this.LastMeshSummary = $"chunks={mesh.ChunkCount} submalhas={mesh.SubmeshCount} tris={mesh.TriangleCount}";

        this.DrawStaticMesh(mesh);
        this.DrawSprites(location);
    }

    private void DrawStaticMesh(LocationMesh mesh)
    {
        this.LastTriangleCount = 0;

        foreach (MeshChunk chunk in mesh.Chunks)
        {
            if (this.Camera.Frustum.Contains(chunk.Bounds) == ContainmentType.Disjoint)
                continue;

            foreach (Submesh submesh in chunk.Submeshes)
            {
                this.worldEffect.Texture = submesh.Texture;
                this.device.SetVertexBuffer(submesh.Buffer);

                foreach (EffectPass pass in this.worldEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    this.device.DrawPrimitives(PrimitiveType.TriangleList, 0, submesh.TriangleCount);
                }

                this.LastTriangleCount += submesh.TriangleCount;
            }
        }

        this.device.SetVertexBuffer(null);
    }

    private void DrawSprites(GameLocation location)
    {
        this.sprites.Begin();
        this.collector.Collect(location, this.Camera, this.config, this.spriteMeshes, this.sprites);
        this.sprites.End(this.device, this.Camera, Color.White, this.config.DistanceFog, this.HorizonColor(), this.FogStart, this.FogEnd);

        this.LastSpriteCount = this.sprites.LastInstanceCount;
        this.LastTriangleCount += this.sprites.LastTriangleCount;
    }

    /// <summary>
    /// Desenha o contorno do tile mirado. E o feedback que torna o editor de cenario
    /// utilizavel: sem ele, nao da para saber qual tile vai ser alterado.
    /// </summary>
    public void DrawTileHighlight(Vector2 tile, int brushSize, Color color)
    {
        int radius = brushSize / 2;
        float x0 = tile.X - radius;
        float z0 = tile.Y - radius;
        float x1 = tile.X + radius + 1;
        float z1 = tile.Y + radius + 1;
        float y = this.GetGeometry(Game1.currentLocation).FloorHeightAt((int)tile.X, (int)tile.Y) + 0.02f;

        var corners = new[]
        {
            new VertexPositionColor(new Vector3(x0, y, z0), color),
            new VertexPositionColor(new Vector3(x1, y, z0), color),
            new VertexPositionColor(new Vector3(x1, y, z1), color),
            new VertexPositionColor(new Vector3(x0, y, z1), color),
            new VertexPositionColor(new Vector3(x0, y, z0), color)
        };

        this.device.DepthStencilState = DepthStencilState.DepthRead;

        foreach (EffectPass pass in this.effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            this.device.DrawUserPrimitives(PrimitiveType.LineStrip, corners, 0, 4);
        }
    }

    /// <summary>
    /// Alcance do fog. Interiores recebem um fog curto, que e o que da a atmosfera fechada;
    /// exteriores recebem um fog longo, cuja funcao principal e dissolver a borda do mapa no
    /// horizonte em vez de deixar o vazio aparecer.
    /// </summary>
    private float FogStart => Game1.currentLocation?.IsOutdoors == true ? 26f : 6f;

    private float FogEnd => Game1.currentLocation?.IsOutdoors == true ? 72f : 26f;

    private void ApplyFog(bool _, Color __)
    {
        // Ponto de extensao da Fase 6 (clima e hora alteram o alcance do fog).
    }

    /// <summary>
    /// Cor do ceu/horizonte, usada no clear e no fog. Derivada da hora do jogo para que
    /// dia e noite continuem valendo sem duplicarmos regra nenhuma.
    /// TODO Fase 6: iluminacao real a partir de Game1.outdoorLight e das luzes pontuais.
    /// </summary>
    private Color HorizonColor()
    {
        if (Game1.currentLocation?.IsOutdoors != true)
            return new Color(12, 10, 16);

        // timeOfDay vai de 600 (6h) a 2600 (2h da madrugada seguinte).
        float t = MathHelper.Clamp((Game1.timeOfDay - 600) / 1400f, 0f, 1f);
        return Color.Lerp(new Color(132, 186, 227), new Color(18, 20, 48), t);
    }

    public LocationGeometry GetGeometry(GameLocation location)
    {
        string key = location.NameOrUniqueName;
        if (this.geometryByLocation.TryGetValue(key, out LocationGeometry? cached))
            return cached;

        var geometry = new LocationGeometry(location);

        // Dado autoral opcional, produzido pelo editor de cenario (ver Fase 2).
        GeometryFile? file = this.helper.Data.ReadJsonFile<GeometryFile>($"assets/geometry/{key}.json");
        if (file is not null)
        {
            geometry.Sectors.AddRange(file.Sectors);
            this.monitor.Log($"Geometria autoral carregada para '{key}' ({file.Sectors.Count} setores).", LogLevel.Debug);
        }

        geometry.Bake();
        this.geometryByLocation[key] = geometry;
        return geometry;
    }
}

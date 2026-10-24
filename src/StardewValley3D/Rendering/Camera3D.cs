using System;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewValley3D.Rendering;

/// <summary>
/// Camera orbital em terceira pessoa. Convencao de unidades do mod:
/// 1 tile do Stardew (64 px) = 1.0 unidade de mundo. Y e a altura.
/// </summary>
public sealed class Camera3D
{
    public const float PixelsPerTile = 64f;

    private readonly ModConfig config;

    /// <summary>Rotacao horizontal, em radianos. 0 = olhando para +Z (sul, como o jogo 2D).</summary>
    public float Yaw { get; set; }
    /// <summary>Rotacao vertical, em radianos. Negativo olha para baixo.</summary>
    public float Pitch { get; set; } = -0.35f;
    public float Distance { get; private set; }

    public Vector3 Position { get; private set; }
    public Vector3 Target { get; private set; }
    public Matrix View { get; private set; } = Matrix.Identity;
    public Matrix Projection { get; private set; } = Matrix.Identity;
    public BoundingFrustum Frustum { get; private set; } = new(Matrix.Identity);

    private Vector3 smoothedTarget;
    private bool initialized;

    public Camera3D(ModConfig config)
    {
        this.config = config;
        this.Distance = config.CameraDistance;
    }

    /// <summary>Distancia zero significa primeira pessoa: a camera fica nos olhos.</summary>
    public bool IsFirstPerson => this.Distance <= 0.01f;

    /// <summary>Converte posicao em pixels do jogo para unidades de mundo (sem altura).</summary>
    public static Vector3 WorldFromPixels(Vector2 pixels, float height = 0f)
        => new(pixels.X / PixelsPerTile, height, pixels.Y / PixelsPerTile);

    /// <summary>
    /// Zero e primeira pessoa; acima de 1.5 e terceira pessoa. A faixa entre os dois e
    /// pulada porque a camera ficaria dentro do proprio corpo do jogador.
    /// </summary>
    public void Zoom(float delta)
    {
        float next = this.Distance + delta;

        if (this.IsFirstPerson)
            next = delta > 0f ? 1.5f : 0f;
        else if (next < 1.5f)
            next = 0f;

        this.Distance = MathHelper.Clamp(next, 0f, 14f);
    }

    public void Update(float elapsedSeconds, float aspectRatio)
    {
        Vector2 anchorPixels = Game1.player?.getStandingPosition() ?? Vector2.Zero;
        Vector3 desiredTarget = WorldFromPixels(anchorPixels, this.config.EyeHeight);

        if (!this.initialized)
        {
            this.smoothedTarget = desiredTarget;
            this.initialized = true;
        }
        else
        {
            // Suavizacao independente de framerate; 0 = camera rigida.
            float smoothing = MathHelper.Clamp(this.config.CameraSmoothing, 0f, 0.95f);
            float lerp = smoothing <= 0f
                ? 1f
                : 1f - MathF.Pow(1f - 0.92f, elapsedSeconds / Math.Max(smoothing, 1e-3f));
            this.smoothedTarget = Vector3.Lerp(this.smoothedTarget, desiredTarget, MathHelper.Clamp(lerp, 0f, 1f));
        }

        this.Pitch = MathHelper.Clamp(this.Pitch, -1.45f, 1.45f);

        if (this.IsFirstPerson)
        {
            this.Position = this.smoothedTarget;
            this.Target = this.Position + this.Forward;
        }
        else
        {
            this.Target = this.smoothedTarget;
            this.Position = this.Target - this.Forward * this.Distance;

            // TODO Fase 2: colisao da camera com paredes (raycast Target -> Position).
        }

        this.View = Matrix.CreateLookAt(this.Position, this.Target, Vector3.Up);
        this.Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(this.config.FieldOfView), aspectRatio, 0.05f, 220f);
        this.Frustum = new BoundingFrustum(this.View * this.Projection);
    }

    /// <summary>Vetor para onde a camera olha.</summary>
    public Vector3 Forward => new(
        MathF.Sin(this.Yaw) * MathF.Cos(this.Pitch),
        MathF.Sin(this.Pitch),
        MathF.Cos(this.Yaw) * MathF.Cos(this.Pitch));

    /// <summary>Forward projetado no plano do chao: base do movimento relativo a camera.</summary>
    public Vector2 FlatForward
    {
        get
        {
            Vector2 v = new(MathF.Sin(this.Yaw), MathF.Cos(this.Yaw));
            v.Normalize();
            return v;
        }
    }

    public Vector2 FlatRight
    {
        get
        {
            Vector2 f = this.FlatForward;
            return new Vector2(f.Y, -f.X);
        }
    }

    /// <summary>
    /// Raycast do centro da tela (crosshair) contra o plano do chao na altura <paramref name="planeY"/>.
    /// Base da mira de todas as ferramentas (ver Fase 4).
    /// </summary>
    public bool RaycastGroundPlane(float planeY, out Vector2 tile)
    {
        tile = Vector2.Zero;
        Vector3 dir = this.Forward;

        // Raio paralelo ao plano: sem interseccao util.
        if (MathF.Abs(dir.Y) < 1e-5f)
            return false;

        float t = (planeY - this.Position.Y) / dir.Y;

        // Interseccao atras da camera.
        if (t <= 0f)
            return false;

        Vector3 hit = this.Position + dir * t;
        tile = new Vector2(MathF.Floor(hit.X), MathF.Floor(hit.Z));
        return true;
    }

    /// <summary>
    /// Igual ao <see cref="RaycastGroundPlane"/>, mas devolve tambem o ponto exato em pixels
    /// de mundo. O jogo mira em pixels, nao em tiles: arredondar aqui perderia precisao em
    /// ferramentas de area e na mira de armas.
    /// </summary>
    public bool RaycastGroundPixels(float planeY, out Vector2 tile, out Vector2 pixels)
    {
        pixels = Vector2.Zero;
        Vector3 dir = this.Forward;

        if (MathF.Abs(dir.Y) < 1e-5f)
        {
            tile = Vector2.Zero;
            return false;
        }

        float t = (planeY - this.Position.Y) / dir.Y;
        if (t <= 0f)
        {
            tile = Vector2.Zero;
            return false;
        }

        Vector3 hit = this.Position + dir * t;
        tile = new Vector2(MathF.Floor(hit.X), MathF.Floor(hit.Z));
        pixels = new Vector2(hit.X * PixelsPerTile, hit.Z * PixelsPerTile);
        return true;
    }
}

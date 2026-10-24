using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley3D.Rendering;

namespace StardewValley3D.Input;

/// <summary>
/// Traduz input de shooter em terceira pessoa para o que o Farmer entende.
///
/// Duas conversoes importam:
///   1. WASD relativo a camera -> Farmer.setMoving() com as flags de direcao do jogo
///      (o Stardew aceita diagonais, entao nao perdemos resolucao de movimento);
///   2. crosshair -> tile alvo, publicado em Game1.currentCursorTile pelo CursorPatches.
///      Como TODA ferramenta do Stardew age no tile sob o cursor, farming, mineracao e
///      combate passam a funcionar em 3D sem tocar na logica de nenhuma delas.
/// </summary>
public sealed class ThirdPersonInput
{
    private readonly IModHelper helper;
    private readonly ModConfig config;
    private readonly Camera3D camera;

    private bool active;
    private int lastScroll;
    private Point windowCenter;

    /// <summary>Tile sob a crosshair neste tick. Lido pelos patches de cursor.</summary>
    public Vector2 AimTile { get; private set; }
    public bool HasAim { get; private set; }

    public ThirdPersonInput(IModHelper helper, ModConfig config, Camera3D camera)
    {
        this.helper = helper;
        this.config = config;
        this.camera = camera;
    }

    public void SetActive(bool value)
    {
        this.active = value;
        Game1.game1.IsMouseVisible = !value;

        if (value)
            this.CenterMouse();
    }

    public void Update()
    {
        if (!this.active)
            return;

        this.UpdateLook();
        this.UpdateMovement();
        this.UpdateAim();
    }

    private void UpdateLook()
    {
        MouseState mouse = Mouse.GetState();
        this.windowCenter = new Point(
            Game1.graphics.GraphicsDevice.Viewport.Width / 2,
            Game1.graphics.GraphicsDevice.Viewport.Height / 2);

        // Mouse travado no centro: delta e o movimento desde o ultimo tick.
        float dx = mouse.X - this.windowCenter.X;
        float dy = mouse.Y - this.windowCenter.Y;

        float sensitivity = this.config.MouseSensitivity * 0.01f;
        this.camera.Yaw -= dx * sensitivity;
        this.camera.Pitch += (this.config.InvertPitch ? dy : -dy) * sensitivity;

        int scrollDelta = mouse.ScrollWheelValue - this.lastScroll;
        this.lastScroll = mouse.ScrollWheelValue;
        if (scrollDelta != 0)
            this.camera.Zoom(-scrollDelta * 0.004f);

        if (Game1.activeClickableMenu is null)
            this.CenterMouse();
    }

    private void CenterMouse()
    {
        if (this.windowCenter == Point.Zero)
            return;

        Mouse.SetPosition(this.windowCenter.X, this.windowCenter.Y);
    }

    /// <summary>Monta o vetor de movimento no espaco da camera e devolve ao Farmer.</summary>
    private void UpdateMovement()
    {
        Farmer? player = Game1.player;
        if (player is null || Game1.activeClickableMenu is not null || Game1.dialogueUp)
            return;

        Vector2 input = Vector2.Zero;
        if (this.helper.Input.IsDown(SButton.W)) input.Y += 1f;
        if (this.helper.Input.IsDown(SButton.S)) input.Y -= 1f;
        if (this.helper.Input.IsDown(SButton.D)) input.X += 1f;
        if (this.helper.Input.IsDown(SButton.A)) input.X -= 1f;

        if (input == Vector2.Zero)
        {
            player.Halt();
            return;
        }

        Vector2 world = this.camera.FlatForward * input.Y + this.camera.FlatRight * input.X;
        world.Normalize();

        // O eixo Z do mundo cresce para o sul, igual ao Y em pixels do jogo.
        const float threshold = 0.38f;
        player.setMoving(0);
        if (world.Y < -threshold) player.setMoving(Farmer.up);
        if (world.Y > threshold) player.setMoving(Farmer.down);
        if (world.X > threshold) player.setMoving(Farmer.right);
        if (world.X < -threshold) player.setMoving(Farmer.left);

        // Com mira ativa o personagem encara a camera; sem ela, encara o movimento.
        if (this.HasAim)
            player.faceDirection(this.DirectionFromVector(this.camera.FlatForward));
    }

    private void UpdateAim()
    {
        this.HasAim = this.camera.RaycastGroundPlane(0f, out Vector2 tile);
        this.AimTile = tile;
    }

    private int DirectionFromVector(Vector2 v)
    {
        if (MathF.Abs(v.X) > MathF.Abs(v.Y))
            return v.X > 0 ? Game1.right : Game1.left;

        return v.Y > 0 ? Game1.down : Game1.up;
    }
}

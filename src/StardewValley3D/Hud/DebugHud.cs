using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley3D.Input;

namespace StardewValley3D.Hud;

/// <summary>Overlay de diagnostico: F10 alterna.</summary>
public sealed class DebugHud
{
    private readonly ModConfig config;

    public bool Visible { get; set; }

    public DebugHud(ModConfig config) => this.config = config;

    public void Draw(SpriteBatch batch, ModEntry mod)
    {
        this.DrawCrosshair(batch);

        if (!this.Visible)
            return;

        var camera = mod.Renderer.Camera;
        string[] lines =
        {
            $"SV3D  loc={Game1.currentLocation?.NameOrUniqueName ?? "-"}",
            $"cam   pos={camera.Position.X:F1},{camera.Position.Y:F1},{camera.Position.Z:F1}  yaw={MathHelper.ToDegrees(camera.Yaw):F0}  pitch={MathHelper.ToDegrees(camera.Pitch):F0}  dist={camera.Distance:F1}",
            $"aim   tile={mod.Input.AimTile.X:F0},{mod.Input.AimTile.Y:F0}  valid={mod.Input.HasAim}",
            $"draw  tris={mod.Renderer.LastTriangleCount}  billboards={mod.Renderer.LastBillboardCount}"
        };

        for (int i = 0; i < lines.Length; i++)
        {
            Vector2 at = new(16f, 16f + i * 24f);
            batch.DrawString(Game1.smallFont, lines[i], at + new Vector2(2f, 2f), Color.Black * 0.6f);
            batch.DrawString(Game1.smallFont, lines[i], at, Color.White);
        }
    }

    private void DrawCrosshair(SpriteBatch batch)
    {
        Viewport viewport = Game1.graphics.GraphicsDevice.Viewport;
        var center = new Point(viewport.Width / 2, viewport.Height / 2);

        const int arm = 7;
        const int thickness = 2;
        Color color = Color.White * 0.8f;

        batch.Draw(Game1.staminaRect, new Rectangle(center.X - arm, center.Y - thickness / 2, arm * 2, thickness), color);
        batch.Draw(Game1.staminaRect, new Rectangle(center.X - thickness / 2, center.Y - arm, thickness, arm * 2), color);
    }
}

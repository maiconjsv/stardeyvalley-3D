using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace StardewValley3D.Patches;

/// <summary>
/// Desvia o desenho do mundo para o renderizador 3D.
///
/// Decisao de design: NAO reimplementamos a HUD nem os menus. O prefix desenha a cena 3D e
/// em seguida chama de volta os metodos de UI do proprio jogo, para que tudo (HUD, menus,
/// mensagens) continue identico ao vanilla e nao quebre a cada atualizacao do Stardew.
/// </summary>
internal static class DrawPatches
{
    private static IMonitor monitor = null!;
    private static IReflectedMethod? drawHud;

    public static void Apply(Harmony harmony, IMonitor monitorInstance)
    {
        monitor = monitorInstance;

        try
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(Game1), "_draw", new[] { typeof(GameTime), typeof(RenderTarget2D) }),
                prefix: new HarmonyMethod(typeof(DrawPatches), nameof(Draw_Prefix)));
        }
        catch (Exception ex)
        {
            monitor.Log($"Falha ao aplicar o patch de desenho; o modo 3D ficara indisponivel.\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Retornar false suprime o desenho vanilla do frame inteiro.</summary>
    private static bool Draw_Prefix(GameTime gameTime, RenderTarget2D target_screen)
    {
        ModEntry mod = ModEntry.Instance;
        if (!mod.ShouldRender3D())
            return true;

        try
        {
            GraphicsDevice device = Game1.graphics.GraphicsDevice;
            device.SetRenderTarget(target_screen);

            mod.Renderer.DrawWorld();

            if (mod.Editor.Active && mod.Input.HasAim)
                mod.Renderer.DrawTileHighlight(mod.Input.AimTile, mod.Editor.BrushSize, Color.Lime);

            // UI por cima, em 2D, usando o pipeline do proprio jogo.
            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
            DrawVanillaUi(mod);
            mod.Debug.Draw(Game1.spriteBatch, mod);
            Game1.spriteBatch.End();

            return false;
        }
        catch (Exception ex)
        {
            monitor.Log($"Erro no frame 3D; voltando ao render 2D.\n{ex}", LogLevel.Error);
            return true;
        }
    }

    private static void DrawVanillaUi(ModEntry mod)
    {
        // A HUD do jogo e um metodo privado de Game1. Chamar o original em vez de
        // reimplementa-lo mantem relogio, energia, dinheiro e barra de ferramentas exatos,
        // e evita ter de reescrever tudo a cada atualizacao do jogo.
        try
        {
            drawHud ??= mod.Helper.Reflection.GetMethod(Game1.game1, "drawHUD");
            drawHud.Invoke();
        }
        catch (Exception ex)
        {
            monitor.LogOnce($"Nao foi possivel desenhar a HUD vanilla: {ex.Message}", LogLevel.Warn);
        }

        foreach (IClickableMenu menu in Game1.onScreenMenus)
        {
            try
            {
                menu.draw(Game1.spriteBatch);
            }
            catch (Exception)
            {
                // Um menu de HUD quebrado nao deve derrubar o frame.
            }
        }

        Game1.activeClickableMenu?.draw(Game1.spriteBatch);

        if (Game1.activeClickableMenu is not null)
            Game1.game1.drawMouseCursor();
    }
}

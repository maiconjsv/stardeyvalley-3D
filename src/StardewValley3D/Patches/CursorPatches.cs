using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace StardewValley3D.Patches;

/// <summary>
/// Faz a crosshair ocupar o lugar do mouse nas coordenadas DE MUNDO, mantendo o mouse real
/// nas coordenadas DE UI.
///
/// Por que aqui: tanto <c>Game1.pressUseToolButton</c> quanto <c>Game1.pressActionButton</c>
/// derivam o alvo de <c>getOldMouseX() + Game1.viewport.X</c> (confirmado no IL do jogo 1.6).
/// Sobrescrevendo esses acessores, herdamos de graca arar, plantar, regar, colher, minerar,
/// cortar arvore, posicionar objeto, interagir com NPC e abrir porta -- sem tocar na logica
/// de nenhuma dessas mecanicas.
///
/// O parametro <c>ui_scale</c> dos acessores separa os dois mundos com precisao: <c>true</c>
/// e usado por menus e HUD, <c>false</c> pelo mundo. So sobrescrevemos o segundo, e por isso
/// os menus continuam respondendo ao mouse de verdade.
/// </summary>
internal static class CursorPatches
{
    private static IMonitor monitor = null!;

    public static void Apply(Harmony harmony, IMonitor monitorInstance)
    {
        monitor = monitorInstance;

        // As sobrecargas sem parametro apenas delegam para estas, entao um patch cobre as duas.
        (string name, string postfix)[] targets =
        {
            ("getMouseX", nameof(MouseX_Postfix)),
            ("getOldMouseX", nameof(MouseX_Postfix)),
            ("getMouseY", nameof(MouseY_Postfix)),
            ("getOldMouseY", nameof(MouseY_Postfix))
        };

        foreach ((string name, string postfix) in targets)
        {
            try
            {
                var original = AccessTools.Method(typeof(Game1), name, new[] { typeof(bool) })
                    ?? throw new MissingMethodException($"Game1.{name}(bool)");

                harmony.Patch(original, postfix: new HarmonyMethod(typeof(CursorPatches), postfix));
            }
            catch (Exception ex)
            {
                monitor.Log($"Falha ao patchar Game1.{name}(bool); a mira 3D ficara indisponivel.\n{ex}", LogLevel.Error);
            }
        }
    }

    private static void MouseX_Postfix(bool ui_scale, ref int __result)
    {
        if (TryGetAimScreenPosition(ui_scale, out float x, out _))
            __result = (int)x;
    }

    private static void MouseY_Postfix(bool ui_scale, ref int __result)
    {
        if (TryGetAimScreenPosition(ui_scale, out _, out float y))
            __result = (int)y;
    }

    /// <summary>
    /// Converte o ponto mirado (em pixels de mundo) para a coordenada de tela que o jogo
    /// espera receber do mouse, desfazendo o deslocamento do viewport.
    /// </summary>
    private static bool TryGetAimScreenPosition(bool ui_scale, out float x, out float y)
    {
        x = y = 0f;

        // Coordenada de UI: o mouse real continua valendo.
        if (ui_scale)
            return false;

        ModEntry mod = ModEntry.Instance;
        if (!mod.ShouldRender3D() || !mod.Input.HasAim)
            return false;

        // Com menu aberto, o jogador esta usando o cursor, nao a mira.
        if (Game1.activeClickableMenu is not null)
            return false;

        x = mod.Input.AimPixel.X - Game1.viewport.X;
        y = mod.Input.AimPixel.Y - Game1.viewport.Y;
        return true;
    }
}

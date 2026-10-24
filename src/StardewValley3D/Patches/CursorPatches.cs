using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace StardewValley3D.Patches;

/// <summary>
/// Publica o tile mirado pela crosshair onde o jogo espera encontrar o tile sob o mouse.
/// E o patch que faz as mecanicas vanilla "simplesmente funcionarem" em 3D (ver Fase 4):
/// ferramentas, armas, interacao e highlight passam a seguir a mira sem nenhuma alteracao
/// na logica de cada uma delas.
/// </summary>
internal static class CursorPatches
{
    private static IMonitor monitor = null!;

    public static void Apply(Harmony harmony, IMonitor monitorInstance)
    {
        monitor = monitorInstance;

        try
        {
            harmony.Patch(
                original: AccessTools.PropertyGetter(typeof(Game1), nameof(Game1.currentCursorTile)),
                postfix: new HarmonyMethod(typeof(CursorPatches), nameof(CurrentCursorTile_Postfix)));
        }
        catch (Exception ex)
        {
            monitor.Log($"Falha ao aplicar o patch de cursor; a mira 3D ficara indisponivel.\n{ex}", LogLevel.Error);
        }
    }

    private static void CurrentCursorTile_Postfix(ref Vector2 __result)
    {
        ModEntry mod = ModEntry.Instance;
        if (!mod.ShouldRender3D() || !mod.Input.HasAim)
            return;

        __result = mod.Input.AimTile;
    }
}

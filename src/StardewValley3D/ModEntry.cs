using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley3D.Hud;
using StardewValley3D.Input;
using StardewValley3D.Patches;
using StardewValley3D.Rendering;

namespace StardewValley3D;

/// <summary>
/// Ponto de entrada. Responsabilidades: ciclo de vida, config, e manter o estado global
/// que os patches Harmony consultam. Nenhuma logica de render mora aqui.
/// </summary>
public sealed class ModEntry : Mod
{
    public static ModEntry Instance = null!;

    public ModConfig Config = null!;
    public Renderer3D Renderer = null!;
    public ThirdPersonInput Input = null!;
    public DebugHud Debug = null!;

    /// <summary>Se o render 3D esta ativo (toggle do usuario).</summary>
    public bool Enabled { get; private set; }

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        this.Config = helper.ReadConfig<ModConfig>();

        this.Renderer = new Renderer3D(this.Monitor, this.Config);
        this.Input = new ThirdPersonInput(helper, this.Config, this.Renderer.Camera);
        this.Debug = new DebugHud(this.Config);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.Renderer.InvalidateAll();
        helper.Events.Player.Warped += (_, e) => this.Renderer.OnWarped(e.NewLocation);
        helper.Events.World.LocationListChanged += (_, _) => this.Renderer.InvalidateAll();
        helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        var harmony = new Harmony(this.ModManifest.UniqueID);
        DrawPatches.Apply(harmony, this.Monitor);
        CursorPatches.Apply(harmony, this.Monitor);

        this.Renderer.Initialize(Game1.graphics.GraphicsDevice, this.Helper);
        this.Monitor.Log("Stardew Valley 3D carregado. F9 alterna o renderizador.", LogLevel.Info);
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (this.Config.ToggleRenderer.JustPressed())
        {
            this.Enabled = !this.Enabled;
            this.Input.SetActive(this.Enabled);
            Game1.addHUDMessage(new HUDMessage($"Modo 3D: {(this.Enabled ? "ON" : "OFF")}", 2));
        }

        if (this.Config.ToggleDebugHud.JustPressed())
            this.Debug.Visible = !this.Debug.Visible;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!this.ShouldRender3D())
            return;

        this.Input.Update();
        this.Renderer.Update(Game1.currentGameTime ?? new GameTime());
    }

    /// <summary>
    /// Decide, por tick, se o mundo deve ser desenhado em 3D. Os patches tambem consultam
    /// isto, por isso a decisao e centralizada aqui.
    /// </summary>
    public bool ShouldRender3D()
    {
        if (!this.Enabled || !Context.IsWorldReady || Game1.currentLocation is null)
            return false;

        // Cutscenes/festivais assumem camera top-down: ver Fase 5 do escopo.
        if (this.Config.FallbackDuringEvents && (Game1.CurrentEvent is not null || Game1.eventUp))
            return false;

        if (Game1.currentMinigame is not null)
            return false;

        if (this.Config.FallbackWithoutGeometry && !this.Renderer.HasGeometryFor(Game1.currentLocation))
            return false;

        return true;
    }
}

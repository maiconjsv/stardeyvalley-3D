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
using StardewValley3D.World;

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
    public FirstPersonInput Input = null!;
    public DebugHud Debug = null!;
    public GeometryEditor Editor = null!;

    /// <summary>Se o render 3D esta ativo (toggle do usuario).</summary>
    public bool Enabled { get; private set; }

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        this.Config = helper.ReadConfig<ModConfig>();

        this.Renderer = new Renderer3D(this.Monitor, this.Config);
        this.Input = new FirstPersonInput(helper, this.Config, this.Renderer.Camera);
        this.Debug = new DebugHud(this.Config);
        this.Editor = new GeometryEditor(helper, this.Monitor);

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

        if (this.Config.ToggleEditor.JustPressed())
            this.Editor.Toggle();

        if (this.Editor.Active && Context.IsWorldReady && Game1.currentLocation is not null)
        {
            bool ctrl = this.Helper.Input.IsDown(SButton.LeftControl) || this.Helper.Input.IsDown(SButton.RightControl);

            if (ctrl && this.Helper.Input.GetState(SButton.S) == SButtonState.Pressed)
            {
                this.Editor.Save(
                    this.Renderer.GetGeometry(Game1.currentLocation),
                    Game1.currentLocation.NameOrUniqueName);
            }

            if (this.Helper.Input.GetState(SButton.F5) == SButtonState.Pressed)
            {
                this.Renderer.ReloadGeometry(Game1.currentLocation);
                Game1.addHUDMessage(new HUDMessage("Geometria recarregada do disco", 2));
            }
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!this.ShouldRender3D())
            return;

        this.Input.Update();
        this.Renderer.Update(Game1.currentGameTime ?? new GameTime());

        // O editor reconstroi a malha a cada alteracao: o retorno visual imediato e o que
        // torna a autoria viavel.
        if (this.Editor.Active && Game1.currentLocation is not null)
        {
            LocationGeometry geometry = this.Renderer.GetGeometry(Game1.currentLocation);
            if (this.Editor.HandleInput(geometry, this.Input.AimTile, this.Input.HasAim))
                this.Renderer.InvalidateMesh(Game1.currentLocation);
        }
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

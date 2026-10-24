using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley3D.Rendering;

namespace StardewValley3D.World;

/// <summary>
/// Mini-engine de autoria de cenario, em jogo. Mira no tile com a crosshair e ajusta a
/// altura do chao e da parede ao vivo, com a malha reconstruida a cada mudanca.
///
/// Existe porque a heuristica que deriva altura do mapa 2D acerta o caso geral e erra muitos
/// casos particulares -- e corrigir cada um deles editando JSON na mao, sem ver o resultado,
/// seria inviavel. Aqui o ciclo e: olhar, ajustar, ver, salvar.
///
/// O resultado vai para assets/geometry/&lt;Localizacao&gt;.json, que e carregado
/// automaticamente na proxima vez que a localizacao for construida.
/// </summary>
public sealed class GeometryEditor
{
    /// <summary>Passo de altura, em tiles. Um quarto de tile da 16 px, a grade do jogo.</summary>
    private const float Step = 0.25f;

    private readonly IModHelper helper;
    private readonly IMonitor monitor;

    private (float Floor, float WallTop)? clipboard;

    public bool Active { get; private set; }
    public int BrushSize { get; private set; } = 1;
    public string Status { get; private set; } = "";

    public GeometryEditor(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
    }

    public void Toggle()
    {
        this.Active = !this.Active;
        this.Status = this.Active ? "editor ligado" : "";
        Game1.addHUDMessage(new HUDMessage($"Editor de cenario: {(this.Active ? "ON" : "OFF")}", 2));
    }

    /// <summary>Processa as teclas do editor. Retorna true se a malha precisa ser reconstruida.</summary>
    public bool HandleInput(LocationGeometry geometry, Vector2 aimTile, bool hasAim)
    {
        if (!this.Active)
            return false;

        IInputHelper input = this.helper.Input;

        if (input.IsDown(SButton.OemOpenBrackets))
            this.BrushSize = Math.Max(1, this.BrushSize - 1);
        if (input.IsDown(SButton.OemCloseBrackets))
            this.BrushSize = Math.Min(9, this.BrushSize + 1);

        if (!hasAim)
            return false;

        int cx = (int)aimTile.X;
        int cz = (int)aimTile.Y;

        float floorDelta = 0f;
        float wallDelta = 0f;
        bool clear = false;
        bool paste = false;

        if (input.GetState(SButton.Q) == SButtonState.Pressed) floorDelta = -Step;
        if (input.GetState(SButton.E) == SButtonState.Pressed) floorDelta = Step;
        if (input.GetState(SButton.Z) == SButtonState.Pressed) wallDelta = -Step;
        if (input.GetState(SButton.C) == SButtonState.Pressed) wallDelta = Step;
        if (input.GetState(SButton.X) == SButtonState.Pressed) clear = true;
        if (input.GetState(SButton.G) == SButtonState.Pressed) paste = true;

        if (input.GetState(SButton.F) == SButtonState.Pressed)
        {
            this.clipboard = (geometry.FloorHeightAt(cx, cz), geometry.WallTopAt(cx, cz));
            this.Status = $"copiado chao={this.clipboard.Value.Floor:F2} parede={this.clipboard.Value.WallTop:F2}";
            return false;
        }

        if (floorDelta == 0f && wallDelta == 0f && !clear && !paste)
            return false;

        int radius = this.BrushSize / 2;
        bool changed = false;

        for (int z = cz - radius; z <= cz + radius; z++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (!geometry.InBounds(x, z))
                    continue;

                if (clear)
                {
                    geometry.ClearEdit(x, z);
                    changed = true;
                    continue;
                }

                if (paste && this.clipboard is { } value)
                {
                    geometry.Edit(x, z, value.Floor, value.WallTop);
                    changed = true;
                    continue;
                }

                float floor = geometry.FloorHeightAt(x, z) + floorDelta;
                float wall = Math.Max(0f, geometry.WallTopAt(x, z) + wallDelta);
                geometry.Edit(x, z, floor, wall);
                changed = true;
            }
        }

        if (changed)
            this.Status = $"chao={geometry.FloorHeightAt(cx, cz):F2} parede={geometry.WallTopAt(cx, cz):F2}";

        return changed;
    }

    /// <summary>Grava as edicoes em assets/geometry/&lt;Localizacao&gt;.json.</summary>
    public void Save(LocationGeometry geometry, string locationName)
    {
        try
        {
            GeometryFile file = geometry.ToFile(locationName);
            this.helper.Data.WriteJsonFile($"assets/geometry/{locationName}.json", file);
            geometry.MarkSaved();

            this.Status = $"salvo: {file.Sectors.Count} setores";
            this.monitor.Log($"Geometria de '{locationName}' salva ({file.Sectors.Count} setores).", LogLevel.Info);
            Game1.addHUDMessage(new HUDMessage($"Geometria salva: {file.Sectors.Count} setores", 1));
        }
        catch (Exception ex)
        {
            this.Status = "erro ao salvar (ver log)";
            this.monitor.Log($"Falha ao salvar a geometria de '{locationName}'.\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Texto de ajuda desenhado na tela enquanto o editor esta ligado.</summary>
    public string[] GetHelpLines(LocationGeometry geometry, Vector2 aimTile)
    {
        int x = (int)aimTile.X;
        int z = (int)aimTile.Y;

        return new[]
        {
            $"EDITOR DE CENARIO   pincel {this.BrushSize}x{this.BrushSize}   {(geometry.HasUnsavedEdits ? "*nao salvo*" : "salvo")}",
            $"tile {x},{z}   chao {geometry.FloorHeightAt(x, z):F2}   parede {geometry.WallTopAt(x, z):F2}   edicoes {geometry.EditCount}",
            "Q/E chao -/+    Z/C parede -/+    X limpa tile",
            "F copia   G cola   [ ] pincel   Ctrl+S salva   F7 sai",
            this.Status
        };
    }
}

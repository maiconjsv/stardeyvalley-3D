using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace StardewValley3D;

/// <summary>Configuracao do mod, exposta em jogo via GMCM (Fase 7).</summary>
public sealed class ModConfig
{
    // --- Controles ---
    public KeybindList ToggleRenderer { get; set; } = KeybindList.Parse("F9");
    public KeybindList ToggleDebugHud { get; set; } = KeybindList.Parse("F10");
    public KeybindList ToggleMouseLook { get; set; } = KeybindList.Parse("LeftAlt");

    // --- Camera ---
    /// <summary>Campo de visao vertical, em graus.</summary>
    public float FieldOfView { get; set; } = 70f;
    /// <summary>Distancia da camera ao jogador, em tiles.</summary>
    public float CameraDistance { get; set; } = 4.5f;
    /// <summary>Altura do alvo da camera acima dos pes do jogador, em tiles.</summary>
    public float CameraHeight { get; set; } = 1.6f;
    public float MouseSensitivity { get; set; } = 0.15f;
    public bool InvertPitch { get; set; } = false;
    /// <summary>0 = camera rigida (anti motion sickness), 1 = suavizacao maxima.</summary>
    public float CameraSmoothing { get; set; } = 0.35f;

    // --- Estetica ---
    /// <summary>Altura em pixels do render target interno; 0 = resolucao nativa.</summary>
    public int InternalResolutionHeight { get; set; } = 400;
    public bool DistanceFog { get; set; } = true;
    public bool CrossBillboardTrees { get; set; } = true;
    public bool BlobShadows { get; set; } = true;

    // --- Seguranca / fallback ---
    /// <summary>Volta ao render 2D em cutscenes e festivais (recomendado ate a Fase 5).</summary>
    public bool FallbackDuringEvents { get; set; } = true;
    /// <summary>Volta ao render 2D em localizacoes sem geometria confiavel.</summary>
    public bool FallbackWithoutGeometry { get; set; } = true;
    public bool VerboseLogging { get; set; } = false;
}

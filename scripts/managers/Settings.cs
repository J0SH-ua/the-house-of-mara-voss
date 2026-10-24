using System;
using Godot;

/// <summary>
/// Persistent player settings (user://settings.cfg).
/// Autoload — registered right after Globals so every other system can read
/// it during its own _Ready. Static helpers provide sane defaults when the
/// autoload is somehow absent (e.g. running a lone scene from the editor).
/// </summary>
public partial class Settings : Node
{
    public static Settings Instance { get; private set; }

    public event Action Changed;

    private const string ConfigPath = "user://settings.cfg";

    // --- Audio (linear 0..1) ---
    public float MasterVolume { get; set; } = 0.9f;
    public float MusicVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 0.9f;
    public float AmbienceVolume { get; set; } = 0.8f;

    // --- Dialogue ---
    /// <summary>Characters per second for the typewriter. 0 = instant.</summary>
    public float TextSpeedCps { get; set; } = 70f;
    /// <summary>When true, dialogue lines advance on their own after a read delay.</summary>
    public bool AutoAdvanceDialogue { get; set; } = false;

    // --- Display / accessibility ---
    public bool Fullscreen { get; set; } = false;
    /// <summary>Reduces flashing/pulsing and caps corruption screen effects.</summary>
    public bool ReduceMotion { get; set; } = false;
    /// <summary>0..1 master strength of corruption screen/audio distortion.</summary>
    public float CorruptionIntensity { get; set; } = 1.0f;
    /// <summary>Allow Tab to reveal remaining hotspots.</summary>
    public bool HotspotHints { get; set; } = true;
    /// <summary>Suppress the auto-opening tutorial after it has been seen once.</summary>
    public bool TutorialSeen { get; set; } = false;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        Load();
        ApplyDisplay();
        Logger.Info("Settings loaded.");
    }

    // ------------------------------------------------------------- safe access

    public static float TextSpeedOrDefault() => Instance?.TextSpeedCps ?? 70f;
    public static bool AutoAdvanceOrDefault() => Instance?.AutoAdvanceDialogue ?? false;
    public static bool ReduceMotionOrDefault() => Instance?.ReduceMotion ?? false;
    public static float CorruptionIntensityOrDefault() => Instance?.CorruptionIntensity ?? 1.0f;
    public static bool HotspotHintsOrDefault() => Instance?.HotspotHints ?? true;

    // ------------------------------------------------------------ persistence

    public void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok)
            return; // first run — defaults stand

        MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume).AsDouble();
        MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume).AsDouble();
        SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume).AsDouble();
        AmbienceVolume = (float)cfg.GetValue("audio", "ambience", AmbienceVolume).AsDouble();

        TextSpeedCps = (float)cfg.GetValue("dialogue", "text_speed_cps", TextSpeedCps).AsDouble();
        AutoAdvanceDialogue = cfg.GetValue("dialogue", "auto_advance", AutoAdvanceDialogue).AsBool();

        Fullscreen = cfg.GetValue("display", "fullscreen", Fullscreen).AsBool();
        ReduceMotion = cfg.GetValue("accessibility", "reduce_motion", ReduceMotion).AsBool();
        CorruptionIntensity = (float)cfg.GetValue("accessibility", "corruption_intensity", CorruptionIntensity).AsDouble();
        HotspotHints = cfg.GetValue("accessibility", "hotspot_hints", HotspotHints).AsBool();
        TutorialSeen = cfg.GetValue("gameplay", "tutorial_seen", TutorialSeen).AsBool();
    }

    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ambience", AmbienceVolume);

        cfg.SetValue("dialogue", "text_speed_cps", TextSpeedCps);
        cfg.SetValue("dialogue", "auto_advance", AutoAdvanceDialogue);

        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.SetValue("accessibility", "reduce_motion", ReduceMotion);
        cfg.SetValue("accessibility", "corruption_intensity", CorruptionIntensity);
        cfg.SetValue("accessibility", "hotspot_hints", HotspotHints);
        cfg.SetValue("gameplay", "tutorial_seen", TutorialSeen);

        cfg.Save(ConfigPath);
    }

    /// <summary>Save + notify listeners (AudioManager volumes, overlay strength, …).</summary>
    public void NotifyChanged()
    {
        ApplyDisplay();
        Save();
        Changed?.Invoke();
    }

    public void ApplyDisplay()
    {
        DisplayServer.WindowSetMode(Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
    }
}

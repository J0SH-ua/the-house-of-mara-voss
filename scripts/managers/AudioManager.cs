using System;
using System.Collections.Generic;
using Godot;

/// <summary>Named SFX ids. Files live in res://assets/Music/SFX/&lt;id&gt;.wav
/// (plus two legacy .mp3s). Generate/refresh them with tools/generate_sfx.py.</summary>
public static class Sfx
{
    public const string UiClick = "ui_click";
    public const string UiHover = "ui_hover";
    public const string UiBack = "ui_back";
    public const string DialogueBlip = "dialogue_blip";
    public const string DialogueAdvance = "dialogue_advance";
    public const string PageTurn = "page_turn";
    public const string Examine = "examine";
    public const string ClarifyArm = "clarify_arm";
    public const string ClarifyReveal = "clarify_reveal";
    public const string Denied = "denied";
    public const string WordChime = "word_chime";
    public const string CorruptionGlitch = "corruption_glitch";
    public const string CorruptionWhisper = "corruption_whisper";
    public const string MemoryShatter = "memory_shatter";
    public const string ScreenCrack1 = "screen_crack_1";
    public const string ScreenCrack2 = "screen_crack_2";
    public const string ScreenCrack3 = "screen_crack_3";
    public const string DialTick = "dial_tick";
    public const string KeyType = "key_type";
    public const string LockOpen = "lock_open";
    public const string LockRattle = "lock_rattle";
    public const string DrawerOpen = "drawer_open";
    public const string DrawerClose = "drawer_close";
    public const string DoorCreak = "door_creak";
    public const string DoorUnlock = "door_unlock";
    public const string ShardCollect = "shard_collect";
    public const string ShardPlace = "shard_place";
    public const string PhotoPickup = "photo_pickup";
    public const string CameraShutter = "camera_shutter"; // legacy mp3
    public const string PuzzleSolve = "puzzle_solve";
    public const string RoomComplete = "room_complete";
    public const string ClockTick = "clock_tick";
    public const string FootstepCarpetA = "footstep_carpet_a";
    public const string FootstepCarpetB = "footstep_carpet_b";
    public const string FootstepDirtA = "footstep_dirt_a";
    public const string FootstepDirtB = "footstep_dirt_b";
    public const string TruthBridgeSteps = "truth_bridge_steps";
    public const string TruthRailingBreak = "truth_railing_break";
    public const string TruthWaterSplash = "truth_water_splash";
    public const string LockpickTick = "lockpick_tick";
    public const string LockpickSnap = "lockpick_snap";
    public const string VentCrawl = "vent_crawl";
    public const string FlashlightClick = "flashlight_click";
}

/// <summary>
/// Central audio system (autoload).
/// Buses (Music / SFX / Ambience) are created at runtime if the project has no
/// saved bus layout yet, so this works without any editor setup; once you save
/// a default_bus_layout.tres with the same names, that layout is respected.
/// Owns: crossfading music, a polyphonic SFX pool, looping room ambience,
/// the corruption heartbeat, and corruption-driven music detune/muffle.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    // ------------------------------------------------------------------ paths
    public const string MusicMenu = "res://assets/Music/bg/Glance Out A Casement Window.mp3";
    public const string MusicIntro = "res://assets/Music/bg/conclusion.mp3";
    public const string MusicRoom = "res://assets/Music/bg/Glance Out A Casement Window.mp3";
    public const string MusicRoom1 = "res://assets/Music/bg/room_1.wav";
    public const string MusicRoom2 = "res://assets/Music/bg/room_2.wav";
    public const string MusicRoom3 = "res://assets/Music/bg/room_3.wav";
    public const string MusicRoom4 = "res://assets/Music/bg/room_4.wav";
    public const string MusicRoom5 = "res://assets/Music/bg/room_5.wav";
    public const string MusicRoom6 = "res://assets/Music/bg/room_6.wav";
    public const string MusicFinale = "res://assets/Music/bg/conclusion.mp3";
    /// <summary>Tense 15-second bed under the Clarity-collapse countdown.</summary>
    public const string MusicCollapse = "res://assets/Music/bg/collapse.wav";
    /// <summary>Release ending: still F minor, but relieved — breathing again.</summary>
    public const string MusicEndingRelease = "res://assets/Music/bg/ending_release.wav";
    /// <summary>Kept-house ending: the same room tone, giving up.</summary>
    public const string MusicEndingKept = "res://assets/Music/bg/ending_kept.wav";

    private const string SfxDir = "res://assets/Music/SFX/";
    private const string AmbiencePath = SfxDir + "ambience_room_tone.wav";
    private const string AmbienceRainPath = SfxDir + "ambience_rain.wav";
    private const string AmbienceFirePath = SfxDir + "ambience_fire.wav";
    private const string AmbienceGardenPath = SfxDir + "ambience_garden.wav";
    private const string AmbiencePorchPath = SfxDir + "ambience_porch.wav";
    private const string AmbienceBasementPath = SfxDir + "ambience_basement.wav";
    public const string AmbienceRoomToneLoopPath = AmbiencePath;
    public const string AmbienceRainLoopPath = AmbienceRainPath;
    public const string AmbienceGardenLoopPath = AmbienceGardenPath;
    public const string AmbiencePorchLoopPath = AmbiencePorchPath;
    public const string AmbienceBasementLoopPath = AmbienceBasementPath;
    private const string HeartbeatPath = SfxDir + "heartbeat.wav";

    private static readonly Dictionary<string, string> LegacySfxPaths = new()
    {
        [Sfx.CameraShutter] = SfxDir + "mixkit-old-camera-shutter-click-1137.mp3",
    };

    // ------------------------------------------------------------------ state
    private readonly List<AudioStreamPlayer> _sfxPool = new();
    private const int SfxPoolSize = 14;
    private int _sfxCursor;

    private AudioStreamPlayer _musicA;
    private AudioStreamPlayer _musicB;
    private bool _musicAIsActive = true;
    private string _currentMusicPath = string.Empty;
    private float _musicBaseVolumeDb;
    private Tween _musicTween;

    private AudioStreamPlayer _ambiencePlayer;
    private AudioStreamPlayer _ambiencePlayerB;
    private bool _ambienceAIsActive = true;
    private string _currentAmbiencePath = string.Empty;
    private Tween _ambienceTween;
    private AudioStreamPlayer _heartbeatPlayer;
    private bool _ambienceWanted;

    private AudioEffectLowPassFilter _musicLowPass;
    private float _corruption01;
    private double _wobbleTime;
    private double _whisperTimer = 18.0;
    private double _glitchTimer = 30.0;
    private readonly RandomNumberGenerator _rng = new();

    private readonly Dictionary<string, AudioStream> _streamCache = new();
    private readonly HashSet<string> _warnedMissing = new();

    private AudioStreamPlayer ActiveMusic => _musicAIsActive ? _musicA : _musicB;
    private AudioStreamPlayer InactiveMusic => _musicAIsActive ? _musicB : _musicA;
    private AudioStreamPlayer ActiveAmbience => _ambienceAIsActive ? _ambiencePlayer : _ambiencePlayerB;
    private AudioStreamPlayer InactiveAmbience => _ambienceAIsActive ? _ambiencePlayerB : _ambiencePlayer;

    // -------------------------------------------------------------------- init

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        _rng.Randomize();

        EnsureBuses();

        _musicA = MakePlayer("Music");
        _musicB = MakePlayer("Music");
        _ambiencePlayer = MakePlayer("Ambience");
        _ambiencePlayerB = MakePlayer("Ambience");
        _heartbeatPlayer = MakePlayer("Ambience");
        _heartbeatPlayer.VolumeDb = -80f;

        for (int i = 0; i < SfxPoolSize; i++)
            _sfxPool.Add(MakePlayer("SFX"));

        ApplyVolumes();
        if (Settings.Instance != null)
            Settings.Instance.Changed += ApplyVolumes;

        RunSelfTestDelayed();
        Logger.Info("AudioManager ready (buses: Music/SFX/Ambience).");
    }

    private AudioStreamPlayer MakePlayer(string bus)
    {
        var p = new AudioStreamPlayer { Bus = bus, ProcessMode = ProcessModeEnum.Always };
        AddChild(p);
        return p;
    }

    private void EnsureBuses()
    {
        EnsureBus("Music");
        EnsureBus("SFX");
        EnsureBus("Ambience");

        // A low-pass on the Music bus lets corruption "muffle" the score.
        int musicIdx = AudioServer.GetBusIndex("Music");
        for (int i = 0; i < AudioServer.GetBusEffectCount(musicIdx); i++)
        {
            if (AudioServer.GetBusEffect(musicIdx, i) is AudioEffectLowPassFilter existing)
                _musicLowPass = existing;
        }

        if (_musicLowPass == null)
        {
            _musicLowPass = new AudioEffectLowPassFilter { CutoffHz = 20500f };
            AudioServer.AddBusEffect(musicIdx, _musicLowPass);
        }
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
            return;

        AudioServer.AddBus();
        int idx = AudioServer.GetBusCount() - 1;
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, "Master");
    }

    // ----------------------------------------------------------------- volumes

    public void ApplyVolumes()
    {
        var s = Settings.Instance;
        SetBusLinear("Master", s?.MasterVolume ?? 0.9f);
        SetBusLinear("Music", s?.MusicVolume ?? 0.8f);
        SetBusLinear("SFX", s?.SfxVolume ?? 0.9f);
        SetBusLinear("Ambience", s?.AmbienceVolume ?? 0.8f);
    }

    private static void SetBusLinear(string bus, float linear)
    {
        int idx = bus == "Master" ? 0 : AudioServer.GetBusIndex(bus);
        if (idx < 0)
            return;

        linear = Mathf.Clamp(linear, 0f, 1f);
        AudioServer.SetBusMute(idx, linear <= 0.001f);
        // LinearToDb(0) is -inf; clamp the input so fades always land somewhere real.
        AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
    }

    /// <summary>Static, null-safe entry point used all over the codebase.</summary>
    public static void Play(string sfxId, float volumeDb = 0f, float pitchScale = 1f)
    {
        Instance?.PlaySfx(sfxId, volumeDb, pitchScale);
    }

    /// <summary>Play with a small random pitch wobble (typewriter blips, ticks…).</summary>
    public static void PlayVaried(string sfxId, float volumeDb = 0f, float pitchCenter = 1f, float pitchSpread = 0.07f)
    {
        if (Instance == null)
            return;
        float pitch = pitchCenter + Instance._rng.RandfRange(-pitchSpread, pitchSpread);
        Instance.PlaySfx(sfxId, volumeDb, pitch);
    }

    public void PlaySfx(string sfxId, float volumeDb = 0f, float pitchScale = 1f)
    {
        var stream = LoadStream(SfxPathFor(sfxId));
        if (stream == null)
            return;

        // Round-robin: prefer an idle voice, otherwise steal the oldest slot.
        AudioStreamPlayer player = null;
        for (int i = 0; i < _sfxPool.Count; i++)
        {
            int idx = (_sfxCursor + i) % _sfxPool.Count;
            if (!_sfxPool[idx].Playing)
            {
                player = _sfxPool[idx];
                _sfxCursor = idx + 1;
                break;
            }
        }
        player ??= _sfxPool[_sfxCursor++ % _sfxPool.Count];

        player.Stream = stream;
        player.VolumeDb = volumeDb;
        player.PitchScale = Mathf.Max(0.05f, pitchScale);
        player.Play();
    }

    private static string SfxPathFor(string sfxId)
    {
        return LegacySfxPaths.TryGetValue(sfxId, out var legacy) ? legacy : SfxDir + sfxId + ".wav";
    }

    // ------------------------------------------------------------------- music

    public void PlayMusic(string path, float volumeDb = 0f, float fadeSeconds = 1.2f)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (path == _currentMusicPath && ActiveMusic.Playing)
        {
            _musicBaseVolumeDb = volumeDb;
            CreateTween()?.TweenProperty(ActiveMusic, "volume_db", volumeDb, fadeSeconds);
            return;
        }

        var stream = LoadStream(path);
        if (stream == null)
            return;

        MakeLooping(stream);
        _currentMusicPath = path;
        _musicBaseVolumeDb = volumeDb;

        var incoming = InactiveMusic;
        var outgoing = ActiveMusic;
        incoming.Stream = stream;
        incoming.VolumeDb = -60f;
        incoming.Play();

        _musicTween?.Kill();
        _musicTween = CreateTween();
        _musicTween.SetParallel(true);
        _musicTween.TweenProperty(incoming, "volume_db", volumeDb, fadeSeconds);
        if (outgoing.Playing)
            _musicTween.TweenProperty(outgoing, "volume_db", -60f, fadeSeconds);
        _musicTween.Chain().TweenCallback(Callable.From(() => outgoing.Stop()));

        _musicAIsActive = incoming == _musicA;
    }

    public void StopMusic(float fadeSeconds = 0.8f)
    {
        _currentMusicPath = string.Empty;
        _musicTween?.Kill();
        if (!_musicA.Playing && !_musicB.Playing)
            return;

        // Fade both crossfade voices: a journey should not inherit the
        // tail of whichever room score happened to be leaving.
        _musicTween = CreateTween();
        _musicTween.SetParallel(true);
        if (_musicA.Playing)
            _musicTween.TweenProperty(_musicA, "volume_db", -60f, fadeSeconds);
        if (_musicB.Playing)
            _musicTween.TweenProperty(_musicB, "volume_db", -60f, fadeSeconds);
        _musicTween.Chain().TweenCallback(Callable.From(() =>
        {
            _musicA.Stop();
            _musicB.Stop();
        }));
    }

    /// <summary>Music + ambience policy for scenes loaded through SceneManager.</summary>
    public void PlayMusicForScene(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath) || !scenePath.Contains("scenes/world/"))
            return;

        string scene = scenePath.ToLowerInvariant();

        // Walking between rooms is deliberately bare: no borrowed room
        // score, room tone, garden loop, or heartbeat. Mara footsteps and
        // the ordinary dialogue sounds still belong to the walk itself.
        if (scene.Contains("/journey_to_"))
        {
            StopMusic(0.3f);
            StopAmbience(0.3f);
            return;
        }

        PlayAmbienceForScene(scenePath);

        // The score now sits underneath the place itself. Ambience loops
        // are several dB quieter at source than the room music, so these
        // gains are low enough for rain/fire/room tone to lead the mix.
        bool puzzle = scene.Contains("puzzle") || scene.Contains("difference_investigation");
        float volume = puzzle ? -28f : -25f;

        if (scene.Contains("difference_investigation"))
            PlayMusic(MusicRoom2, -28f);
        else if (scene.Contains("jar_closeup"))
            PlayMusic(MusicRoom3, -28f);
        else if (scene.Contains("kitchen_cabinet"))
            PlayMusic(MusicRoom1, -28f);
        else if (scene.Contains("flashback"))
            PlayMusic(MusicRoom4, -28f);
        else if (scene.Contains("apartment"))
            PlayMusic(MusicRoom5, -28f);
        else if (scene.Contains("basement") || scene.Contains("vent_crawl") || scene.Contains("lockpick") || scene.Contains("breaker_box"))
            PlayMusic(MusicRoom5, -29f);
        else if (scene.Contains("porch"))
            PlayMusic(MusicRoom6, -26f);
        else if (scene.EndsWith("room_7.tscn") || scene.EndsWith("door.tscn"))
            PlayMusic(MusicFinale, -18f);
        else if (scene.Contains("bedroom"))
            PlayMusic(MusicRoom1, -26f);
        else if (scene.Contains("room_1"))
            PlayMusic(MusicRoom1, volume);
        else if (scene.Contains("room_2"))
            PlayMusic(MusicRoom2, volume);
        else if (scene.Contains("room_3"))
            PlayMusic(MusicRoom3, volume);
        else if (scene.Contains("room_4"))
            PlayMusic(MusicRoom4, volume);
        else if (scene.Contains("room_5"))
            PlayMusic(MusicRoom5, volume);
        else if (scene.Contains("room_6"))
            PlayMusic(MusicRoom6, volume);
        else
            PlayMusic(MusicRoom, volume);
    }

    public void StartAmbience()
    {
        PlayAmbienceForScene(SceneManager.CurrentScenePath);
    }

    private void EnsureHeartbeat()
    {
        var hb = LoadStream(HeartbeatPath);
        if (hb != null && !_heartbeatPlayer.Playing)
        {
            MakeLooping(hb);
            _heartbeatPlayer.Stream = hb;
            _heartbeatPlayer.Play(); // volume follows corruption; starts silent
        }
    }

    /// <summary>Each place sounds like itself, mixed in front of the
    /// background score: rain where the windows are wet (Room 5, the
    /// Apartment), the fire in Room 2, wind and leaves in the garden,
    /// crickets at dusk on the porch, drips and furnace air below the
    /// house, and plain room tone everywhere else. Crossfaded like music.</summary>
    public void PlayAmbienceForScene(string scenePath)
    {
        string scene = (scenePath ?? string.Empty).ToLowerInvariant();
        if (scene.Contains("/journey_to_"))
        {
            StopAmbience(0.3f);
            return;
        }

        _ambienceWanted = true;
        EnsureHeartbeat();

        // A house with no power has only one sound left: itself,
        // holding its breath. Whatever room Mara is feeling her way
        // through, the blackout hushes it to bare room tone.
        if (GameState.Instance?.HasFlag("blackout_active") == true &&
            (scene.Contains("room_1") || scene.Contains("room_2") ||
             scene.Contains("room_3") || scene.Contains("room_4") ||
             scene.Contains("room_5") || scene.Contains("kitchen_cabinet")))
        {
            CrossfadeAmbience(AmbiencePath, -22f);
            return;
        }

        bool puzzle = scene.Contains("puzzle");
        (string path, float volume) ambience =
            scene.Contains("basement") || scene.Contains("vent_crawl") || scene.Contains("breaker_box") ? (AmbienceBasementPath, -14f)
            : scene.Contains("lockpick") ? (AmbiencePath, -20f)
            : scene.Contains("porch") ? (AmbiencePorchPath, -16f)
            : scene.Contains("apartment") ? (AmbienceRainPath, -15f)
            : scene.Contains("room_5") ? (AmbienceRainPath, -15f)
            : scene.Contains("room_2") || scene.Contains("difference_investigation") ? (AmbienceFirePath, puzzle || scene.Contains("difference_investigation") ? -20f : -17f)
            : scene.Contains("room_6") ? (AmbienceGardenPath, -16f)
            : (AmbiencePath, -18f);

        CrossfadeAmbience(ambience.path, ambience.volume);
    }

    /// <summary>Play one named ambience loop for a scripted sequence
    /// without reviving the corruption heartbeat underneath it.</summary>
    public void PlayCutsceneAmbience(string path, float volumeDb)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        _ambienceWanted = true;
        CrossfadeAmbience(path, volumeDb);
    }

    private void CrossfadeAmbience(string path, float volumeDb)
    {
        if (path == _currentAmbiencePath && ActiveAmbience.Playing)
        {
            ActiveAmbience.VolumeDb = volumeDb;
            return;
        }

        var stream = LoadStream(path);
        if (stream == null)
            return;
        MakeLooping(stream);
        _currentAmbiencePath = path;

        var incoming = InactiveAmbience;
        var outgoing = ActiveAmbience;
        _ambienceAIsActive = !_ambienceAIsActive;

        incoming.Stream = stream;
        incoming.VolumeDb = -60f;
        incoming.Play();

        _ambienceTween?.Kill();
        _ambienceTween = CreateTween();
        _ambienceTween.SetParallel(true);
        _ambienceTween.TweenProperty(incoming, "volume_db", volumeDb, 1.8f);
        if (outgoing.Playing)
        {
            _ambienceTween.TweenProperty(outgoing, "volume_db", -60f, 1.8f);
            _ambienceTween.Chain().TweenCallback(Callable.From(() => outgoing.Stop()));
        }
    }

    public void StopAmbience(float fadeSeconds = 0f)
    {
        _ambienceWanted = false;
        _ambienceTween?.Kill();
        _currentAmbiencePath = string.Empty;
        _heartbeatPlayer.Stop(); // no body loop under the footsteps

        if (fadeSeconds <= 0.01f || (!_ambiencePlayer.Playing && !_ambiencePlayerB.Playing))
        {
            _ambiencePlayer.Stop();
            _ambiencePlayerB?.Stop();
            return;
        }

        _ambienceTween = CreateTween();
        _ambienceTween.SetParallel(true);
        if (_ambiencePlayer.Playing)
            _ambienceTween.TweenProperty(_ambiencePlayer, "volume_db", -60f, fadeSeconds);
        if (_ambiencePlayerB.Playing)
            _ambienceTween.TweenProperty(_ambiencePlayerB, "volume_db", -60f, fadeSeconds);
        _ambienceTween.Chain().TweenCallback(Callable.From(() =>
        {
            if (_ambienceWanted)
                return;
            _ambiencePlayer.Stop();
            _ambiencePlayerB?.Stop();
        }));
    }

    /// <summary>Swap the room score for the countdown bed (collapse).</summary>
    public void PlayCollapseMusic() => PlayMusic(MusicCollapse, -22f, 0.45f);

    /// <summary>Restore whatever score the current scene calls for.</summary>
    public void RestoreSceneMusic() => PlayMusicForScene(SceneManager.CurrentScenePath);

    /// <summary>Pause-menu ducking: pull the score down without touching Settings.</summary>
    public void SetPausedDuck(bool ducked)
    {
        var target = ducked ? _musicBaseVolumeDb - 9f : _musicBaseVolumeDb;
        if (ActiveMusic.Playing)
            CreateTween()?.TweenProperty(ActiveMusic, "volume_db", target, 0.3f);
    }

    // -------------------------------------------------------------- corruption

    /// <summary>0 = calm … 1 = shattered. Drives heartbeat, detune and muffle.</summary>
    public void SetCorruption01(float value)
    {
        float intensity = Settings.CorruptionIntensityOrDefault();
        _corruption01 = Mathf.Clamp(value, 0f, 1f) * intensity;
    }

    public override void _Process(double delta)
    {
        _wobbleTime += delta;

        if (ActiveMusic.Playing)
        {
            float detune = 1f - 0.055f * _corruption01;
            float wobble = (float)Math.Sin(_wobbleTime * 0.35) * 0.012f * _corruption01;
            ActiveMusic.PitchScale = detune + wobble;

            if (_musicLowPass != null)
                _musicLowPass.CutoffHz = Mathf.Lerp(20500f, 1100f, _corruption01 * _corruption01);
        }

        if (_heartbeatPlayer.Playing)
        {
            // Silent when calm; a body you can hear when the memory frays.
            float targetDb = _corruption01 < 0.15f
                ? -80f
                : Mathf.Lerp(-30f, -12f, (_corruption01 - 0.15f) / 0.85f);
            _heartbeatPlayer.VolumeDb = Mathf.MoveToward(_heartbeatPlayer.VolumeDb, targetDb, (float)delta * 24f);
        }

        if (!_ambienceWanted || GetTree().Paused)
            return;

        // Sparse corruption flavours while exploring at high tiers.
        if (_corruption01 >= 0.45f)
        {
            _whisperTimer -= delta;
            if (_whisperTimer <= 0)
            {
                _whisperTimer = _rng.RandfRange(16f, 34f);
                PlaySfx(Sfx.CorruptionWhisper, -13f, _rng.RandfRange(0.85f, 1.1f));
            }
        }

        if (_corruption01 >= 0.8f)
        {
            _glitchTimer -= delta;
            if (_glitchTimer <= 0)
            {
                _glitchTimer = _rng.RandfRange(24f, 48f);
                PlaySfx(Sfx.CorruptionGlitch, -16f, _rng.RandfRange(0.8f, 1.2f));
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private AudioStream LoadStream(string path)
    {
        if (_streamCache.TryGetValue(path, out var cached))
            return cached;

        var stream = ResourceLoader.Load<AudioStream>(path);

        // Import-proof fallback: if the .import remap didn't produce the
        // imported sample (stale/mismatched .import metadata, fresh clone
        // before first import, export quirks), decode the WAV bytes
        // ourselves. A silent game because of import bookkeeping is the
        // worst possible failure mode for an audio-driven piece.
        if (stream == null && path.EndsWith(".wav"))
        {
            stream = LoadWavFromBytes(path);
            if (stream != null && _warnedMissing.Add(path + "#fallback"))
                Logger.Warning($"AudioManager: {path} loaded via raw-byte fallback — check that the editor reimports assets/Music/SFX cleanly.");
        }

        if (stream == null)
        {
            if (_warnedMissing.Add(path))
                Logger.Warning($"AudioManager: stream not found at {path} (run tools/generate_sfx.py?)");
            return null;
        }

        _streamCache[path] = stream;
        return stream;
    }

    private static void MakeLooping(AudioStream stream)
    {
        switch (stream)
        {
            case AudioStreamMP3 mp3:
                mp3.Loop = true;
                break;
            case AudioStreamWav wav when wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled:
            {
                // The .import files already request looping for our loop assets;
                // this is a runtime safety net using the raw sample data.
                int frameBytes = (wav.Format == AudioStreamWav.FormatEnum.Format8Bits ? 1 : 2) * (wav.Stereo ? 2 : 1);
                if (frameBytes > 0 && wav.Data.Length > 0)
                {
                    wav.LoopBegin = 0;
                    wav.LoopEnd = wav.Data.Length / frameBytes;
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                }
                break;
            }
            case AudioStreamOggVorbis ogg:
                ogg.Loop = true;
                break;
        }
    }

    // ------------------------------------------------------------- diagnostics

    private async void RunSelfTestDelayed()
    {
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);

        var buses = new System.Collections.Generic.List<string>();
        for (int i = 0; i < AudioServer.GetBusCount(); i++)
            buses.Add(AudioServer.GetBusName(i));

        string[] critical = [Sfx.UiClick, Sfx.UiHover, Sfx.Examine, Sfx.DialogueBlip];
        var missing = new System.Collections.Generic.List<string>();
        foreach (var id in critical)
        {
            if (LoadStream(SfxPathFor(id)) == null)
                missing.Add(id);
        }

        if (missing.Count == 0)
            Logger.Info($"[AudioManager self-test] OK — buses: {string.Join(", ", buses)}; core SFX loaded. If the game is silent, check Settings volumes and system output device.");
        else
            Logger.Warning($"[AudioManager self-test] MISSING streams: {string.Join(", ", missing)} — SFX will be silent. Reimport assets/Music/SFX (or delete the .godot folder and reopen).");
    }

    private AudioStream LoadWavFromBytes(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
            return null;

        byte[] bytes = file.GetBuffer((long)file.GetLength());
        return ParseWav(bytes);
    }

    /// <summary>Minimal RIFF/WAVE reader for our generated 16-bit PCM files.</summary>
    private static AudioStreamWav ParseWav(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 44)
            return null;

        int channels = 1, sampleRate = 22050, bits = 16;
        int dataOffset = -1, dataLength = 0;

        int i = 12; // skip "RIFF<size>WAVE"
        while (i + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, i, 4);
            int size = BitConverter.ToInt32(bytes, i + 4);

            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, i + 10);
                sampleRate = BitConverter.ToInt32(bytes, i + 12);
                bits = BitConverter.ToInt16(bytes, i + 22);
            }
            else if (id == "data")
            {
                dataOffset = i + 8;
                dataLength = Math.Min(size, bytes.Length - dataOffset);
                break;
            }

            i += 8 + size + (size % 2);
        }

        if (dataOffset < 0 || dataLength <= 0)
            return null;

        byte[] data = new byte[dataLength];
        System.Array.Copy(bytes, dataOffset, data, 0, dataLength);

        return new AudioStreamWav
        {
            Format = bits == 8 ? AudioStreamWav.FormatEnum.Format8Bits : AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = sampleRate,
            Stereo = channels > 1,
            Data = data,
        };
    }
}

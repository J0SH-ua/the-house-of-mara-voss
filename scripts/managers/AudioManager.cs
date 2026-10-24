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
    public const string MusicFinale = "res://assets/Music/bg/conclusion.mp3";

    private const string SfxDir = "res://assets/Music/SFX/";
    private const string AmbiencePath = SfxDir + "ambience_room_tone.wav";
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
        _heartbeatPlayer = MakePlayer("Ambience");
        _heartbeatPlayer.VolumeDb = -80f;

        for (int i = 0; i < SfxPoolSize; i++)
            _sfxPool.Add(MakePlayer("SFX"));

        ApplyVolumes();
        if (Settings.Instance != null)
            Settings.Instance.Changed += ApplyVolumes;

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
        var outgoing = ActiveMusic;
        if (!outgoing.Playing)
            return;

        _musicTween?.Kill();
        _musicTween = CreateTween();
        _musicTween.TweenProperty(outgoing, "volume_db", -60f, fadeSeconds);
        _musicTween.TweenCallback(Callable.From(() => outgoing.Stop()));
    }

    /// <summary>Music + ambience policy for scenes loaded through SceneManager.</summary>
    public void PlayMusicForScene(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath) || !scenePath.Contains("scenes/world/"))
            return;

        StartAmbience();
        if (scenePath.EndsWith("room_7.tscn"))
            PlayMusic(MusicFinale, -6f);
        else if (scenePath.Contains("puzzle"))
            PlayMusic(MusicRoom, -14f);
        else
            PlayMusic(MusicRoom, -11f);
    }

    public void StartAmbience()
    {
        _ambienceWanted = true;

        var amb = LoadStream(AmbiencePath);
        if (amb != null && !_ambiencePlayer.Playing)
        {
            MakeLooping(amb);
            _ambiencePlayer.Stream = amb;
            _ambiencePlayer.VolumeDb = -22f;
            _ambiencePlayer.Play();
        }

        var hb = LoadStream(HeartbeatPath);
        if (hb != null && !_heartbeatPlayer.Playing)
        {
            MakeLooping(hb);
            _heartbeatPlayer.Stream = hb;
            _heartbeatPlayer.Play(); // volume follows corruption; starts silent
        }
    }

    public void StopAmbience()
    {
        _ambienceWanted = false;
        _ambiencePlayer.Stop();
        _heartbeatPlayer.Stop();
    }

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
            case AudioStreamWAV wav when wav.LoopMode == AudioStreamWAV.LoopModeEnum.Disabled:
            {
                // The .import files already request looping for our loop assets;
                // this is a runtime safety net using the raw sample data.
                int frameBytes = (wav.Format == AudioStreamWAV.FormatEnum.Format8Bits ? 1 : 2) * (wav.Stereo ? 2 : 1);
                if (frameBytes > 0 && wav.Data.Length > 0)
                {
                    wav.LoopBegin = 0;
                    wav.LoopEnd = wav.Data.Length / frameBytes;
                    wav.LoopMode = AudioStreamWAV.LoopModeEnum.Forward;
                }
                break;
            }
            case AudioStreamOggVorbis ogg:
                ogg.Loop = true;
                break;
        }
    }
}

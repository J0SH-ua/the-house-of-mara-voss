using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// A short first-person journey between two completed rooms.
///
/// The camera visibly travels: straight down one memory hallway, left at
/// the bent runner, right past the photographs, or along the empty road
/// until the lit porch is close enough to name. On a first arrival Mara
/// stops with the newly recovered fragment in her hand, tries to remember
/// what it could be for, is briefly pulled into the room it came from, and
/// shakes the migraine back before the door can be opened. The destination
/// is not loaded until the player clicks the door (or the distant porch
/// light). Each wrapper scene stores its own destination and journey id, so
/// saving/quitting mid-journey resumes in the right place.
///
/// Room 4 keeps its authored brass-door close-up and Room 5 keeps its
/// wardrobe/garden passage; this script covers the plainer stretches of
/// house and road between Rooms 1-2, 2-3, 3-4, and 6-Porch.
/// </summary>
public partial class RoomJourney : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    private const string HandTexturePath = "res://assets/images/core/hand_palm.png";
    private const float HeldPalmX = 0.51f;
    private const float HeldPalmY = 0.36f;
    private const float HeldHandAspect = 1.332f;
    private const float RecoverShakeSeconds = 0.55f;

    private static readonly string[] FragmentIconPaths =
    [
        "res://assets/images/core/shards/brass_key.png",
        "res://assets/images/core/shards/photo.png",
        "res://assets/images/core/shards/fragment_jar.png",
        "res://assets/images/core/shards/fragment_frame.png",
        "res://assets/images/core/shards/fragment_letter.png",
        "res://assets/images/core/shards/fragment_apple.png",
    ];

    [Export(PropertyHint.File, "*.tscn")]
    public string DestinationScenePath { get; set; } = "res://scenes/world/room_1.tscn";

    [Export]
    public string JourneyId { get; set; } = "Room1ToRoom2";

    private sealed class JourneySpec
    {
        public string TexturePath { get; init; }
        public string[] Lines { get; init; }
        public string[] RepeatLines { get; init; }
        public Vector2 StartFocus { get; init; } = new(576f, 324f);
        public Vector2 Focus { get; init; }
        public float FinalScale { get; init; }
        public Rect2 TargetRect { get; init; }
        public string Prompt { get; init; }
        public float CameraSeconds { get; init; }
        public int FragmentIndex { get; init; } = -1;
        public string MemoryTexturePath { get; init; } = string.Empty;
        public string[] FragmentLines { get; init; } = [];
        public string[] RecoveryLines { get; init; } = [];
        public string OpenSfx { get; init; } = Sfx.DoorCreak;
        public float OpenPitch { get; init; } = 1f;
    }

    private static readonly Dictionary<string, JourneySpec> Specs = new()
    {
        ["Room1ToRoom2"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Door.png",
            Lines =
            [
                "The kitchen lets go behind me.",
                "Straight on. The house is being unusually honest about the way.",
            ],
            RepeatLines = ["Straight on, then. The house puts the same door in front of me again."],
            FragmentLines =
            [
                "The key's still warm in my hand. What are you for, now? A door I already opened shouldn't need carrying.",
                "Morning. Toast. Her voice from the next room—",
            ],
            RecoveryLines =
            [
                "No. Stay with me. Breathe. The door first.",
            ],
            FragmentIndex = 0,
            MemoryTexturePath = "res://assets/images/bgs/Room_1_BG.png",
            Focus = new Vector2(580f, 318f),
            FinalScale = 1.7f,
            TargetRect = new Rect2(372f, 12f, 410f, 620f),
            Prompt = "Click the door to open it",
            CameraSeconds = 3.4f,
        },
        ["Room2ToRoom3"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Left.png",
            Lines =
            [
                "The sitting room goes quiet behind me.",
                "Left, where the runner bends. I used to run this corner too fast.",
            ],
            RepeatLines = ["Left at the runner. The house puts the same door in front of me again."],
            FragmentLines =
            [
                "Her face keeps catching the light. What am I meant to do with you? Look until looking turns into staying?",
                "Two chairs. A page turning. Don't—",
            ],
            RecoveryLines =
            [
                "Not backwards. I can carry her without crawling inside the photograph.",
            ],
            FragmentIndex = 1,
            MemoryTexturePath = "res://assets/images/bgs/Room2_no_objects.png",
            StartFocus = new Vector2(690f, 330f),
            Focus = new Vector2(398f, 240f),
            FinalScale = 1.75f,
            TargetRect = new Rect2(450f, 80f, 250f, 490f),
            Prompt = "Click the door to open it",
            CameraSeconds = 3.8f,
        },
        ["Room3ToRoom4"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Right.png",
            Lines =
            [
                "My steps sound too light. Like socks on boards.",
                "Right at the photographs. Even facing away, they know my steps.",
            ],
            RepeatLines = ["Right at the photographs. The house puts the same door in front of me again."],
            FragmentLines =
            [
                "The jar knocks softly against my fingers. Names. Pieces. Why does this house keep putting that day in my hands?",
                "A windowsill. Pencil letters. Her laugh—",
            ],
            RecoveryLines =
            [
                "Enough. Shake it off, Mara. The next room isn't waiting inside this one.",
            ],
            FragmentIndex = 2,
            MemoryTexturePath = "res://assets/images/bgs/Room_3_BG.png",
            StartFocus = new Vector2(460f, 330f),
            Focus = new Vector2(773f, 255f),
            FinalScale = 1.75f,
            TargetRect = new Rect2(430f, 80f, 290f, 490f),
            Prompt = "Click the door to open it",
            CameraSeconds = 3.8f,
        },
        ["Room6ToPorch"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Empty_Road.png",
            Lines =
            [
                "The gate shuts behind me.",
                "No swing now. No counting. Only the road, and my own steps on it.",
                "It is shorter than I made it in my head. It was always shorter.",
                "There. A light. The porch.",
            ],
            RepeatLines =
            [
                "The road again. Shorter, now that I let myself know it.",
                "There. The porch light.",
            ],
            FragmentLines =
            [
                "The apple bruises where my thumb holds it. Is every piece meant to be carried back to you?",
                "The swing. The gate. Four o'—",
            ],
            RecoveryLines =
            [
                "I know. I know. Walk anyway. She isn't on this road now; I am.",
            ],
            FragmentIndex = 5,
            MemoryTexturePath = "res://assets/images/bgs/Room_6_Final.png",
            Focus = new Vector2(635f, 292f),
            FinalScale = 2.45f,
            TargetRect = new Rect2(490f, 215f, 220f, 155f),
            Prompt = "Click the porch light to arrive",
            CameraSeconds = 6.2f,
            OpenSfx = Sfx.DoorCreak,
            OpenPitch = 0.82f,
        },
    };

    private JourneySpec _spec;
    private Texture2D _texture;
    private Font _font;
    private Label _promptLabel;
    private TextureRect _heldHandRect;
    private TextureRect _heldFragmentRect;
    private Texture2D _memoryTexture;
    private Tween _heldTween;
    private Vector2 _handRestPosition;
    private Vector2 _fragmentRestPosition;

    private bool _readyToOpen;
    private bool _opening;
    private bool _hovering;
    private bool _hoverCursorOwned;
    private bool _reduceMotion;
    private bool _seenBefore;
    private float _cameraSeconds = 3.4f;
    private float _elapsed;
    private float _progress;
    private bool _heldSettled;
    private bool _migraineActive;
    private float _migraineTime;
    private float _recoverShakeTime;
    private float _recoverShakeElapsed;

    private string SeenFlag => $"journey_seen_{JourneyId}";

    public override void _Ready()
    {
        SetProcess(true);
        SetProcessInput(true);

        if (!Specs.TryGetValue(JourneyId ?? string.Empty, out _spec))
        {
            Logger.Warning($"[RoomJourney] Unknown journey id '{JourneyId}'; using Room1ToRoom2 staging.");
            _spec = Specs["Room1ToRoom2"];
        }

        _font = GD.Load<FontFile>(FontPath);
        _texture = GD.Load<Texture2D>(_spec.TexturePath);
        if (_texture == null)
            Logger.Error($"[RoomJourney] Could not load journey background: {_spec.TexturePath}");
        if (!string.IsNullOrWhiteSpace(_spec.MemoryTexturePath))
        {
            _memoryTexture = GD.Load<Texture2D>(_spec.MemoryTexturePath);
            if (_memoryTexture == null)
                Logger.Warning($"[RoomJourney] Could not load memory flicker: {_spec.MemoryTexturePath}");
        }

        _seenBefore = GameState.Instance?.HasFlag(SeenFlag) ?? false;
        _reduceMotion = Settings.ReduceMotionOrDefault();
        _cameraSeconds = _seenBefore ? Math.Min(1.7f, _spec.CameraSeconds) : _spec.CameraSeconds;
        _progress = _reduceMotion ? 1f : 0f;

        BuildHeldFragment();
        BuildPrompt();
        QueueRedraw();
        _ = RunJourneyAsync();
    }

    public override void _ExitTree()
    {
        _heldTween?.Kill();
        ClearHover();
    }

    public override void _Process(double delta)
    {
        if (!GetTree().Paused)
        {
            if (_migraineActive)
            {
                _migraineTime += (float)delta;
                UpdateHeldTremor();
                QueueRedraw();
            }
            if (_recoverShakeTime > 0f)
            {
                _recoverShakeElapsed += (float)delta;
                _recoverShakeTime = Math.Max(0f, _recoverShakeTime - (float)delta);
                QueueRedraw();
            }
        }

        if (!_reduceMotion && !GetTree().Paused && _progress < 1f)
        {
            _elapsed += (float)delta;
            _progress = Mathf.Clamp(_elapsed / Math.Max(_cameraSeconds, 0.01f), 0f, 1f);
            QueueRedraw();
        }

        if (!_readyToOpen || _opening)
        {
            SetHovering(false);
            return;
        }

        bool hover = !GetTree().Paused
            && !Dialogue.IsShowing
            && !Dialogue.IsInputBlocked
            && _spec.TargetRect.HasPoint(GetGlobalMousePosition());
        SetHovering(hover);
        QueueRedraw(); // the ready-door/porch highlight breathes
    }

    public override void _Input(InputEvent @event)
    {
        if (!_readyToOpen || _opening || GetTree().Paused || SceneManager.IsChanging)
            return;
        if (@event is not InputEventMouseButton mouse || !mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Left)
            return;
        if (Dialogue.IsShowing || Dialogue.IsInputBlocked)
            return;

        Vector2 position = GetGlobalMousePosition();
        if (!_spec.TargetRect.HasPoint(position))
            return;

        GetTree().Root.SetInputAsHandled();
        Hotspots.SpawnRipple(position, new Color(1f, 0.88f, 0.55f));
        _ = OpenDestinationAsync();
    }

    public override void _Draw()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152f, 648f));
        if (_texture == null)
        {
            DrawRect(screen, new Color(0.03f, 0.025f, 0.02f));
            return;
        }

        float eased = _progress * _progress * (3f - 2f * _progress); // smoothstep
        float scale = Mathf.Lerp(1f, _spec.FinalScale, eased);
        Vector2 sourceSize = new(1152f / scale, 648f / scale);
        Vector2 focus = _spec.StartFocus.Lerp(_spec.Focus, eased);

        // The migraine is a camera event as much as a visual overlay:
        // small, involuntary source-space jolts while the memory has her,
        // then one deliberate, decaying left-right head shake as she
        // comes back to herself. Reduce Motion keeps only a trace.
        if (_migraineActive)
        {
            float amp = _reduceMotion ? 1.4f : 6f;
            focus += new Vector2(
                (float)Math.Sin(_migraineTime * 71f) * amp,
                (float)Math.Cos(_migraineTime * 59f) * amp * 0.65f);
        }
        else if (_recoverShakeTime > 0f)
        {
            float strength = Mathf.Clamp(_recoverShakeTime / RecoverShakeSeconds, 0f, 1f);
            float amp = (_reduceMotion ? 2f : 11f) * strength;
            focus += new Vector2(
                (float)Math.Sin(_recoverShakeElapsed * 34f) * amp,
                (float)Math.Cos(_recoverShakeElapsed * 27f) * amp * 0.3f);
        }

        // A barely-there walking bob, gone once Mara arrives (or for
        // Reduce Motion). It is in source pixels, so it stays subtle.
        if (!_reduceMotion && _progress < 1f)
            focus.Y += (float)Math.Sin(_elapsed * 5.2f) * 1.6f * (1f - _progress);

        Vector2 sourcePos = new(
            Mathf.Clamp(focus.X - sourceSize.X * 0.5f, 0f, 1152f - sourceSize.X),
            Mathf.Clamp(focus.Y - sourceSize.Y * 0.5f, 0f, 648f - sourceSize.Y));
        var sourceRect = new Rect2(sourcePos, sourceSize);
        DrawTextureRectRegion(_texture, screen, sourceRect, Colors.White);
        DrawMemoryEffects(screen, sourceRect);

        if (_readyToOpen && !_opening)
        {
            float pulse = _reduceMotion
                ? 0.72f
                : 0.62f + 0.22f * (float)Math.Sin(Time.GetTicksMsec() / 210.0);
            DrawRect(_spec.TargetRect, new Color(1f, 0.89f, 0.58f, _hovering ? 0.10f : 0.05f));
            DrawRect(_spec.TargetRect, new Color(1f, 0.89f, 0.58f, _hovering ? 0.95f : pulse), false, 2f);
        }
    }

    private async Task RunJourneyAsync()
    {
        try
        {
            await WaitSeconds(0.45f);
            if (!StillHere())
                return;

            string[] lines = _seenBefore && _spec.RepeatLines is { Length: > 0 }
                ? _spec.RepeatLines
                : _spec.Lines;
            foreach (string line in lines)
            {
                await Say(line, 4.8f, 24);
                if (!StillHere())
                    return;
            }

            while (_progress < 1f)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!StillHere())
                    return;
            }

            await PlayFragmentMigraineAsync();
            if (!StillHere())
                return;

            _readyToOpen = true;
            if (_promptLabel != null)
            {
                _promptLabel.Text = _spec.Prompt;
                _promptLabel.Visible = true;
            }
            QueueRedraw();
        }
        catch (Exception ex)
        {
            // A scene can be freed while the walk/dialogue is suspended.
            Logger.Warning($"[RoomJourney] Journey interrupted: {ex.Message}");
        }
    }

    /// <summary>Draw the brief, involuntary return of the room Mara has
    /// just left: red/cyan separation, hard flickers of that room over the
    /// hallway, a closing red edge, and jagged migraine aura at the top
    /// and bottom of sight. Everything is derived from _migraineTime, so
    /// the effect can run underneath click-to-advance dialogue without a
    /// second timed sequence.</summary>
    private void DrawMemoryEffects(Rect2 screen, Rect2 sourceRect)
    {
        if (_migraineActive)
        {
            float t = _migraineTime;
            float pulse = 0.5f + 0.5f * (float)Math.Sin(t * 8.5f);
            float effectScale = _reduceMotion ? 0.45f : 1f;

            if (!_reduceMotion)
            {
                var redGhost = screen;
                redGhost.Position += new Vector2(5f, 0f);
                DrawTextureRectRegion(_texture, redGhost, sourceRect,
                    new Color(1f, 0.25f, 0.25f, 0.16f));
                var cyanGhost = screen;
                cyanGhost.Position += new Vector2(-5f, 0f);
                DrawTextureRectRegion(_texture, cyanGhost, sourceRect,
                    new Color(0.35f, 0.85f, 1f, 0.13f));
            }

            if (_memoryTexture != null)
            {
                float gate = Math.Abs((float)Math.Sin(t * 10.7f)
                    + 0.45f * (float)Math.Sin(t * 23.1f));
                float alpha = gate > 1.18f ? 0.68f : gate > 0.95f ? 0.32f : 0.05f;
                DrawTextureRect(_memoryTexture, screen, false,
                    new Color(1f, 1f, 1f, alpha * effectScale));
            }

            DrawRect(screen, new Color(0.32f, 0f, 0.04f, (0.07f + 0.04f * pulse) * effectScale));
            float edge = (30f + 16f * pulse) * effectScale;
            var edgeColor = new Color(0.18f, 0f, 0.025f, (0.34f + 0.16f * pulse) * effectScale);
            DrawRect(new Rect2(0f, 0f, 1152f, edge), edgeColor);
            DrawRect(new Rect2(0f, 648f - edge, 1152f, edge), edgeColor);
            DrawRect(new Rect2(0f, 0f, edge, 648f), edgeColor);
            DrawRect(new Rect2(1152f - edge, 0f, edge, 648f), edgeColor);

            var aura = new Color(1f, 0.72f, 0.62f, (0.28f + 0.18f * pulse) * effectScale);
            for (int i = 0; i < 5; i++)
            {
                float x = 130f + i * 220f;
                float topBend = (float)Math.Sin(t * 17f + i * 1.7f) * 22f;
                float topEnd = (float)Math.Cos(t * 13f + i) * 30f;
                DrawLine(new Vector2(x, 0f), new Vector2(x + topBend, 38f), aura, 2f, true);
                DrawLine(new Vector2(x + topBend, 38f), new Vector2(x + topEnd, 74f), aura, 2f, true);

                float bottomBend = (float)Math.Cos(t * 15f + i * 2.1f) * 22f;
                float bottomEnd = (float)Math.Sin(t * 12f + i) * 30f;
                DrawLine(new Vector2(x, 648f), new Vector2(x + bottomBend, 610f), aura, 2f, true);
                DrawLine(new Vector2(x + bottomBend, 610f), new Vector2(x + bottomEnd, 574f), aura, 2f, true);
            }
            return;
        }

        if (_recoverShakeTime > 0f)
        {
            float strength = Mathf.Clamp(_recoverShakeTime / RecoverShakeSeconds, 0f, 1f);
            DrawRect(screen, new Color(0.25f, 0f, 0.03f, 0.10f * strength));
        }
    }

    /// <summary>The pre-open pause. Mara takes the just-recovered fragment
    /// in her palm, wonders what carrying it can possibly be for, and the
    /// room it came from cuts through the hallway in flashes. Then she
    /// pulls herself together — a hard little head shake, a steadier line —
    /// and only after the hand lowers does the journey make its door (or
    /// the porch light) clickable. First arrivals only; shortened repeat
    /// journeys stay shortened.</summary>
    private async Task PlayFragmentMigraineAsync()
    {
        if (_seenBefore || _spec.FragmentIndex < 0 ||
            _spec.FragmentIndex >= FragmentIconPaths.Length ||
            _spec.FragmentLines == null || _spec.FragmentLines.Length == 0)
        {
            return;
        }

        var gs = GameState.Instance;
        if (gs != null && !gs.IsShardRecovered(_spec.FragmentIndex))
            return;

        await ShowHeldFragmentAsync();
        if (!StillHere())
            return;

        await Say(_spec.FragmentLines[0], 5.2f, 23);
        if (!StillHere())
            return;

        if (_spec.FragmentLines.Length > 1)
        {
            try
            {
                _migraineTime = 0f;
                _migraineActive = true;
                AudioManager.Play(Sfx.CorruptionGlitch, -8f, 0.82f);
                AudioManager.Play(Sfx.CorruptionWhisper, -13f, 0.75f);
                QueueRedraw();

                for (int i = 1; i < _spec.FragmentLines.Length; i++)
                {
                    await Say(_spec.FragmentLines[i], 5.2f, 23);
                    if (!StillHere())
                        return;
                }
            }
            finally
            {
                _migraineActive = false;
                ResetHeldPositions();
            }

            _recoverShakeElapsed = 0f;
            _recoverShakeTime = RecoverShakeSeconds;
            AudioManager.Play(Sfx.ClarifyReveal, -10f, 0.85f);
            QueueRedraw();
        }

        if (_spec.RecoveryLines != null)
        {
            foreach (string line in _spec.RecoveryLines)
            {
                await Say(line, 5.2f, 23);
                if (!StillHere())
                    return;
            }
        }

        await HideHeldFragmentAsync();
    }

    private async Task OpenDestinationAsync()
    {
        if (_opening)
            return;
        _opening = true;
        _readyToOpen = false;
        SetHovering(false);
        if (_promptLabel != null)
            _promptLabel.Visible = false;

        GameState.Instance?.SetFlag(SeenFlag);
        AudioManager.Play(_spec.OpenSfx, -7f, _spec.OpenPitch);
        await WaitSeconds(0.3f);
        if (!StillHere())
            return;

        if (string.IsNullOrWhiteSpace(DestinationScenePath))
        {
            Logger.Error("[RoomJourney] DestinationScenePath is empty.");
            _opening = false;
            _readyToOpen = true;
            return;
        }

        SceneManager.ChangeScene(DestinationScenePath);
    }

    private async Task ShowHeldFragmentAsync()
    {
        if (_heldHandRect == null || _heldFragmentRect == null)
            return;

        _heldSettled = false;
        _heldHandRect.Position = _handRestPosition + new Vector2(0f, 190f);
        _heldFragmentRect.Position = _fragmentRestPosition + new Vector2(0f, 190f);
        _heldHandRect.Modulate = new Color(1, 1, 1, 0);
        _heldFragmentRect.Modulate = new Color(1, 1, 1, 0);
        _heldHandRect.Visible = true;
        _heldFragmentRect.Visible = true;
        AudioManager.Play(Sfx.PhotoPickup, -13f, 0.9f);

        if (_reduceMotion)
        {
            ResetHeldPositions();
            _heldHandRect.Modulate = Colors.White;
            _heldFragmentRect.Modulate = Colors.White;
            _heldSettled = true;
            return;
        }

        _heldTween?.Kill();
        _heldTween = CreateTween();
        _heldTween.SetParallel(true);
        _heldTween.TweenProperty(_heldHandRect, "position", _handRestPosition, 0.65f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _heldTween.TweenProperty(_heldFragmentRect, "position", _fragmentRestPosition, 0.65f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _heldTween.TweenProperty(_heldHandRect, "modulate:a", 1f, 0.4f);
        _heldTween.TweenProperty(_heldFragmentRect, "modulate:a", 1f, 0.4f);
        await WaitSeconds(0.7f);
        _heldSettled = StillHere();
    }

    private async Task HideHeldFragmentAsync()
    {
        if (_heldHandRect == null || _heldFragmentRect == null)
            return;

        _heldSettled = false;
        AudioManager.Play(Sfx.ShardPlace, -13f, 0.9f);
        if (_reduceMotion)
        {
            _heldHandRect.Visible = false;
            _heldFragmentRect.Visible = false;
            return;
        }

        _heldTween?.Kill();
        _heldTween = CreateTween();
        _heldTween.SetParallel(true);
        _heldTween.TweenProperty(_heldHandRect, "position",
                _handRestPosition + new Vector2(0f, 190f), 0.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _heldTween.TweenProperty(_heldFragmentRect, "position",
                _fragmentRestPosition + new Vector2(0f, 190f), 0.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _heldTween.TweenProperty(_heldHandRect, "modulate:a", 0f, 0.45f);
        _heldTween.TweenProperty(_heldFragmentRect, "modulate:a", 0f, 0.45f);
        await WaitSeconds(0.5f);
        if (!StillHere())
            return;
        _heldHandRect.Visible = false;
        _heldFragmentRect.Visible = false;
    }

    private void ResetHeldPositions()
    {
        if (_heldHandRect == null || _heldFragmentRect == null)
            return;
        _heldHandRect.Position = _handRestPosition;
        _heldFragmentRect.Position = _fragmentRestPosition;
    }

    private void UpdateHeldTremor()
    {
        if (!_heldSettled || _heldHandRect == null || _heldFragmentRect == null)
            return;
        Vector2 tremor = _reduceMotion
            ? Vector2.Zero
            : new Vector2(
                (float)Math.Sin(_migraineTime * 43f) * 2.2f,
                (float)Math.Cos(_migraineTime * 37f) * 1.6f);
        _heldHandRect.Position = _handRestPosition + tremor;
        _heldFragmentRect.Position = _fragmentRestPosition + tremor;
    }

    private void BuildHeldFragment()
    {
        if (_spec.FragmentIndex < 0 || _spec.FragmentIndex >= FragmentIconPaths.Length)
            return;

        var handTexture = GD.Load<Texture2D>(HandTexturePath);
        var fragmentTexture = GD.Load<Texture2D>(FragmentIconPaths[_spec.FragmentIndex]);
        if (handTexture == null || fragmentTexture == null)
        {
            Logger.Warning("[RoomJourney] Held-fragment art could not be loaded; monologue only.");
            return;
        }

        float handW = 370f;
        float handH = handW * HeldHandAspect;
        _handRestPosition = new Vector2(576f - handW / 2f, 648f - handH * 0.92f);
        Vector2 palm = _handRestPosition + new Vector2(handW * HeldPalmX, handH * HeldPalmY);
        _fragmentRestPosition = palm - new Vector2(150f, 150f);

        _heldHandRect = new TextureRect
        {
            Name = "JourneyHeldHand",
            Texture = handTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(handW, handH),
            Position = _handRestPosition + new Vector2(0f, 190f),
            Visible = false,
            Modulate = new Color(1, 1, 1, 0),
            ZIndex = 2450,
        };
        AddChild(_heldHandRect);

        _heldFragmentRect = new TextureRect
        {
            Name = "JourneyHeldFragment",
            Texture = fragmentTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(300f, 300f),
            PivotOffset = new Vector2(150f, 150f),
            Scale = new Vector2(0.55f, 0.55f),
            Position = _fragmentRestPosition + new Vector2(0f, 190f),
            Visible = false,
            Modulate = new Color(1, 1, 1, 0),
            ZIndex = 2460,
        };
        AddChild(_heldFragmentRect);
    }

    private void BuildPrompt()
    {
        _promptLabel = new Label
        {
            Text = string.Empty,
            Position = new Vector2(276f, 500f),
            Size = new Vector2(600f, 40f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ZIndex = 2600,
        };
        if (_font != null)
            _promptLabel.AddThemeFontOverride("font", _font);
        _promptLabel.AddThemeFontSizeOverride("font_size", 20);
        _promptLabel.AddThemeColorOverride("font_color", new Color(0.97f, 0.93f, 0.84f));

        var chip = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.035f, 0.03f, 0.82f),
            BorderColor = new Color(0.78f, 0.66f, 0.46f, 0.45f),
        };
        chip.SetBorderWidthAll(1);
        chip.SetCornerRadiusAll(4);
        chip.ContentMarginLeft = 8;
        chip.ContentMarginRight = 8;
        chip.ContentMarginTop = 4;
        chip.ContentMarginBottom = 4;
        _promptLabel.AddThemeStyleboxOverride("normal", chip);
        AddChild(_promptLabel);
    }

    private void SetHovering(bool hover)
    {
        if (_hovering == hover)
            return;
        _hovering = hover;

        if (_hovering)
        {
            CursorHelper.BeginHover(this, PointerCursorPath, 32, 32);
            _hoverCursorOwned = true;
            AudioManager.Play(Sfx.UiHover, -13f);
        }
        else
        {
            ClearHover();
        }
        QueueRedraw();
    }

    private void ClearHover()
    {
        if (_hoverCursorOwned)
        {
            CursorHelper.EndHover(this);
            _hoverCursorOwned = false;
        }
        _hovering = false;
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();

    private async Task Say(string text, float duration = 4.5f, int fontSize = 24)
    {
        if (!StillHere() || string.IsNullOrWhiteSpace(text))
            return;
        await Dialogue.ShowText(this, text, duration, 576f, 324f, FontPath, fontSize);
    }

    private async Task WaitSeconds(float seconds)
    {
        if (!IsInsideTree() || seconds <= 0f)
            return;
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}

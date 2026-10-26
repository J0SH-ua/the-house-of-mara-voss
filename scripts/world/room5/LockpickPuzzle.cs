using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Her father's trade, remembered with Mara hands — played the way
/// Dying Light taught a generation to feel a lock.
///
/// The pick is locked to the mouse: it follows it around the top of
/// the lock exactly, no sliding, no drift, no mouse button involved.
/// Holding A (or D) turns the cylinder. How far the cylinder turns
/// before the pick starts to shake is the whole secret — barely
/// turning means the pick is far from the pins' truth; turning most
/// of the way means it is close. Let go and the unset part springs
/// back; reseat the pick a breath sideways and turn again.
///
/// Each lock is a sequence of pin layers with its own hidden sweet
/// spot. A layer turned all the way home sets with the unlocked
/// click of a little lock inside the lock, and stays set; deeper
/// layers sit narrower. And the picks *break*: the roll holds
/// seven, forcing a bind until the trembling tops out snaps one,
/// and every snap costs Mara a little more of her nerve. Break
/// them all and the panic takes the room — unless she has Clarity
/// saved to spend the whole moment away and wake back at the
/// flashlight, remembering nothing. That is what saving it is for.
///
/// The scene serves both locks in the under-house chain. Choice of
/// target arrives through saved transient flags (with a progress-based
/// fallback): the Room 5 vent latch, or the harder little trunk in the
/// basement. Success, failure and the drawn "put the tools away" control
/// all return to the place the lock belongs.
/// </summary>
public partial class LockpickPuzzle : Node2D
{
    private const string BackgroundPath = "res://assets/images/bgs/Lockpick_BG.png";
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    private const string Room5ScenePath = "res://scenes/world/room_5.tscn";
    private const string VentCrawlScenePath = "res://scenes/world/vent_crawl.tscn";
    private const string BasementScenePath = "res://scenes/world/basement.tscn";
    private const float MaxPickDegrees = 78f;
    private const float PlugOpenDegrees = 90f;
    private const float TurnSpeedDegreesPerSecond = 62f;
    private const float SpringBackDegreesPerSecond = 110f;

    private enum PickState
    {
        Intro,
        Picking,
        Broken,
        Panic,
        Rewinding,
        Success,
        Leaving,
    }

    private static readonly Rect2 LeaveRect = new(16, 468, 216, 44);
    private static readonly Vector2 LockCenter = new(576, 324);
    private static readonly string[] NearLines =
    [
        "There. Did you hear that? The little clean sound. Stay there.",
        "That's not the knock. That's the pins. Easy. Easy.",
    ];

    // One for each of the seven, in the order they go. The seventh
    // has no line here — the panic speaks for it.
    private static readonly string[] SnapLines =
    [
        "—the pick. It— no. No no. Dad's pick just snapped in my hands. Forty years he's had these. Okay. Okay. There are more in the roll. There are more.",
        "Another one. My hands won't— stop shaking first, then pick. That's the order, Mara. Hands first.",
        "Three. I'm forcing it. He told me never to push a sound I didn't like and I pushed it anyway—",
        "Four gone. The whole of my childhood fits in this roll and I'm spending it like loose change. Breathe. Breathe.",
        "Five. They feel like they're breaking easier. They aren't. It's not the steel getting weaker. It's the hands.",
        "Six— God. One left. One. If this one goes I can't open anything ever again in this house— stop. Stop. Slow down. His voice. Slow.",
    ];

    private static readonly Rect2 RewindRect = new(216, 468, 350, 54);
    private static readonly Rect2 LetGoRect = new(586, 468, 350, 54);
    private const float PanicSeconds = 10f;
    private const string KitchenCabinetScenePath = "res://scenes/world/kitchen_cabinet.tscn";

    // The screen's own glass, giving up while the choice runs out.
    private static readonly Vector2[][] PanicCracks =
    [
        [new Vector2(576, -8), new Vector2(548, 96), new Vector2(590, 190), new Vector2(552, 290), new Vector2(588, 392), new Vector2(566, 500)],
        [new Vector2(-8, 260), new Vector2(110, 290), new Vector2(206, 262), new Vector2(306, 298), new Vector2(398, 274)],
        [new Vector2(1160, 380), new Vector2(1040, 356), new Vector2(944, 390), new Vector2(850, 362), new Vector2(764, 394)],
        [new Vector2(240, 656), new Vector2(284, 546), new Vector2(252, 448), new Vector2(296, 356)],
    ];

    private Texture2D _background;
    private FontFile _font;
    private Label _titleLabel;
    private Label _helpLabel;
    private Label _statusLabel;
    private Label _leaveLabel;

    private bool _targetTrunk;
    private bool _hasFineWrench;
    private float[] _layerSweetDegrees = System.Array.Empty<float>();
    private float[] _layerEnds = System.Array.Empty<float>();
    private float[] _layerTolerances = System.Array.Empty<float>();
    private float[] _layerStressScales = System.Array.Empty<float>();
    private int _layerIndex;
    private float _pickDegrees;
    private float _plugDegrees;
    // Instantaneous strain — the trembling itself. Rises fast while
    // a bind is forced, drains the moment she lets go. It drives
    // the shake and the sound, and nothing else. There is no
    // gauge for it anywhere on screen.
    private float _stress;
    // The Dying Light truth of the thing, kept hidden: every
    // second spent forcing a binding pick does PERMANENT damage to
    // that pick (worse the farther from the sweet spot it binds),
    // and letting go only spares it further damage — it never
    // un-bends steel. At 1.0 the pick snaps. The only instruments
    // Mara gets are the worsening tremble and the changing sound.
    private float _pickDamage;
    private bool _binding;
    private int _lastPickTickBand;
    private int _lastPlugTickStep;
    private int _nearLineLayer = -1;
    private ulong _lastBindSoundMsec;
    private bool _leaveCursorOwned;
    private PickState _state = PickState.Intro;
    private float _panicLeft;
    private float _panicRumbleTimer;
    private bool _panicDecided;
    // The snap's red weather: a hot flash at the edges of her
    // vision the moment steel goes, cooling over about a second.
    private float _snapFlashLeft;
    // The rewind, played backwards for her eyes: grey strobing,
    // cracks un-walking, picks mending, her own words reversing.
    private const float RewindSeconds = 2.6f;
    private float _rewindT;
    private int _rewindSnippet = -1;
    private float _rewindSnippetTimer;
    private Label _rewindTextLabel;
    private Label _rewindLabel;
    private Label _letGoLabel;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _background = GD.Load<Texture2D>(BackgroundPath);
        _font = GD.Load<FontFile>(FontPath);

        var gs = GameState.Instance;
        _targetTrunk = gs != null &&
            (gs.HasFlag("lockpick_target_trunk") ||
             (!gs.HasFlag("lockpick_target_vent") && gs.HasFlag("room5_vent_open")));
        _hasFineWrench = gs?.HasFlag("item_fine_tension_wrench") ?? false;
        _rng.Randomize();
        BuildLayers();
        _lastPickTickBand = Mathf.RoundToInt(_pickDegrees / 4f);

        // A is also Anchor elsewhere in the house. Here it belongs to the
        // pick alone; verb/clarity modes do not enter the lock with her.
        MemoryVerbSystem.Cancel(silent: true);
        ClaritySystem.Disarm(silent: true);

        BuildLabels();
        QueueRedraw();
        _ = PlayIntroAsync();
    }

    private void BuildLayers()
    {
        if (_targetTrunk)
        {
            _layerEnds = [18f, 36f, 54f, 72f, PlugOpenDegrees];
            _layerTolerances = [11f, 9f, 7.5f, 6f, 5f];
            _layerStressScales = [1f, 1.08f, 1.16f, 1.24f, 1.34f];
        }
        else
        {
            _layerEnds = [30f, 60f, PlugOpenDegrees];
            _layerTolerances = [13f, 10f, 8f];
            _layerStressScales = [0.9f, 1f, 1.12f];
        }

        _layerSweetDegrees = new float[_layerEnds.Length];
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        float previousSweet = 0f;
        for (int i = 0; i < _layerSweetDegrees.Length; i++)
        {
            float candidate = rng.RandfRange(-58f, 58f);
            int attempts = 0;
            // Consecutive layers should feel like different pins, not the
            // same fortunate wrist angle wearing a new number.
            while (i > 0 && Mathf.Abs(candidate - previousSweet) < 24f && attempts < 24)
            {
                candidate = rng.RandfRange(-58f, 58f);
                attempts++;
            }
            _layerSweetDegrees[i] = candidate;
            previousSweet = candidate;
        }
    }

    private int LayerCount => _layerEnds.Length;
    private float CurrentLayerStart => _layerIndex <= 0 ? 0f : _layerEnds[_layerIndex - 1];
    private float CurrentLayerEnd => _layerIndex < LayerCount ? _layerEnds[_layerIndex] : PlugOpenDegrees;
    private float CurrentSweetDegrees => _layerIndex < _layerSweetDegrees.Length
        ? _layerSweetDegrees[_layerIndex]
        : 0f;
    private float CurrentToleranceDegrees
    {
        get
        {
            float tolerance = _layerIndex < _layerTolerances.Length
                ? _layerTolerances[_layerIndex]
                : 5f;
            // The Room 5 upgrade is deliberately modest: pins read about
            // a degree sooner, it does not pick the lock for her.
            return tolerance + (_hasFineWrench ? 1f : 0f);
        }
    }
    private float CurrentStressScale
    {
        get
        {
            float scale = _layerIndex < _layerStressScales.Length
                ? _layerStressScales[_layerIndex]
                : 1.2f;
            return scale * (_hasFineWrench ? 0.82f : 1f);
        }
    }

    private void BuildLabels()
    {
        _titleLabel = MakeLabel(
            _targetTrunk ? "The little trunk's lock" : "The vent latch",
            new Vector2(326, 18), new Vector2(500, 38), 29,
            new Color(0.96f, 0.9f, 0.76f, 0.97f));
        _helpLabel = MakeLabel(
            "Mouse — seat the pick      Hold A — turn the lock. How far it turns before it shakes tells the truth.\nNo gauge for the steel — only the shake and the sound. Force a bind and the damage stays in the pick until it snaps. She has seven.",
            new Vector2(256, 58), new Vector2(640, 62), 19,
            new Color(0.9f, 0.86f, 0.76f, 0.94f));
        _statusLabel = MakeLabel(string.Empty,
            new Vector2(326, 560), new Vector2(500, 42), 21,
            new Color(0.94f, 0.9f, 0.8f, 0.97f));
        _leaveLabel = MakeLabel("Put the tools away",
            LeaveRect.Position, LeaveRect.Size, 20,
            new Color(0.95f, 0.88f, 0.72f, 0.97f));
        _rewindLabel = MakeLabel(string.Empty,
            RewindRect.Position, RewindRect.Size, 21,
            new Color(0.97f, 0.93f, 0.82f, 0.98f));
        _letGoLabel = MakeLabel("Let it take me",
            LetGoRect.Position, LetGoRect.Size, 21,
            new Color(0.94f, 0.88f, 0.78f, 0.98f));
        _rewindLabel.Visible = false;
        _letGoLabel.Visible = false;
        // Her own evening, coming back at her backwards.
        _rewindTextLabel = MakeLabel(string.Empty,
            new Vector2(176, 140), new Vector2(800, 90), 26,
            new Color(0.94f, 0.94f, 0.94f, 0.98f));
        _rewindTextLabel.ZIndex = 300;
        _rewindTextLabel.Visible = false;
    }

    private Label MakeLabel(string text, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            Size = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 100,
        };
        if (_font != null)
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(label);
        return label;
    }

    private async Task PlayIntroAsync()
    {
        Dialogue.BeginInputBlock();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!StillHere())
                return;

            var gs = GameState.Instance;
            if (gs != null && !gs.HasFlag("item_lockpick_set"))
            {
                await Say("A lock is only a question if you brought the grammar for it. I did not.");
                if (StillHere())
                    ReturnFromLock();
                return;
            }

            if (_targetTrunk)
            {
                await Say("Small lock. Old lock. Five layers deep, if my fingers are reading it right. The kind Dad called honest — which meant it would tell you exactly where you were wrong, one pin at a time.");
            }
            else
            {
                await Say("Pick across the pins. Wrench under it. Three layers in this latch, each deeper than the last. Dad's voice, from over my shoulder: don't push a sound you don't like.");
            }
            if (!StillHere())
                return;
            await Say("Seven picks in the roll. He oiled them like they were alive, and they'll take a lot — but force one while it's binding and even his steel remembers it can break. So: don't push a sound I don't like. His words. I'm going to try and deserve them.");
            if (!StillHere())
                return;

            if (gs != null && gs.PicksLeft <= 0)
            {
                // A gathered memory gathers the small breakages too;
                // if she has somehow arrived with an empty roll, the
                // house has already put it back together for her.
                gs.RestoreAllPicks("the roll was whole again when she opened it");
                await Say("Seven. All seven lying whole in their pockets, like the breaking got gathered back along with everything else. …All right. I won't waste that twice.");
                if (!StillHere())
                    return;
            }
            else if (gs != null && gs.BrokenPickCount > 0)
            {
                await Say($"{gs.PicksLeft} sound picks left in the roll. The broken ones are still in their pockets. I keep them there so my hands remember what forcing costs.");
                if (!StillHere())
                    return;
            }

            _pickDamage = 0f;
            _state = PickState.Picking;
            UpdateStatus();
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Consume A/D before GameUI can read A as Anchor.
        if (@event is InputEventKey keyEvent &&
            (keyEvent.PhysicalKeycode == Key.A || keyEvent.PhysicalKeycode == Key.D))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Picking itself needs no mouse button at all: _Process reads the
        // mouse position (the pick) and the A/D keys (the turn) directly.
        // This handler exists only for the drawn "put the tools away"
        // control. Waiting for unhandled input keeps Bag, Journal, Pause
        // and navigation ordinary buttons over the lock close-up.
        if (_state == PickState.Panic)
        {
            if (GetTree().Paused || Dialogue.IsShowing || _panicDecided)
                return;
            if (@event is InputEventMouseButton panicClick &&
                panicClick.ButtonIndex == MouseButton.Left && panicClick.Pressed)
            {
                if (RewindRect.HasPoint(panicClick.Position))
                {
                    GetViewport().SetInputAsHandled();
                    _ = ChooseRewindAsync();
                }
                else if (LetGoRect.HasPoint(panicClick.Position))
                {
                    GetViewport().SetInputAsHandled();
                    _panicDecided = true;
                    _ = FallToShatterAsync();
                }
            }
            return;
        }

        if (_state != PickState.Picking || GetTree().Paused || Dialogue.IsShowing)
            return;

        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed &&
            LeaveRect.HasPoint(mouseButton.Position))
        {
            GetViewport().SetInputAsHandled();
            _ = LeaveAsync();
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateCursor();

        if (_snapFlashLeft > 0f)
            _snapFlashLeft = Mathf.Max(0f, _snapFlashLeft - dt);

        // Time running backwards has its own weather and ignores
        // every other rule in the room for its 2.6 seconds.
        if (_state == PickState.Rewinding)
        {
            _rewindT = Mathf.Clamp(_rewindT + dt / RewindSeconds, 0f, 1f);
            _plugDegrees = Mathf.MoveToward(_plugDegrees, 0f, 150f * dt);
            _rewindSnippetTimer -= dt;
            if (_rewindSnippetTimer <= 0f && _rewindT < 1f)
            {
                _rewindSnippetTimer = Settings.ReduceMotionOrDefault() ? 0.5f : 0.3f;
                _rewindSnippet++;
                if (_rewindTextLabel != null)
                {
                    _rewindTextLabel.Text = RewindSnippetText(_rewindSnippet);
                    _rewindTextLabel.Visible = true;
                }
                // Tape-sounds: ticks racing up as the moment unspools.
                AudioManager.Play(Sfx.LockpickTick, -13f, 0.55f + 0.09f * _rewindSnippet);
                if (_rewindSnippet % 3 == 2)
                    AudioManager.Play(Sfx.WindGust, -18f, 1.15f + 0.06f * _rewindSnippet);
            }
            QueueRedraw();
            return;
        }

        bool blocked = GetTree().Paused || Dialogue.IsShowing || Dialogue.IsInputBlocked ||
            (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());

        // The panic runs its own ten seconds while the screen goes.
        if (_state == PickState.Panic && !blocked && !_panicDecided)
        {
            _panicLeft -= dt;
            _panicRumbleTimer -= dt;
            if (_panicRumbleTimer <= 0f)
            {
                _panicRumbleTimer = _rng.RandfRange(0.9f, 1.6f);
                AudioManager.Play(Sfx.CrumbleRumble, -9f, _rng.RandfRange(0.75f, 0.95f));
                AudioManager.Play(Sfx.ScreenCrack3, -13f, _rng.RandfRange(0.8f, 1.1f));
            }
            if (_panicLeft <= 0f)
            {
                _panicDecided = true;
                _ = FallToShatterAsync();
            }
            QueueRedraw();
            return;
        }

        if (_state != PickState.Picking || blocked)
        {
            _binding = false;
            QueueRedraw();
            return;
        }

        // The pick IS the mouse: it sits wherever around the top of the
        // lock the hand is pointing, every frame, exactly. Nothing is
        // accumulated, so nothing can feel slippery.
        Vector2 mouse = GetGlobalMousePosition();
        Vector2 fromLock = mouse - LockCenter;
        if (fromLock.LengthSquared() > 4f)
        {
            float pointing = Mathf.RadToDeg(Mathf.Atan2(fromLock.X, -fromLock.Y));
            _pickDegrees = Mathf.Clamp(pointing, -MaxPickDegrees, MaxPickDegrees);
            int band = Mathf.RoundToInt(_pickDegrees / 4f);
            if (band != _lastPickTickBand)
            {
                _lastPickTickBand = band;
                PlayPickTick();
            }
        }

        // Holding A (or D) turns the cylinder, Dying-Light fashion: it
        // travels until the wrong pins catch it, and how far it got is
        // the information. In the sweet spot it runs all the way home
        // to this layer's gate. Let go and the unset layer springs back.
        bool turning = Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.D);
        float error = PickError();
        float tolerance = CurrentToleranceDegrees;

        if (turning && _layerIndex < LayerCount)
        {
            float limit = error <= tolerance ? CurrentLayerEnd : BindLimitFor(error, tolerance);
            _plugDegrees = Mathf.MoveToward(_plugDegrees, limit, TurnSpeedDegreesPerSecond * dt);
            _binding = error > tolerance && _plugDegrees >= limit - 0.05f;

            if (_binding)
            {
                // Strain is the moment; damage is the memory. The
                // pick trembles now, and keeps every injury: the
                // farther past tolerance it is being forced, the
                // faster the steel gives up its future. Nothing on
                // screen counts this. Her hands are the instrument.
                _stress = Mathf.MoveToward(_stress, 1f, 2.2f * CurrentStressScale * dt);
                float extraError = Mathf.Max(0f, error - tolerance);
                _pickDamage += dt * CurrentStressScale * (0.42f + 0.018f * extraError);
                ulong nowBind = Time.GetTicksMsec();
                if (nowBind - _lastBindSoundMsec > 120)
                {
                    _lastBindSoundMsec = nowBind;
                    // The sound climbs and thins as the pick's hidden
                    // hurt grows — the only meter she gets.
                    AudioManager.Play(Sfx.LockpickTick,
                        -13f + 3f * _pickDamage, 0.6f + 0.25f * _pickDamage);
                }
            }
            else
            {
                _stress = Mathf.MoveToward(_stress, 0f, 1.8f * dt);
            }

            // Bent past what bent steel forgives, his pick
            // remembers what steel does.
            if (_binding && _pickDamage >= 1f)
            {
                SnapPick();
                return;
            }

            if (error <= tolerance)
            {
                float layerSpan = Mathf.Max(1f, CurrentLayerEnd - CurrentLayerStart);
                float layerProgress = (_plugDegrees - CurrentLayerStart) / layerSpan;
                if (_nearLineLayer != _layerIndex && layerProgress > 0.45f)
                {
                    _nearLineLayer = _layerIndex;
                    GameState.Instance?.Toast(NearLines[_targetTrunk ? 1 : 0]);
                }
                if (_plugDegrees >= CurrentLayerEnd - 0.05f)
                    SetCurrentLayer();
            }
        }
        else
        {
            _binding = false;
            _plugDegrees = Mathf.MoveToward(_plugDegrees, CurrentLayerStart, SpringBackDegreesPerSecond * dt);
            _stress = Mathf.MoveToward(_stress, 0f, 2.6f * dt);
        }

        int plugStep = Mathf.FloorToInt(_plugDegrees / 6f);
        if (plugStep != _lastPlugTickStep)
        {
            _lastPlugTickStep = plugStep;
            if (turning)
                AudioManager.Play(Sfx.LockpickTick, -15f, 0.82f + plugStep * 0.025f);
        }

        UpdateStatus();
        QueueRedraw();
    }

    /// <summary>How far this layer lets the cylinder turn with the pick
    /// where it is: nearly-right almost reaches the gate, far-wrong
    /// knocks almost at once. A bind never crosses an unset gate.</summary>
    private float BindLimitFor(float error, float tolerance)
    {
        float layerStart = CurrentLayerStart;
        float layerSpan = CurrentLayerEnd - layerStart;
        float travelFraction = 1f - Mathf.Clamp((error - tolerance) / 62f, 0f, 1f);
        return layerStart + Mathf.Clamp(
            layerSpan * travelFraction, 4f, Mathf.Max(4f, layerSpan - 3f));
    }


    private void SetCurrentLayer()
    {
        if (_state != PickState.Picking || _layerIndex >= LayerCount)
            return;

        int completed = _layerIndex + 1;
        _plugDegrees = CurrentLayerEnd;
        _stress = 0f;
        _lastPlugTickStep = Mathf.FloorToInt(_plugDegrees / 6f);
        // Every set layer clicks like the little lock it is — the
        // unlocked sound of one more depth giving way.
        AudioManager.Play(Sfx.LockOpen, -8f, 0.95f + completed * 0.07f);
        AudioManager.Play(Sfx.LockpickTick, -12f, 1.3f);

        if (completed >= LayerCount)
        {
            _ = SucceedAsync();
            return;
        }

        _layerIndex = completed;
        GameState.Instance?.Toast(
            $"{Ordinal(completed)} layer set. The next pins sit deeper.");
        UpdateStatus();
        QueueRedraw();
    }

    private static string Ordinal(int value) => value switch
    {
        1 => "First",
        2 => "Second",
        3 => "Third",
        4 => "Fourth",
        5 => "Fifth",
        _ => value.ToString(),
    };

    private float PickError() => Mathf.Abs(_pickDegrees - CurrentSweetDegrees);

    private void PlayPickTick()
    {
        float closeness = 1f - Mathf.Clamp(PickError() / 70f, 0f, 1f);
        AudioManager.Play(
            Sfx.LockpickTick,
            -18f + closeness * 5f,
            0.76f + closeness * 0.42f);
    }

    // ------------------------------------------------------- the breaking

    private void SnapPick()
    {
        if (_state != PickState.Picking)
            return;
        _state = PickState.Broken;
        _binding = false;
        _stress = 0f;
        _plugDegrees = CurrentLayerStart;
        // The panic shows on the screen before it reaches her
        // mouth: red at the edges of the world, then the words.
        _snapFlashLeft = 0.9f;

        var gs = GameState.Instance;
        gs?.RegisterPickBroken();
        int broken = gs?.BrokenPickCount ?? 1;
        AudioManager.Play(Sfx.ScreenCrack1, -4f, 1.1f);
        AudioManager.Play(Sfx.LockpickTick, -6f, 0.45f);
        // Every snap is a small panic, and the house keeps count.
        gs?.AddCorruption(2);
        UpdateStatus();
        QueueRedraw();
        _ = SnapBeatAsync(broken);
    }

    private async Task SnapBeatAsync(int broken)
    {
        Dialogue.BeginInputBlock();
        try
        {
            await WaitSeconds(0.45f);
            if (!StillHere())
                return;

            var gs = GameState.Instance;
            if ((gs?.PicksLeft ?? 1) <= 0)
            {
                BeginPanic();
                return;
            }

            await Say(SnapLines[Mathf.Min(broken - 1, SnapLines.Length - 1)]);
            if (!StillHere())
                return;
            // A fresh pick out of the roll: unbent steel, whatever
            // her hands have learned about the price.
            _pickDamage = 0f;
            _state = PickState.Picking;
            UpdateStatus();
            QueueRedraw();
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    /// <summary>Seven gone. The room starts coming apart at the
    /// glass, and she has ten seconds to decide what her saved
    /// Clarity was for.</summary>
    private void BeginPanic()
    {
        _state = PickState.Panic;
        _panicLeft = PanicSeconds;
        _panicRumbleTimer = 0.2f;
        _panicDecided = false;
        AudioManager.Play(Sfx.ScreenCrack2, -3f, 0.8f);
        AudioManager.Play(Sfx.CrumbleRumble, -6f, 0.8f);
        Dialogue.Gust(2.2f, 1.1f);

        var gs = GameState.Instance;
        int clarity = gs?.ClarityCount ?? 0;
        if (_titleLabel != null)
            _titleLabel.Text = "Seven. All seven.";
        if (_helpLabel != null)
        {
            _helpLabel.Text = clarity > 0
                ? "Her breath goes. The glass goes. Everything goes — unless the Clarity she saved can buy the whole moment back."
                : "Her breath goes. The glass goes. And there is no Clarity left to buy any of it back with.";
        }
        if (_rewindLabel != null)
        {
            _rewindLabel.Text = clarity > 0
                ? $"Spend all my Clarity ({clarity}) — go back"
                : "No Clarity left to spend";
            _rewindLabel.Visible = true;
        }
        if (_letGoLabel != null)
            _letGoLabel.Visible = true;
        if (_leaveLabel != null)
            _leaveLabel.Visible = false;
        UpdateStatus();
        QueueRedraw();
        _ = PanicCryAsync();
    }

    private async Task PanicCryAsync()
    {
        // Short, and said over the counting — the ten seconds do
        // not politely wait for her to finish the sentence.
        await Say("No— no no no, not the last one, not Dad's last— I can't open anything, I'm stuck in the walls of this—");
    }

    private async Task ChooseRewindAsync()
    {
        if (_panicDecided || _state != PickState.Panic)
            return;
        var gs = GameState.Instance;
        if (gs == null || gs.ClarityCount <= 0)
        {
            GameState.Instance?.Toast("Nothing left to spend. That's why it mattered to keep some.");
            return;
        }
        _panicDecided = true;
        Dialogue.BeginInputBlock();
        try
        {
            if (_rewindLabel != null) _rewindLabel.Visible = false;
            if (_letGoLabel != null) _letGoLabel.Visible = false;
            AudioManager.Play(Sfx.WordChime, -7f, 0.7f);
            AudioManager.Play(Sfx.BreathSlow, -9f, 0.8f);
            await Say("Back. Take it all — every clear thought I've been saving, take them — just give me back the bit where my hands hadn't—");
            if (!StillHere())
                return;

            // And it goes back WHERE SHE CAN SEE IT GO: the room
            // drains to black-and-white and strobes like tape
            // screaming through a VCR, the cracks un-walk the
            // glass, the roll mends pick by pick, the cylinder
            // turns itself un-done — and her own voice runs past
            // her ears in reverse, word over word, too fast to hold.
            _state = PickState.Rewinding;
            _rewindT = 0f;
            _rewindSnippet = -1;
            _rewindSnippetTimer = 0f;
            AudioManager.Play(Sfx.WindGust, -9f, 1.3f);
            await WaitSeconds(RewindSeconds);
            if (!StillHere())
                return;
            if (_rewindTextLabel != null)
                _rewindTextLabel.Visible = false;
            AudioManager.Play(Sfx.BreathSlow, -9f, 0.9f);

            gs.SpendAllClarityForRewind();
            gs.RestoreAllPicks("time rewound before the breaking");
            ClearTargetFlags(gs);
            // She does not remember the attempt. That is the mercy
            // and the price of it. The cabinet will ask her the
            // rest ("I feel like I've done this before…").
            gs.SetFlag("lockpick_rewound", save: false);
            gs.SaveGame("clarity rewind from the lock");
            SceneManager.ChangeScene(KitchenCabinetScenePath);
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private async Task FallToShatterAsync()
    {
        if (_state != PickState.Panic)
            return;
        _state = PickState.Leaving;
        Dialogue.BeginInputBlock();
        try
        {
            if (_rewindLabel != null) _rewindLabel.Visible = false;
            if (_letGoLabel != null) _letGoLabel.Visible = false;
            AudioManager.Play(Sfx.ScreenCrack1, -3f, 0.8f);
            await Say("—I can't— Dad, I can't— the walls—");
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
        if (!StillHere())
            return;

        // The memory does what strained memories do in this house.
        // It shatters, it is gathered — and the gathering puts her
        // father's roll back together too (GameState sees to it),
        // so the lock can be faced again rather than never.
        GameState.Instance?.AddCorruption(GameState.ShatterThreshold);
    }

    private async Task SucceedAsync()
    {
        if (_state != PickState.Picking)
            return;

        _state = PickState.Success;
        _plugDegrees = PlugOpenDegrees;
        _stress = 0f;
        AudioManager.Play(Sfx.LockpickTick, -9f, 1.22f);
        AudioManager.Play(Sfx.DoorUnlock, -6f, _targetTrunk ? 0.92f : 1.05f);
        UpdateStatus();
        QueueRedraw();

        Dialogue.BeginInputBlock();
        try
        {
            await WaitSeconds(0.4f);
            if (!StillHere())
                return;

            var gs = GameState.Instance;
            if (_targetTrunk)
            {
                if (gs != null)
                {
                    gs.SetFlag("basement_trunk_open", save: false);
                    ClearTargetFlags(gs);
                    gs.SaveGame("basement trunk picked");
                }
                await Say("There. A small, honest click. Now let's see what the house kept under itself.");
                if (StillHere())
                    SceneManager.ChangeScene(BasementScenePath);
            }
            else
            {
                if (gs != null)
                {
                    gs.SetFlag("room5_vent_open", save: false);
                    ClearTargetFlags(gs);
                    gs.SaveGame("room5 vent picked");
                }
                bool hasLight = gs?.HasFlag("item_flashlight") ?? false;
                await Say(hasLight
                    ? "The latch sighs open. I was listening after all. Flashlight on, then."
                    : "The latch sighs open. I was listening after all. Though that dark wants a light before it wants me.");
                if (StillHere())
                    SceneManager.ChangeScene(hasLight ? VentCrawlScenePath : Room5ScenePath);
            }
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private async Task LeaveAsync()
    {
        if (_state != PickState.Picking)
            return;
        _state = PickState.Leaving;
        AudioManager.Play(Sfx.PageTurn, -10f, 0.82f);
        Dialogue.BeginInputBlock();
        try
        {
            await Say(_targetTrunk
                ? "Tools away. The trunk can keep its counsel a while longer."
                : "Tools away. A latch isn't a memory. It will still be a latch when I come back.");
            if (StillHere())
                ReturnFromLock();
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private void ReturnFromLock()
    {
        SceneManager.ChangeScene(_targetTrunk ? BasementScenePath : Room5ScenePath);
    }

    private static void ClearTargetFlags(GameState gs)
    {
        gs.ClearFlag("lockpick_target_vent", save: false);
        gs.ClearFlag("lockpick_target_trunk", save: false);
        gs.ClearFlag("lockpick_origin_room5", save: false);
        gs.ClearFlag("lockpick_origin_basement", save: false);
    }

    private void UpdateStatus()
    {
        if (_statusLabel == null)
            return;
        string toolNote = _hasFineWrench ? " · fine wrench" : string.Empty;
        int picks = GameState.Instance?.PicksLeft ?? GameState.LockpickTotal;
        _statusLabel.Text = _state switch
        {
            PickState.Success => "It turns.",
            PickState.Panic => "—",
            PickState.Rewinding => "—",
            _ when _binding && _pickDamage > 0.78f => "That thin sound. That is the sound steel makes last. LET GO.",
            _ when _binding && _pickDamage > 0.5f => "The steel has started to sound tired. Let go. Breathe. Bent doesn't un-bend.",
            _ when _binding => "It binds there. Let go, breathe, try a hair to one side.",
            _ => $"Layer {Mathf.Min(_layerIndex + 1, LayerCount)} of {LayerCount} · {_layerIndex} set{toolNote} · {picks} of {GameState.LockpickTotal} picks sound",
        };
    }

    private void UpdateCursor()
    {
        bool overLeave = _state == PickState.Picking &&
            !GetTree().Paused && !Dialogue.IsShowing &&
            LeaveRect.HasPoint(GetGlobalMousePosition());
        if (overLeave && !_leaveCursorOwned)
        {
            CursorHelper.BeginHover(this, PointerCursorPath);
            _leaveCursorOwned = true;
        }
        else if (!overLeave && _leaveCursorOwned)
        {
            CursorHelper.EndHover(this);
            _leaveCursorOwned = false;
        }
    }

    public override void _ExitTree()
    {
        if (_leaveCursorOwned)
        {
            CursorHelper.EndHover(this);
            _leaveCursorOwned = false;
        }
    }

    private async Task Say(string line)
    {
        await Dialogue.ShowText(this, line, 5.5f, 576f, 324f, fontSize: 24);
    }

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();

    public override void _Draw()
    {
        if (_background != null)
            DrawTextureRect(_background, new Rect2(0, 0, 1152, 648), false);
        else
            DrawRect(new Rect2(0, 0, 1152, 648), new Color(0.08f, 0.07f, 0.06f), true);

        DrawRect(new Rect2(250, 8, 652, 122), new Color(0.03f, 0.025f, 0.02f, 0.68f), true);
        DrawRect(new Rect2(250, 8, 652, 122), new Color(0.72f, 0.58f, 0.34f, 0.55f), false, 1.5f);
        DrawRect(new Rect2(300, 548, 552, 58), new Color(0.03f, 0.025f, 0.02f, 0.68f), true);
        DrawRect(new Rect2(300, 548, 552, 58), new Color(0.72f, 0.58f, 0.34f, 0.45f), false, 1.5f);

        DrawLeaveControl();
        DrawPlugAndTools();
        DrawLayerPips();
        DrawPickRoll();

        // A held-breath vignette, in plain strips so it stays in the
        // game's chunky visual language.
        DrawRect(new Rect2(0, 0, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);

        if (_state == PickState.Panic)
            DrawPanic();
        else if (_state == PickState.Rewinding)
            DrawRewind();
        else
        {
            DrawEchoCracks();
            DrawSnapFlash();
        }
    }

    /// <summary>The panic itself: a red held breath over everything,
    /// the screen's glass walking apart segment by segment, the
    /// two ways out of the next ten seconds, and the seconds.</summary>
    private void DrawPanic()
    {
        var screen = new Rect2(0, 0, 1152, 648);
        float gone = 1f - Mathf.Clamp(_panicLeft / PanicSeconds, 0f, 1f);
        float pulse = Settings.ReduceMotionOrDefault()
            ? 0.3f
            : 0.3f + 0.12f * (float)Math.Sin(Time.GetTicksMsec() / 120.0);
        DrawRect(screen, new Color(0.3f, 0.02f, 0.02f, 0.24f + 0.3f * gone), true);
        var edge = new Color(0.55f, 0.05f, 0.04f, pulse);
        DrawRect(new Rect2(0, 0, 1152, 60), edge, true);
        DrawRect(new Rect2(0, 588, 1152, 60), edge, true);
        DrawRect(new Rect2(0, 0, 60, 648), edge, true);
        DrawRect(new Rect2(1092, 0, 60, 648), edge, true);

        var bright = new Color(0.93f, 0.96f, 1f, 0.25f + 0.6f * gone);
        var faint = new Color(0.93f, 0.96f, 1f, 0.08f + 0.25f * gone);
        var twin = new Vector2(1.5f, 1.5f);
        foreach (var chain in PanicCracks)
        {
            int segments = Mathf.Clamp(
                Mathf.FloorToInt(gone * (chain.Length - 1) + 0.001f), 0, chain.Length - 1);
            for (int i = 0; i < segments; i++)
            {
                DrawLine(chain[i] + twin, chain[i + 1] + twin, faint, 2.4f, true);
                DrawLine(chain[i], chain[i + 1], bright, 1.2f, true);
            }
        }

        bool canRewind = (GameState.Instance?.ClarityCount ?? 0) > 0;
        DrawPanicChoice(RewindRect, canRewind);
        DrawPanicChoice(LetGoRect, true);

        // The ten seconds, draining red above the choice.
        var track = new Rect2(316, 446, 520, 8);
        DrawRect(track, new Color(1f, 0.92f, 0.68f, 0.15f), true);
        DrawRect(new Rect2(track.Position,
                new Vector2(track.Size.X * Mathf.Clamp(_panicLeft / PanicSeconds, 0f, 1f), track.Size.Y)),
            new Color(1f, 0.36f, 0.26f, 0.95f), true);
    }

    private void DrawPanicChoice(Rect2 rect, bool live)
    {
        bool hover = live && rect.HasPoint(GetGlobalMousePosition());
        DrawRect(rect, new Color(0.05f, 0.04f, 0.03f, live ? (hover ? 0.95f : 0.86f) : 0.6f), true);
        DrawRect(rect, new Color(0.82f, 0.68f, 0.45f, live ? (hover ? 1f : 0.65f) : 0.3f),
            false, hover ? 2.5f : 1.5f);
    }

    private void DrawLeaveControl()
    {
        bool hover = _state == PickState.Picking &&
            LeaveRect.HasPoint(GetGlobalMousePosition());
        DrawRect(LeaveRect,
            hover ? new Color(0.2f, 0.14f, 0.07f, 0.92f) : new Color(0.05f, 0.04f, 0.03f, 0.84f),
            true);
        DrawRect(LeaveRect, new Color(0.78f, 0.63f, 0.36f, hover ? 0.95f : 0.6f), false, 1.5f);
        if (_leaveLabel != null)
            _leaveLabel.Modulate = hover ? new Color(1.08f, 1.02f, 0.88f) : Colors.White;
    }

    private void DrawPlugAndTools()
    {
        float plugRad = Mathf.DegToRad(_plugDegrees);
        Vector2 plugUp = DirectionFromDegrees(_plugDegrees);
        // The painted lock is the housing; this darker brass circle is the
        // part that can actually turn inside it.
        DrawCircle(LockCenter, 72f, new Color(0.09f, 0.07f, 0.05f, 0.98f));
        DrawArc(LockCenter, 72f, 0f, Mathf.Tau, 48,
            new Color(0.72f, 0.55f, 0.28f, 0.95f), 3f, true);
        DrawCircle(LockCenter, 58f, new Color(0.22f, 0.16f, 0.1f, 0.98f));
        DrawArc(LockCenter, 58f, 0f, Mathf.Tau, 44,
            new Color(0.45f, 0.34f, 0.2f, 0.9f), 2f, true);

        // Keyway, carried around by the plug.
        DrawLine(LockCenter + plugUp * 8f, LockCenter + plugUp * 48f,
            new Color(0.015f, 0.012f, 0.01f), 17f, true);
        DrawCircle(LockCenter, 15f, new Color(0.015f, 0.012f, 0.01f));
        Vector2 plugSide = new(-plugUp.Y, plugUp.X);
        DrawLine(LockCenter - plugUp * 3f, LockCenter - plugUp * 3f + plugSide * 15f,
            new Color(0.015f, 0.012f, 0.01f), 9f, true);

        DrawTensionWrench(plugRad);
        DrawPick();
    }

    private void DrawTensionWrench(float plugRad)
    {
        Vector2 Rotate(Vector2 local)
        {
            float cos = Mathf.Cos(plugRad);
            float sin = Mathf.Sin(plugRad);
            return LockCenter + new Vector2(
                local.X * cos - local.Y * sin,
                local.X * sin + local.Y * cos);
        }

        var steel = new Color(0.78f, 0.8f, 0.78f, 0.98f);
        var steelShadow = new Color(0.28f, 0.29f, 0.29f, 0.98f);
        // Short leg in the bottom of the keyway, long flat leg to Mara
        // other hand at lower left. The mouse turns this whole tool.
        DrawLine(Rotate(new Vector2(0, 7)), Rotate(new Vector2(-34, 48)), steelShadow, 13f, true);
        DrawLine(Rotate(new Vector2(-34, 48)), Rotate(new Vector2(-238, 48)), steelShadow, 13f, true);
        DrawLine(Rotate(new Vector2(0, 6)), Rotate(new Vector2(-34, 46)), steel, 8f, true);
        DrawLine(Rotate(new Vector2(-34, 46)), Rotate(new Vector2(-238, 46)), steel, 8f, true);
        DrawLine(Rotate(new Vector2(-238, 46)), Rotate(new Vector2(-262, 69)), steel, 10f, true);
    }

    private void DrawPick()
    {
        Vector2 direction = DirectionFromDegrees(_pickDegrees);
        Vector2 side = new(-direction.Y, direction.X);
        Vector2 jitter = Vector2.Zero;
        if (_state == PickState.Picking && !Settings.ReduceMotionOrDefault())
        {
            // The trembling is the durability gauge she was born
            // with: strain now, damage underneath it. A pick that
            // has been forced hard trembles worse at every later
            // bind — and her hands themselves worsen with every
            // pick already lost. No numbers anywhere; just shake.
            float strain = Mathf.Max(_stress,
                _binding ? _pickDamage * 0.9f : _pickDamage * 0.22f);
            float nerves = 0.35f * (GameState.Instance?.BrokenPickCount ?? 0);
            float shake = Mathf.Max(0f, strain - 0.18f) * 9f + nerves;
            jitter = side * (float)Math.Sin(Time.GetTicksMsec() / 28.0) * shake;
        }

        var steel = new Color(0.84f, 0.85f, 0.82f, 0.99f);
        var darkSteel = new Color(0.32f, 0.33f, 0.33f, 0.99f);
        Vector2 tip = LockCenter + direction * 12f + jitter;
        Vector2 shaftEnd = LockCenter + direction * 176f + jitter;
        Vector2 handleEnd = LockCenter + direction * 258f + jitter;

        if (_state == PickState.Broken || _state == PickState.Panic)
        {
            // The snap, held a moment too long: a stub in her hand,
            // the working end already somewhere on the floor.
            Vector2 stubEnd = LockCenter + direction * 92f;
            DrawLine(stubEnd, handleEnd, darkSteel, 6f, true);
            DrawLine(stubEnd, handleEnd, steel, 3.5f, true);
            DrawLine(stubEnd, stubEnd + side * 6f - direction * 7f, steel, 3f, true);
            DrawLine(shaftEnd, handleEnd, new Color(0.16f, 0.12f, 0.09f), 15f, true);
            Vector2 frag = LockCenter + new Vector2(30, 58);
            DrawLine(frag, frag + new Vector2(15, 9), steel, 3f, true);
            return;
        }



        DrawLine(tip, shaftEnd, darkSteel, 6f, true);
        DrawLine(tip, shaftEnd, steel, 3.5f, true);
        // The little hook that worries one pin at a time.
        DrawLine(tip, tip + side * 13f - direction * 4f, steel, 3.5f, true);
        // Wrapped handle.
        DrawLine(shaftEnd, handleEnd, new Color(0.16f, 0.12f, 0.09f), 15f, true);
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = shaftEnd + direction * (12f + i * 17f);
            DrawLine(p - side * 7f, p + side * 7f,
                new Color(0.55f, 0.43f, 0.28f, 0.95f), 3f, true);
        }
    }

    private void DrawLayerPips()
    {
        if (LayerCount <= 0)
            return;

        const float spacing = 30f;
        float startX = LockCenter.X - (LayerCount - 1) * spacing * 0.5f;
        float y = 512f;
        float pulse = Settings.ReduceMotionOrDefault()
            ? 0.75f
            : 0.62f + 0.22f * (float)Math.Sin(Time.GetTicksMsec() / 220.0);

        for (int i = 0; i < LayerCount; i++)
        {
            var center = new Vector2(startX + i * spacing, y);
            bool set = _state == PickState.Success || i < _layerIndex;
            bool current = i == _layerIndex && _state == PickState.Picking;
            if (set)
            {
                DrawCircle(center, 8f, new Color(1f, 0.82f, 0.42f, 0.96f));
                DrawArc(center, 10f, 0f, Mathf.Tau, 28,
                    new Color(0.55f, 0.39f, 0.18f, 0.9f), 1.5f, true);
            }
            else
            {
                DrawCircle(center, 6f, new Color(0.08f, 0.07f, 0.05f, 0.9f));
                DrawArc(center, current ? 10f : 8f, 0f, Mathf.Tau, 28,
                    new Color(1f, 0.86f, 0.52f, current ? pulse : 0.42f),
                    current ? 2f : 1.2f, true);
            }

            // Gate ticks around the plug: gold once that layer has set,
            // pale while it is still the depth Mara is trying to reach.
            Vector2 direction = DirectionFromDegrees(_layerEnds[i]);
            Color tick = set
                ? new Color(1f, 0.82f, 0.42f, 0.95f)
                : new Color(0.9f, 0.82f, 0.64f, current ? pulse : 0.38f);
            DrawLine(LockCenter + direction * 79f, LockCenter + direction * 91f,
                tick, set || current ? 3f : 2f, true);
        }
    }

    /// <summary>The roll, unrolled along the bottom right: seven
    /// picks in their pockets, the broken ones drawn in two dim
    /// pieces with the gap where the steel gave up.</summary>
    private void DrawPickRoll()
    {
        int broken = GameState.Instance?.BrokenPickCount ?? 0;
        // While time runs backwards the roll is seen mending,
        // pick by pick, newest wound first.
        if (_state == PickState.Rewinding)
            broken = Mathf.RoundToInt((1f - _rewindT) * broken);
        var sound = new Color(0.88f, 0.78f, 0.5f, 0.95f);
        var gone = new Color(0.55f, 0.5f, 0.44f, 0.55f);
        DrawRect(new Rect2(986, 566, 150, 64), new Color(0.03f, 0.025f, 0.02f, 0.66f), true);
        DrawRect(new Rect2(986, 566, 150, 64), new Color(0.72f, 0.58f, 0.34f, 0.45f), false, 1.5f);
        for (int i = 0; i < GameState.LockpickTotal; i++)
        {
            float x = 1002f + i * 18f;
            if (i < GameState.LockpickTotal - broken)
            {
                DrawLine(new Vector2(x, 576), new Vector2(x, 618), sound, 3f, true);
                DrawLine(new Vector2(x, 576), new Vector2(x + 5, 570), sound, 2f, true);
            }
            else
            {
                DrawLine(new Vector2(x, 576), new Vector2(x, 592), gone, 2.5f, true);
                DrawLine(new Vector2(x + 2, 600), new Vector2(x, 618), gone, 2.5f, true);
            }
        }
    }

    /// <summary>What stays on the screen after each snap: one more
    /// hairline in the glass for every broken pick, and a red
    /// pulse at the edges of her vision that breathes faster the
    /// emptier the roll gets. The panic is legible at a glance —
    /// it just never becomes a number.</summary>
    private void DrawEchoCracks()
    {
        int broken = GameState.Instance?.BrokenPickCount ?? 0;
        if (broken <= 0 || _state == PickState.Success)
            return;

        var line = new Color(0.93f, 0.96f, 1f, 0.1f + 0.045f * broken);
        var twin = new Vector2(1.5f, 1.5f);
        for (int c = 0; c < Mathf.Min(broken, PanicCracks.Length); c++)
        {
            var chain = PanicCracks[c];
            for (int i = 0; i < chain.Length - 1; i++)
            {
                DrawLine(chain[i] + twin, chain[i + 1] + twin,
                    new Color(0, 0, 0, line.A * 0.8f), 2.2f, true);
                DrawLine(chain[i], chain[i + 1], line, 1.1f, true);
            }
        }

        float pace = 460f - 36f * broken;
        float pulse = Settings.ReduceMotionOrDefault()
            ? 0.5f
            : 0.5f + 0.5f * (float)Math.Sin(Time.GetTicksMsec() / (double)pace);
        var edge = new Color(0.55f, 0.05f, 0.04f, (0.05f + 0.022f * broken) * pulse + 0.02f * broken);
        DrawRect(new Rect2(0, 0, 1152, 44), edge, true);
        DrawRect(new Rect2(0, 604, 1152, 44), edge, true);
        DrawRect(new Rect2(0, 0, 44, 648), edge, true);
        DrawRect(new Rect2(1108, 0, 44, 648), edge, true);
    }

    /// <summary>The instant of a snap, worn on the whole screen:
    /// a hot red wash strobing at the edges, cooling inside a
    /// second — flinch first, words after.</summary>
    private void DrawSnapFlash()
    {
        if (_snapFlashLeft <= 0f)
            return;
        float heat = Mathf.Clamp(_snapFlashLeft / 0.9f, 0f, 1f);
        bool on = Settings.ReduceMotionOrDefault()
            ? true
            : (Time.GetTicksMsec() / 80) % 2 == 0;
        var screen = new Rect2(0, 0, 1152, 648);
        DrawRect(screen, new Color(0.5f, 0.03f, 0.02f, 0.16f * heat * (on ? 1f : 0.4f)), true);
        var edge = new Color(0.62f, 0.04f, 0.03f, 0.5f * heat * (on ? 1f : 0.45f));
        DrawRect(new Rect2(0, 0, 1152, 64), edge, true);
        DrawRect(new Rect2(0, 584, 1152, 64), edge, true);
        DrawRect(new Rect2(0, 0, 64, 648), edge, true);
        DrawRect(new Rect2(1088, 0, 64, 648), edge, true);
    }

    /// <summary>Time going back in plain sight: the colour drains
    /// to grey, the picture strobes and tears like abused tape,
    /// the cracks walk themselves OFF the glass, and underneath
    /// it all the evening reassembles — plug unturning, picks
    /// rising whole into the roll, her words reversing overhead.</summary>
    private void DrawRewind()
    {
        var screen = new Rect2(0, 0, 1152, 648);
        float t = _rewindT;
        bool calm = Settings.ReduceMotionOrDefault();

        // The grey comes for everything, then deeper towards black.
        DrawRect(screen, new Color(0.55f, 0.55f, 0.55f, 0.45f * (1f - t) + 0.15f), true);
        if (!calm && (Time.GetTicksMsec() / 90) % 2 == 0)
            DrawRect(screen, new Color(0.92f, 0.92f, 0.92f, 0.22f * (1f - t)), true);
        DrawRect(screen, new Color(0, 0, 0, 0.55f * t * t), true);

        // Tape tears: pale and dark bands skating sideways.
        var tearRng = new RandomNumberGenerator();
        tearRng.Seed = (ulong)(Time.GetTicksMsec() / 70);
        for (int i = 0; i < 5; i++)
        {
            float y = tearRng.RandfRange(0f, 648f);
            float h = tearRng.RandfRange(3f, 14f);
            bool pale = tearRng.Randf() > 0.5f;
            DrawRect(new Rect2(0, y, 1152, h),
                pale ? new Color(0.9f, 0.9f, 0.9f, 0.14f) : new Color(0, 0, 0, 0.2f), true);
        }

        // The cracks, leaving the way they came.
        float remain = 1f - t;
        var bright = new Color(0.95f, 0.97f, 1f, 0.75f * remain + 0.1f);
        var twin = new Vector2(1.5f, 1.5f);
        foreach (var chain in PanicCracks)
        {
            int segments = Mathf.Clamp(
                Mathf.FloorToInt(remain * (chain.Length - 1)), 0, chain.Length - 1);
            for (int i = 0; i < segments; i++)
            {
                DrawLine(chain[i] + twin, chain[i + 1] + twin,
                    new Color(0, 0, 0, bright.A * 0.6f), 2.2f, true);
                DrawLine(chain[i], chain[i + 1], bright, 1.2f, true);
            }
        }
    }

    /// <summary>Her words, unspooling: fragments of the snap lines
    /// and the intro, word order reversed, walking backwards
    /// through the evening faster than she can hold them.</summary>
    private static string RewindSnippetText(int index)
    {
        string[] pool =
        [
            SnapLines[5], SnapLines[4], SnapLines[3],
            SnapLines[2], SnapLines[1], SnapLines[0],
            "don't push a sound you don't like",
            "seven picks in the roll",
        ];
        string source = pool[Mathf.Min(index, pool.Length - 1)];
        // A bite of it, not the whole sentence — tape at speed.
        var words = source.Split(' ');
        int take = Mathf.Min(words.Length, 9);
        var bite = new string[take];
        System.Array.Copy(words, bite, take);
        System.Array.Reverse(bite);
        return "…" + string.Join(" ", bite) + "…";
    }

    private static Vector2 DirectionFromDegrees(float degrees)
    {
        float rad = Mathf.DegToRad(degrees);
        return new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
    }
}

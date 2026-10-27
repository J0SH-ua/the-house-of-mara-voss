using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The last preserve jar, up close — its own small puzzle, the way her
/// mother taught by example.
///
/// The seal will not give to fingernails. Try anyway and the attempt
/// plays out in Mara's hands: the lid strains, a crack starts across
/// the glass, and she stops herself cold — remembering, out loud, how
/// her mother used to open these with just a hairpin. The clue is the
/// moment; the hairpin sleeps where it always slept, in the little
/// drawer of her childhood nightstand, in the very first room.
///
/// With the hairpin, the seal becomes timing: a working line travels
/// the rim, and the one red stretch where the rubber gives keeps
/// moving. Tap the instant the line crosses the red — seven true
/// taps, faster and turning back on itself each time — until the lid
/// sighs open instead of breaking. One mistimed tap and the seal
/// takes its whole patience back: from the top. Inside, in layers, lie the two
/// things the jar was keeping: Ida's folded name tape (shard 2) on top,
/// and underneath it the circle key for the hallway door. Each is
/// taken by its own click. The tape alone is enough to leave by —
/// the key can sleep on, and the house will ask once whether Mara
/// meant to leave it sleeping.
/// </summary>
public partial class JarCloseup : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    private const string CircleKeyIconPath = "res://assets/images/core/items/circle_key.png";
    private const string HairpinIconPath = "res://assets/images/core/items/hairpin.png";
    private const string Room3ScenePath = "res://scenes/world/room_3.tscn";
    private const string JourneyScenePath = "res://scenes/world/journey_to_room_4.tscn";
    private const string HairpinFlag = "item_hairpin";
    private const string CircleKeyFlag = "item_hall_key_circle";
    private const string JarOpenedFlag = "jar_opened";
    private const string BareAttemptFlag = "jar_attempted_barehanded";

    private static readonly Vector2 JarCenter = new(576, 372);
    private static readonly Vector2 SealCenter = new(576, 258);
    private const float SealRadius = 98f;
    private const int TapsNeeded = 7;
    private const float TapWindowDegrees = 10f;

    private static readonly Rect2 JarRect = new(452, 196, 248, 330);
    private static readonly Rect2 TapeRect = new(526, 306, 100, 44);
    private static readonly Rect2 KeyRect = new(530, 420, 92, 92);
    private static readonly Rect2 LeaveRect = new(16, 468, 216, 44);

    // Three cracks from one moment of impatience, as point chains.
    private static readonly Vector2[][] Cracks =
    [
        [new Vector2(648, 352), new Vector2(624, 372), new Vector2(628, 398), new Vector2(598, 416), new Vector2(602, 442)],
        [new Vector2(648, 352), new Vector2(672, 376), new Vector2(662, 402), new Vector2(682, 426)],
        [new Vector2(648, 352), new Vector2(636, 328), new Vector2(652, 304), new Vector2(638, 282)],
    ];

    private enum JarState
    {
        Intro,
        Sealed,
        Cracking,
        Open,
        Leaving,
    }

    private FontFile _font;
    private Texture2D _circleKeyTexture;
    private Texture2D _hairpinTexture;
    private float _pinIntro = 1f;
    private Label _helpLabel;
    private Label _leaveLabel;

    private JarState _state = JarState.Intro;
    private bool _busy;
    private bool _hasHairpin;
    private readonly RandomNumberGenerator _rng = new();
    private bool _tapReady;
    private int _tapsLanded;
    private float _needleAngle;
    private float _needleSpeed;
    private float _targetAngle;
    private float _tapFlash;
    private float _missShake;
    private int _missCount;
    private float _crackProgress;
    private float _lidLift;
    private bool _lidPopped;
    private bool _cursorOwned;

    private bool TapeTaken => GameState.Instance?.IsShardRecovered(2) ?? false;
    private bool KeyTaken => GameState.Instance?.HasFlag(CircleKeyFlag) ?? false;
    // Open means the lid was actually worked off — nothing else.
    // (This used to also accept "tape taken", which let any stray
    // grant of shard 2 present the jar already open, puzzle and
    // all skipped. The one proof the tape gives is handled as a
    // migration in _Ready, below.)
    private bool JarOpened => GameState.Instance?.HasFlag(JarOpenedFlag) ?? false;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>(FontPath);
        _circleKeyTexture = GD.Load<Texture2D>(CircleKeyIconPath);
        _hairpinTexture = GD.Load<Texture2D>(HairpinIconPath);
        _rng.Randomize();

        var gs = GameState.Instance;
        _hasHairpin = gs?.HasFlag(HairpinFlag) ?? false;
        if (gs?.HasFlag(BareAttemptFlag) == true)
            _crackProgress = 1f;
        // Migration, and the limit of what the tape proves: the
        // tape has only ever been reachable inside an opened jar,
        // so a save carrying it without the flag popped this lid
        // in a design that didn't record the fact. Record it.
        if (TapeTaken && gs?.HasFlag(JarOpenedFlag) != true)
            gs?.SetFlag(JarOpenedFlag, save: false);
        if (JarOpened)
        {
            _lidPopped = true;
            _lidLift = 1f;
            _state = JarState.Open;
        }

        BuildLabels();
        QueueRedraw();
        _ = PlayIntroAsync();
    }

    private void BuildLabels()
    {
        MakeLabel("The last jar",
            new Vector2(376, 22), new Vector2(400, 38), 29,
            new Color(0.96f, 0.9f, 0.76f, 0.97f));
        _helpLabel = MakeLabel(string.Empty,
            new Vector2(276, 566), new Vector2(600, 38), 20,
            new Color(0.9f, 0.86f, 0.76f, 0.94f));
        _leaveLabel = MakeLabel("Step away from the jars",
            LeaveRect.Position, LeaveRect.Size, 20,
            new Color(0.95f, 0.88f, 0.72f, 0.97f));
        UpdateHelp();
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

    private void UpdateHelp()
    {
        if (_helpLabel == null)
            return;
        _helpLabel.Text = _state switch
        {
            JarState.Sealed when _hasHairpin => $"Tap as the hairpin crosses the red give in the seal.  ·  {_tapsLanded} of {TapsNeeded} true",
            JarState.Sealed => "The seal is twelve years stubborn. Fingernails are what she has. Click the jar to try.",
            JarState.Open when !TapeTaken => "Something folded sleeps on top. Take it gently.",
            JarState.Open when !KeyTaken => "Under where the tape lay, something cold catches the light.",
            JarState.Open => "Empty glass, honestly empty.",
            _ => string.Empty,
        };
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
            if (_state == JarState.Open)
            {
                if (!TapeTaken)
                    await Say("The jar stands open where I left it. Patient as everything in this room.");
                else if (!KeyTaken)
                    await Say("The open jar. Still keeping its second secret at the bottom.");
                else
                    await Say("Empty glass. What it was keeping, I am keeping now.");
                return;
            }

            _state = JarState.Sealed;
            if (_hasHairpin)
            {
                await Say("The last jar. Its seal has had twelve years to become one with the glass. But I watched Mum do this a hundred times — hairpin under the rubber, worked round and round, tapped true every time it crossed where the rubber gives. Seven of those, usually. She counted under her breath.");
                if (!StillHere())
                    return;
                await Say("Wait — under the folded paper. Is that—? Something round. Metal. A ring, sitting in the dark beneath the tape like it was put there to be found second. All right. Whatever you are, you're why this jar stayed sealed.");
                if (!StillHere())
                    return;
                // Physically out of the bag before anything starts:
                // her line, then the pin travelling up to the seal.
                AudioManager.Play(Sfx.PhotoPickup, -12f, 0.9f);
                await Say("Right. Out of the bag, then — quietly does it. Show me again how this goes, Mum.");
            }
            else if (gs?.HasFlag(BareAttemptFlag) == true)
            {
                await Say("The crack is still there, exactly where my impatience left it. Glass remembers being forced. So do I, now.");
            }
            else
            {
                await Say("The last jar. Whichever of Ida's spiders this one held, the seal has had twelve years to become one with the glass.");
                if (!StillHere())
                    return;
                await Say("Wait — under the folded paper. Is that—? Something round. Metal. A ring, sitting in the dark beneath the tape like it was put there to be found second. All right, jar. Let's see how stubborn twelve years made you.");
            }
            if (!StillHere())
                return;
            if (_hasHairpin)
                StartTapGame();
            UpdateHelp();
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    // ---------------------------------------------------------- the seal

    private static float SpeedForTap(int landed) => Mathf.DegToRad(55f + landed * 24f);

    private static float WrapTau(float a)
    {
        while (a > Mathf.Tau) a -= Mathf.Tau;
        while (a < 0f) a += Mathf.Tau;
        return a;
    }

    private static float AngularDistance(float a, float b)
    {
        float d = Mathf.Abs(WrapTau(a) - WrapTau(b));
        return Mathf.Min(d, Mathf.Tau - d);
    }

    private void StartTapGame()
    {
        // Out of the bag first — visibly. The pin travels up to the
        // seal before the counting starts.
        _pinIntro = 0f;
        GameState.Instance?.Toast("Out of the bag: Mum's hairpin.");
        _tapsLanded = 0;
        _needleAngle = -Mathf.Pi / 2f;
        // Counter-clockwise first, the way she started every jar.
        _needleSpeed = -SpeedForTap(0);
        NewTarget();
        _tapReady = true;
    }

    private void NewTarget()
    {
        float a = 0f;
        for (int guard = 0; guard < 24; guard++)
        {
            a = _rng.RandfRange(0f, Mathf.Tau);
            if (AngularDistance(a, _needleAngle) >= Mathf.DegToRad(70f))
                break;
        }
        _targetAngle = a;
    }

    private void TapJar()
    {
        if (!_tapReady || _busy || _state != JarState.Sealed || _pinIntro < 1f)
            return;

        if (AngularDistance(_needleAngle, _targetAngle) <= Mathf.DegToRad(TapWindowDegrees))
            LandTap();
        else
            MissTap();
    }

    private void LandTap()
    {
        _tapsLanded++;
        _tapFlash = 0.28f;
        AudioManager.Play(Sfx.LockpickTick, -10f, 0.95f + _tapsLanded * 0.07f);
        Hotspots.SpawnRipple(
            SealCenter + Vector2.FromAngle(_targetAngle) * SealRadius,
            new Color(1f, 0.88f, 0.55f));

        if (_tapsLanded >= TapsNeeded)
        {
            _tapReady = false;
            _ = PopLidAsync();
            return;
        }

        // Each true tap turns the work back on itself, a little faster.
        float sign = _tapsLanded % 2 == 0 ? -1f : 1f;
        _needleSpeed = sign * SpeedForTap(_tapsLanded);
        NewTarget();
        UpdateHelp();
        QueueRedraw();
    }

    private void MissTap()
    {
        _missCount++;
        _missShake = 0.35f;
        AudioManager.Play(Sfx.LockRattle, -8f, 0.75f);
        AudioManager.Play(Sfx.Denied, -13f, 0.9f);

        // One mistimed tap and the seal takes its whole patience back.
        _tapsLanded = 0;
        _needleSpeed = -SpeedForTap(0);
        NewTarget();

        string[] lines =
        [
            "Slipped. The seal takes its whole patience back. From the top.",
            "Missed the give. Slow is smooth. Again.",
            "Too eager. Mum never hurried a jar in her life.",
        ];
        GameState.Instance?.Toast(lines[Mathf.Min(_missCount - 1, lines.Length - 1)]);
        UpdateHelp();
        QueueRedraw();
    }

    private bool InputFree() =>
        !_busy && !GetTree().Paused && !Dialogue.IsShowing && !Dialogue.IsInputBlocked &&
        !(GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());

    public override void _UnhandledInput(InputEvent @event)
    {
        // A click, or F: the house's hand, for the thing under the
        // cursor. Same reach, same rules, either way.
        bool clicked = @event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left;
        bool useKey = @event is InputEventKey keyEvent && keyEvent.Pressed &&
            !keyEvent.Echo && keyEvent.PhysicalKeycode == Key.F;
        if (!clicked && !useKey)
            return;

        Vector2 pos = GetGlobalMousePosition();

        if (clicked && !mouseButton.Pressed)
            return;

        if (!InputFree())
            return;

        if (LeaveRect.HasPoint(pos))
        {
            GetViewport().SetInputAsHandled();
            _ = LeaveAsync();
            return;
        }

        switch (_state)
        {
            case JarState.Sealed when _hasHairpin:
                if (JarRect.Grow(70f).HasPoint(pos))
                {
                    GetViewport().SetInputAsHandled();
                    TapJar();
                }
                break;

            case JarState.Sealed:
                if (JarRect.HasPoint(pos))
                {
                    GetViewport().SetInputAsHandled();
                    _ = AttemptBarehandedAsync();
                }
                break;

            case JarState.Open:
                if (!TapeTaken && TapeRect.HasPoint(pos))
                {
                    GetViewport().SetInputAsHandled();
                    _ = TakeTapeAsync();
                }
                else if (TapeTaken && !KeyTaken && KeyRect.HasPoint(pos))
                {
                    GetViewport().SetInputAsHandled();
                    _ = TakeCircleKeyAsync();
                }
                break;
        }
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Max((float)delta, 0f);
        UpdateCursor();

        if (_state == JarState.Cracking)
        {
            _crackProgress = Mathf.MoveToward(_crackProgress, 1f, dt / 0.9f);
        }

        if (_lidPopped && _lidLift < 1f)
            _lidLift = Mathf.MoveToward(_lidLift, 1f, dt / 0.5f);

        if (_pinIntro < 1f)
            _pinIntro = Mathf.MoveToward(_pinIntro, 1f, dt / 2.1f);
        if (_state == JarState.Sealed && _hasHairpin && _tapReady && _pinIntro >= 1f && !_busy &&
            !Dialogue.IsShowing && !Dialogue.IsInputBlocked && !GetTree().Paused &&
            !(GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen()))
        {
            _needleAngle = WrapTau(_needleAngle + _needleSpeed * dt);
        }
        _tapFlash = Mathf.Max(0f, _tapFlash - dt);
        _missShake = Mathf.Max(0f, _missShake - dt);

        QueueRedraw();
    }

    private async Task AttemptBarehandedAsync()
    {
        if (_busy)
            return;
        _busy = true;
        Dialogue.BeginInputBlock();
        try
        {
            var gs = GameState.Instance;
            bool firstAttempt = gs?.HasFlag(BareAttemptFlag) != true;

            AudioManager.Play(Sfx.LockRattle, -8f, 0.8f);
            await Say("All right. Fingernails, then. It's only a seal. It's only—");

            if (firstAttempt)
            {
                if (!StillHere())
                    return;
                _state = JarState.Cracking;
                AudioManager.Play(Sfx.ScreenCrack1, -7f, 0.9f);
                await WaitSeconds(0.95f);
                if (!StillHere())
                    return;
                gs?.SetFlag(BareAttemptFlag, save: false);
            }

            await Say("Okay. Okay — stop. Stop. Not like this. One more pull like that and I'll break it, and whatever she kept in it. Glass doesn't forgive being forced. It just keeps the shape of it forever.");
            if (!StillHere())
                return;
            await Say("Maybe I need something to open this. Something thin. ...I remember back then, when Mom opened this with just a hairpin. Worried it round the seal, patient as anything, until the lid just — sighed. Never forced. Not once.");
            if (!StillHere())
                return;
            await Say("A hairpin, then. There's one where there's always been one — the little drawer in my old nightstand. Where I woke up. Everything useful in this house is back the way I came.");
            if (!StillHere())
                return;

            gs?.SaveGame("jar barehanded attempt");
            _state = JarState.Leaving;
            SceneManager.ChangeScene(Room3ScenePath);
        }
        finally
        {
            Dialogue.EndInputBlock();
            _busy = false;
        }
    }

    private async Task PopLidAsync()
    {
        if (_busy || _state != JarState.Sealed)
            return;
        _busy = true;
        _tapReady = false;
        Dialogue.BeginInputBlock();
        try
        {
            _state = JarState.Open;
            _lidPopped = true;
            _lidLift = 0f;
            AudioManager.Play(Sfx.LockOpen, -7f, 1.28f);
            AudioManager.Play(Sfx.WordChime, -13f, 1.05f);
            Hotspots.SpawnRipple(SealCenter, new Color(1f, 0.88f, 0.55f));
            GameState.Instance?.SetFlag(JarOpenedFlag, save: false);
            UpdateHelp();
            QueueRedraw();

            await WaitSeconds(0.55f);
            if (!StillHere())
                return;
            await Say("There. The sigh. Twelve years of seal, undone by a hairpin and not hurrying. She made it look like nothing. It was never nothing. It was the whole technique: don't break what you're opening.");
            if (!StillHere())
                return;
            GameState.Instance?.SaveGame("jar opened");
        }
        finally
        {
            Dialogue.EndInputBlock();
            _busy = false;
        }
    }

    private async Task TakeTapeAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var gs = GameState.Instance;
            AudioManager.Play(Sfx.PhotoPickup, -8f, 0.95f);
            bool firstFragment = gs?.CollectShard(2) ?? false;
            RoomExamineTracker.RecordExamine("Room3", "Jars");
            gs?.MarkRoomCompleted("Room3");
            Hotspots.SpawnRipple(TapeRect.GetCenter(), new Color(1f, 0.88f, 0.55f));
            UpdateHelp();
            QueueRedraw();

            if (firstFragment)
            {
                await Say("At the bottom of the last jar is a folded square of name tape. IDA, in careful block letters. The one label Mara could never throw away.");
                if (!StillHere())
                    return;
                await Say("Ida named the spiders so she could say them aloud without flinching. Mara used to repeat the names after her, in the order Ida gave them, until fear sounded smaller than the voice holding it.");
                if (!StillHere())
                    return;
                await Say("That is the first new way this memory gives back: Echo — her sister's old courage, turned into listening. From here, remembering has verbs.");
                if (!StillHere())
                    return;
            }

            await Say("She kept everything. That's what love does when it doesn't know what else to do — it keeps everything exactly where it was and calls it waiting.");
            if (!StillHere())
                return;
            await Say("The jars, the bag, the drawings on the wall. She kept the spiders. She kept the names.");
            if (!StillHere())
                return;
            await Say("She kept a note she meant to give someone. Everything in here was preserved, and preservation is not the same as keeping someone. But the heart does what it can.");
        }
        finally
        {
            _busy = false;
            if (StillHere())
                QueueRedraw();
        }
    }

    private async Task TakeCircleKeyAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs == null || gs.HasFlag(CircleKeyFlag))
                return;

            AudioManager.Play(Sfx.KeyType, -10f, 1.05f);
            await Say("Under the folded tape, something cold moves against the glass. A key. Its bow is a perfect circle — no beginning, no end, preserved with everything else she couldn't let go.");
            if (!StillHere())
                return;

            gs.SetFlag(CircleKeyFlag, save: false);
            gs.UnlockClue("hall_key_circle");
            gs.Toast("Circle key — kept in Mara's bag (B).");
            gs.SaveGame("circle key taken");
            UpdateHelp();
        }
        finally
        {
            _busy = false;
            if (StillHere())
                QueueRedraw();
        }
    }

    private async Task LeaveAsync()
    {
        if (_busy || _state == JarState.Leaving)
            return;
        JarState from = _state;
        _state = JarState.Leaving;
        AudioManager.Play(Sfx.PageTurn, -11f, 0.85f);
        await WaitSeconds(0.15f);
        if (!StillHere())
            return;

        var gs = GameState.Instance;
        // The walk to the hallway belongs to the first time she leaves
        // with the name tape; coming back later just for the key is an
        // ordinary step away.
        bool goJourney = gs != null && gs.IsShardRecovered(2) &&
            !gs.HasFlag("journey_seen_Room3ToRoom4");
        SceneManager.ChangeScene(goJourney ? JourneyScenePath : Room3ScenePath);

        // If the house is holding the change to ask whether she meant
        // to leave the circle key sleeping, her "look around a little
        // more" must find the close-up alive again, not latched shut.
        await WaitSeconds(0.6f);
        if (StillHere() && _state == JarState.Leaving)
            _state = from == JarState.Leaving ? JarState.Sealed : from;
    }

    private void UpdateCursor()
    {
        Vector2 pos = GetGlobalMousePosition();
        bool over = InputFree() && (LeaveRect.HasPoint(pos) ||
            (_state == JarState.Open && !TapeTaken && TapeRect.HasPoint(pos)) ||
            (_state == JarState.Open && TapeTaken && !KeyTaken && KeyRect.HasPoint(pos)) ||
            (_state == JarState.Sealed && JarRect.Grow(70f).HasPoint(pos)));
        if (over && !_cursorOwned)
        {
            CursorHelper.BeginHover(this, PointerCursorPath);
            _cursorOwned = true;
        }
        else if (!over && _cursorOwned)
        {
            CursorHelper.EndHover(this);
            _cursorOwned = false;
        }
    }

    public override void _ExitTree()
    {
        if (_cursorOwned)
        {
            CursorHelper.EndHover(this);
            _cursorOwned = false;
        }
    }

    private async Task Say(string line)
    {
        await Dialogue.ShowText(this, line, 6f, 576f, 324f, fontSize: 23);
    }

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();

    // ------------------------------------------------------------ drawing

    public override void _Draw()
    {
        DrawPantry();
        bool shaking = _missShake > 0.01f && !Settings.ReduceMotionOrDefault();
        if (shaking)
        {
            float s = 3.2f * (_missShake / 0.35f);
            DrawSetTransform(new Vector2(
                _rng.RandfRange(-s, s), _rng.RandfRange(-s * 0.6f, s * 0.6f)),
                0f, Vector2.One);
        }
        DrawJar();
        if (shaking)
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        if (_state == JarState.Sealed && _hasHairpin && _tapReady)
            DrawWorkingLine();
        DrawLeaveControl();

        DrawRect(new Rect2(0, 0, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
    }

    private void DrawPantry()
    {
        // Back wall of pantry boards and the ghosts of other jars,
        // in flat chunky bands. The one that matters sits lit center.
        DrawRect(new Rect2(0, 0, 1152, 648), new Color(0.07f, 0.055f, 0.045f), true);
        for (int i = 0; i < 8; i++)
        {
            float x = i * 148f;
            DrawRect(new Rect2(x, 0, 3, 648), new Color(0.045f, 0.035f, 0.03f, 0.8f), true);
        }
        // Shelf lines with kept silhouettes.
        DrawShelfSilhouettes(150f);
        DrawShelfSilhouettes(300f);
        // The table the last jar stands on.
        DrawRect(new Rect2(180, 518, 792, 20), new Color(0.24f, 0.17f, 0.11f), true);
        DrawRect(new Rect2(180, 518, 792, 5), new Color(0.38f, 0.28f, 0.17f), true);
        DrawRect(new Rect2(220, 538, 26, 110), new Color(0.17f, 0.12f, 0.08f), true);
        DrawRect(new Rect2(906, 538, 26, 110), new Color(0.17f, 0.12f, 0.08f), true);
        // A pool of attention around the jar.
        DrawCircle(JarCenter, 230f, new Color(1f, 0.85f, 0.55f, 0.045f));
        DrawCircle(JarCenter, 160f, new Color(1f, 0.85f, 0.55f, 0.05f));
    }

    private void DrawShelfSilhouettes(float y)
    {
        DrawRect(new Rect2(40, y + 58, 1072, 8), new Color(0.13f, 0.1f, 0.07f, 0.9f), true);
        var jarGhost = new Color(0.11f, 0.1f, 0.09f, 0.85f);
        float[] xs = [88f, 236f, 828f, 952f, 1040f];
        foreach (float x in xs)
        {
            DrawRect(new Rect2(x, y + 8, 52, 50), jarGhost, true);
            DrawRect(new Rect2(x + 6, y, 40, 10), jarGhost, true);
        }
    }

    private void DrawJar()
    {
        var glass = new Color(0.62f, 0.72f, 0.66f, 0.3f);
        var glassEdge = new Color(0.8f, 0.88f, 0.82f, 0.85f);
        var contents = new Color(0.23f, 0.14f, 0.18f, 0.9f);

        // Body: sloped shoulders under the lid, straight sides, heavy base.
        Vector2[] bodyShape =
        [
            new(490, 264), new(662, 264), new(684, 302),
            new(684, 520), new(468, 520), new(468, 302),
        ];
        DrawColoredPolygon(bodyShape, glass);
        DrawLine(new Vector2(490, 264), new Vector2(468, 302), glassEdge, 3f, true);
        DrawLine(new Vector2(662, 264), new Vector2(684, 302), glassEdge, 3f, true);
        DrawLine(new Vector2(468, 302), new Vector2(468, 520), glassEdge, 3f, true);
        DrawLine(new Vector2(684, 302), new Vector2(684, 520), glassEdge, 3f, true);
        DrawRect(new Rect2(468, 508, 216, 12), new Color(0.7f, 0.78f, 0.72f, 0.5f), true);
        DrawLine(new Vector2(468, 520), new Vector2(684, 520), glassEdge, 3f, true);

        // Preserved dark fruit settled at the bottom.
        Vector2[] plums =
        [
            new(516, 478), new(560, 492), new(606, 480), new(648, 492),
            new(538, 458), new(586, 464), new(630, 456),
        ];
        foreach (var plum in plums)
        {
            DrawCircle(plum, 17f, contents);
            DrawCircle(plum + new Vector2(-5, -5), 5f, new Color(0.55f, 0.4f, 0.45f, 0.5f));
        }

        // A patient shine down the left glass.
        DrawRect(new Rect2(492, 300, 10, 150), new Color(1f, 1f, 1f, 0.1f), true);
        DrawRect(new Rect2(510, 320, 5, 90), new Color(1f, 1f, 1f, 0.07f), true);

        DrawCracks();
        DrawLid();

        if (_state == JarState.Open || _lidPopped)
            DrawJarContents();
    }

    private void DrawCracks()
    {
        if (_crackProgress <= 0.01f)
            return;
        var crackColor = new Color(0.9f, 0.95f, 0.92f, 0.75f);
        foreach (var chain in Cracks)
        {
            int segments = Mathf.Clamp(
                Mathf.FloorToInt(_crackProgress * (chain.Length - 1)), 0, chain.Length - 1);
            for (int i = 0; i < segments; i++)
                DrawLine(chain[i], chain[i + 1], crackColor, 1.6f, true);
        }
    }

    private void DrawLid()
    {
        float lift = _lidLift;
        Vector2 offset = new(46f * lift, -54f * lift);

        // The seal ring the hairpin works around: pale while sealed,
        // gold where true taps have worried it loose, and one red
        // stretch where the rubber gives — never twice in the same
        // place. Seven true taps, counted in pips above the lid.
        if (!_lidPopped)
        {
            DrawArc(SealCenter, SealRadius, 0f, Mathf.Tau, 64,
                new Color(0.85f, 0.8f, 0.68f, 0.35f), 5f, true);
            float given = (float)_tapsLanded / TapsNeeded;
            if (given > 0.01f)
            {
                DrawArc(SealCenter, SealRadius, -Mathf.Pi / 2f,
                    -Mathf.Pi / 2f + given * Mathf.Tau * 0.92f, 64,
                    new Color(1f, 0.82f, 0.42f, 0.9f), 5f, true);
            }

            if (_tapReady)
            {
                float halfWin = Mathf.DegToRad(TapWindowDegrees);
                float flash = _tapFlash > 0f ? 0.35f : 0f;
                DrawArc(SealCenter, SealRadius, _targetAngle - halfWin, _targetAngle + halfWin, 14,
                    new Color(0.96f, 0.26f + flash, 0.2f + flash, 0.95f), 8f, true);

                for (int i = 0; i < TapsNeeded; i++)
                {
                    Vector2 pip = new(576f + (i - (TapsNeeded - 1) * 0.5f) * 24f, 124f);
                    bool litPip = i < _tapsLanded;
                    DrawCircle(pip, 6f, litPip
                        ? new Color(1f, 0.84f, 0.45f, 0.98f)
                        : new Color(0.9f, 0.84f, 0.68f, 0.3f));
                    DrawArc(pip, 8.5f, 0f, Mathf.Tau, 24,
                        new Color(0.85f, 0.7f, 0.42f, litPip ? 0.8f : 0.35f), 1.5f, true);
                }
            }
        }

        var lidMetal = new Color(0.55f, 0.52f, 0.48f, 1f);
        var lidDark = new Color(0.3f, 0.28f, 0.26f, 1f);
        var band = new Rect2(new Vector2(478, 232) + offset, new Vector2(196, 34));
        var top = new Rect2(new Vector2(490, 214) + offset, new Vector2(172, 20));

        if (_lidPopped)
        {
            // The dark mouth where the lid was.
            DrawRect(new Rect2(486, 238, 180, 22), new Color(0.02f, 0.02f, 0.02f, 0.95f), true);
            DrawRect(new Rect2(486, 238, 180, 22), new Color(0.8f, 0.88f, 0.82f, 0.6f), false, 2f);
        }

        DrawRect(band, lidDark, true);
        DrawRect(band, lidMetal, false, 3f);
        for (int i = 0; i < 9; i++)
        {
            float x = band.Position.X + 14f + i * 21f;
            DrawLine(new Vector2(x, band.Position.Y + 5),
                new Vector2(x, band.End.Y - 5),
                new Color(0.7f, 0.68f, 0.62f, 0.7f), 2f, true);
        }
        DrawRect(top, lidMetal, true);
        DrawRect(top, new Color(0.22f, 0.2f, 0.18f), false, 2f);
        DrawRect(new Rect2(top.Position + new Vector2(10, 5), new Vector2(60, 4)),
            new Color(0.82f, 0.8f, 0.74f, 0.8f), true);
    }

    private void DrawJarContents()
    {
        if (!TapeTaken)
        {
            // Under the folded paper, something round: the circle
            // key shows one cold edge of itself below the tape's
            // hem — enough to be pointed at, not enough to be had.
            if (!KeyTaken && _circleKeyTexture != null)
            {
                DrawTextureRect(_circleKeyTexture, new Rect2(504, 322, 76, 76), false,
                    new Color(0.82f, 0.8f, 0.74f, 0.92f));
            }
            // The folded name tape, pale on top of the dark.
            var tape = TapeRect;
            DrawRect(tape, new Color(0.9f, 0.85f, 0.7f, 0.98f), true);
            DrawRect(tape, new Color(0.45f, 0.38f, 0.26f, 0.95f), false, 2f);
            DrawRect(new Rect2(tape.Position + new Vector2(8, 8), new Vector2(34, 5)),
                new Color(0.3f, 0.26f, 0.2f, 0.85f), true);
            DrawRect(new Rect2(tape.Position + new Vector2(8, 20), new Vector2(52, 5)),
                new Color(0.3f, 0.26f, 0.2f, 0.6f), true);
            DrawRect(new Rect2(tape.Position + new Vector2(8, 32), new Vector2(26, 5)),
                new Color(0.3f, 0.26f, 0.2f, 0.6f), true);
            if (_font != null)
            {
                DrawString(_font, tape.Position + new Vector2(58, 26), "IDA",
                    HorizontalAlignment.Left, -1, 20, new Color(0.25f, 0.2f, 0.16f));
            }
        }
        else if (!KeyTaken && _circleKeyTexture != null)
        {
            float pulse = Settings.ReduceMotionOrDefault()
                ? 0.75f
                : 0.62f + 0.22f * (float)Math.Sin(Time.GetTicksMsec() / 240.0);
            DrawArc(KeyRect.GetCenter(), 52f, 0f, Mathf.Tau, 40,
                new Color(1f, 0.85f, 0.5f, pulse * 0.6f), 2f, true);
            DrawTextureRect(_circleKeyTexture, KeyRect, false);
        }
    }

    /// <summary>She has her bag at her hip the whole evening; for the
    /// seconds the hairpin is LEAVING it, the bag gets to exist on
    /// screen — a worn dark shape bottom-right, flap open, so the
    /// taking-out reads as a taking-out and not as an appearing.</summary>
    private void DrawBagProp(float open01)
    {
        var body = new Rect2(942, 580, 150, 62);
        DrawRect(new Rect2(body.Position + new Vector2(4, 5), body.Size),
            new Color(0, 0, 0, 0.4f), true);
        DrawRect(body, new Color(0.16f, 0.1f, 0.07f, 0.98f), true);
        DrawRect(body, new Color(0.42f, 0.3f, 0.18f, 0.9f), false, 2f);
        // The strap, going up out of frame toward her shoulder.
        DrawLine(new Vector2(1078, 586), new Vector2(1128, 470),
            new Color(0.2f, 0.13f, 0.09f, 0.95f), 10f, true);
        // The flap, folded back further as the pin comes out.
        float flapLift = 26f * open01;
        var flap = new Rect2(950, 556 - flapLift * 0.4f, 134, 30 - flapLift * 0.35f);
        DrawRect(flap, new Color(0.22f, 0.14f, 0.09f, 0.98f), true);
        DrawRect(flap, new Color(0.5f, 0.37f, 0.22f, 0.85f), false, 1.5f);
        // The clasp catching what light there is.
        DrawRect(new Rect2(1006, 600, 22, 14), new Color(0.72f, 0.56f, 0.3f, 0.95f), true);
    }

    private void DrawWorkingLine()
    {
        // The hairpin IS the working line now: no separate hand, no
        // guide — Mum's bent pin lies along the seal's radius like a
        // clock hand, its two tips reaching the rubber, its loop
        // riding back over the seal's heart, and the whole thing
        // turns with the work. The pin's art runs bend-to-tips on
        // the diagonal, so a quarter-turn's correction lays its
        // long axis exactly along the needle's angle. Before the
        // counting starts, it travels up out of the bag — bottom
        // right, where her bag hangs — and settles onto the seal.
        Vector2 dir = Vector2.FromAngle(_needleAngle);
        Vector2 needlePos = SealCenter + dir * (SealRadius * 0.3f);
        float needleAngle = _needleAngle - Mathf.Pi / 4f;

        Vector2 pinPos = needlePos;
        float pinAngle = needleAngle;
        float pinSize = 152f;
        if (_pinIntro < 1f)
        {
            float eased = _pinIntro * _pinIntro * (3f - 2f * _pinIntro);
            // Her bag hangs bottom-right, ON screen — the pin's
            // whole errand happens where eyes can follow it: first
            // it rises clear of the bag's mouth, then it travels.
            DrawBagProp(Mathf.Clamp(_pinIntro * 3f, 0f, 1f));
            Vector2 bagPos = new(1008f, 566f);
            pinPos = bagPos.Lerp(needlePos, eased);
            pinPos.Y -= Mathf.Sin(eased * Mathf.Pi) * 52f;
            pinAngle = Mathf.Lerp(0.3f, needleAngle, eased);
            pinSize = Mathf.Lerp(118f, 152f, eased);
        }
        else
        {
            // The pivot her thumb would be, under the pin's loop.
            DrawCircle(SealCenter, 5f, new Color(0.45f, 0.42f, 0.38f, 0.9f));
        }

        float half = pinSize * 0.5f;
        if (_hairpinTexture != null)
        {
            DrawSetTransform(pinPos + new Vector2(2, 3), pinAngle, Vector2.One);
            DrawTextureRect(_hairpinTexture, new Rect2(-half, -half, pinSize, pinSize), false,
                new Color(0f, 0f, 0f, 0.35f));
            DrawSetTransform(pinPos, pinAngle, Vector2.One);
            DrawTextureRect(_hairpinTexture, new Rect2(-half, -half, pinSize, pinSize), false);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        else
        {
            var steel = new Color(0.88f, 0.86f, 0.8f, 0.98f);
            Vector2 side = new(-dir.Y, dir.X);
            DrawLine(pinPos + side * 4f, pinPos + dir * 22f + side * 4f, steel, 3f, true);
            DrawLine(pinPos - side * 4f, pinPos + dir * 22f - side * 4f, steel, 3f, true);
        }

        if (_tapFlash > 0f)
        {
            DrawCircle(SealCenter + Vector2.FromAngle(_targetAngle) * SealRadius,
                12f * (_tapFlash / 0.28f) + 4f,
                new Color(1f, 0.88f, 0.5f, 0.5f * (_tapFlash / 0.28f)));
        }
    }

    private void DrawLeaveControl()
    {
        bool hover = LeaveRect.HasPoint(GetGlobalMousePosition()) && InputFree();
        DrawRect(LeaveRect,
            hover ? new Color(0.2f, 0.14f, 0.07f, 0.92f) : new Color(0.05f, 0.04f, 0.03f, 0.84f),
            true);
        DrawRect(LeaveRect, new Color(0.78f, 0.63f, 0.36f, hover ? 0.95f : 0.6f), false, 1.5f);
        if (_leaveLabel != null)
            _leaveLabel.Modulate = hover ? new Color(1.08f, 1.02f, 0.88f) : Colors.White;
    }
}

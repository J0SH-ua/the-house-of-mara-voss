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
/// moment; the hairpin is back in the kitchen drawer, beside the keys,
/// where hands left it.
///
/// With the hairpin, the seal becomes work: press and drag it slowly
/// all the way around the rim, stretch by ticking stretch, until the
/// lid sighs open instead of breaking. Inside, in layers, lie the two
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
    private const string Room3ScenePath = "res://scenes/world/room_3.tscn";
    private const string JourneyScenePath = "res://scenes/world/journey_to_room_4.tscn";
    private const string HairpinFlag = "item_hairpin";
    private const string CircleKeyFlag = "item_hall_key_circle";
    private const string JarOpenedFlag = "jar_opened";
    private const string BareAttemptFlag = "jar_attempted_barehanded";

    private static readonly Vector2 JarCenter = new(576, 372);
    private static readonly Vector2 SealCenter = new(576, 258);
    private const float SealRadius = 98f;
    private const float PryNeededRadians = 5.2f; // ≈ 300° of patient rim

    private static readonly Rect2 JarRect = new(452, 196, 248, 330);
    private static readonly Rect2 TapeRect = new(526, 306, 100, 44);
    private static readonly Rect2 KeyRect = new(530, 420, 92, 92);
    private static readonly Rect2 LeaveRect = new(28, 572, 252, 44);

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
    private Label _helpLabel;
    private Label _leaveLabel;

    private JarState _state = JarState.Intro;
    private bool _busy;
    private bool _hasHairpin;
    private bool _dragging;
    private float _lastDragAngle;
    private bool _dragAngleReady;
    private float _pryRadians;
    private int _pryTicks;
    private float _crackProgress;
    private float _lidLift;
    private bool _lidPopped;
    private bool _cursorOwned;

    private bool TapeTaken => GameState.Instance?.IsShardRecovered(2) ?? false;
    private bool KeyTaken => GameState.Instance?.HasFlag(CircleKeyFlag) ?? false;
    private bool JarOpened => (GameState.Instance?.HasFlag(JarOpenedFlag) ?? false) || TapeTaken;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>(FontPath);
        _circleKeyTexture = GD.Load<Texture2D>(CircleKeyIconPath);

        var gs = GameState.Instance;
        _hasHairpin = gs?.HasFlag(HairpinFlag) ?? false;
        if (gs?.HasFlag(BareAttemptFlag) == true)
            _crackProgress = 1f;
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
            JarState.Sealed when _hasHairpin => "Press and drag the hairpin slowly around the seal — all the way round, like she did.",
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
                await Say("The last jar. Its seal has had twelve years to become one with the glass. But I watched Mum do this a hundred times — hairpin under the rubber, and patience, worked all the way round.");
            }
            else if (gs?.HasFlag(BareAttemptFlag) == true)
            {
                await Say("The crack is still there, exactly where my impatience left it. Glass remembers being forced. So do I, now.");
            }
            else
            {
                await Say("The last jar. Whichever of Ida's spiders this one held, the seal has had twelve years to become one with the glass.");
            }
            if (!StillHere())
                return;
            UpdateHelp();
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private bool InputFree() =>
        !_busy && !GetTree().Paused && !Dialogue.IsShowing && !Dialogue.IsInputBlocked &&
        !(GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton ||
            mouseButton.ButtonIndex != MouseButton.Left)
            return;

        Vector2 pos = GetGlobalMousePosition();

        if (!mouseButton.Pressed)
        {
            if (_dragging)
            {
                _dragging = false;
                _dragAngleReady = false;
                GetViewport().SetInputAsHandled();
            }
            return;
        }

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
                if (NearSeal(pos))
                {
                    _dragging = true;
                    _dragAngleReady = false;
                    GetViewport().SetInputAsHandled();
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

    private static bool NearSeal(Vector2 pos)
    {
        float r = (pos - SealCenter).Length();
        return r >= SealRadius - 44f && r <= SealRadius + 40f;
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

        if (_dragging && _state == JarState.Sealed && InputFree())
            DragPryStep();
        else if (_dragging && !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _dragging = false;
            _dragAngleReady = false;
        }

        QueueRedraw();
    }

    private void DragPryStep()
    {
        Vector2 mouse = GetGlobalMousePosition();
        Vector2 v = mouse - SealCenter;
        float r = v.Length();
        if (r < SealRadius - 52f || r > SealRadius + 52f)
        {
            _dragAngleReady = false;
            return;
        }

        float angle = Mathf.Atan2(v.Y, v.X);
        if (!_dragAngleReady)
        {
            _lastDragAngle = angle;
            _dragAngleReady = true;
            return;
        }

        float deltaAngle = angle - _lastDragAngle;
        while (deltaAngle > Mathf.Pi)
            deltaAngle -= Mathf.Tau;
        while (deltaAngle < -Mathf.Pi)
            deltaAngle += Mathf.Tau;
        _lastDragAngle = angle;
        _pryRadians += Mathf.Abs(deltaAngle);

        int ticks = Mathf.FloorToInt(_pryRadians / (Mathf.Pi / 4f));
        if (ticks != _pryTicks)
        {
            _pryTicks = ticks;
            AudioManager.Play(Sfx.LockpickTick, -14f, 0.9f + ticks * 0.05f);
            Hotspots.SpawnRipple(SealCenter + v.Normalized() * SealRadius,
                new Color(1f, 0.88f, 0.55f));
        }

        if (_pryRadians >= PryNeededRadians)
        {
            _dragging = false;
            _ = PopLidAsync();
        }
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
            await Say("A hairpin, then. Mum's hands left bent ones all over that house. One of them will still be where a hand would put it down.");
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
            (_state == JarState.Sealed && !_hasHairpin && JarRect.HasPoint(pos)));
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
        DrawJar();
        if (_state == JarState.Sealed && _hasHairpin)
            DrawHairpin();
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

        // The seal ring the hairpin works around (drawn beneath the lid
        // once popped: pale while sealed shut, gold where it has given).
        if (!_lidPopped)
        {
            DrawArc(SealCenter, SealRadius, 0f, Mathf.Tau, 64,
                new Color(0.85f, 0.8f, 0.68f, 0.35f), 5f, true);
            float given = Mathf.Clamp(_pryRadians / PryNeededRadians, 0f, 1f);
            if (given > 0.01f)
            {
                DrawArc(SealCenter, SealRadius, -Mathf.Pi / 2f,
                    -Mathf.Pi / 2f + given * Mathf.Tau * 0.92f, 64,
                    new Color(1f, 0.82f, 0.42f, 0.9f), 5f, true);
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

    private void DrawHairpin()
    {
        Vector2 mouse = GetGlobalMousePosition();
        if (!JarRect.Grow(60f).HasPoint(mouse))
            return;

        // The pin in her fingers: two legs and the bend, tipped toward
        // the seal wherever her hand is.
        Vector2 toward = (SealCenter - mouse).Normalized();
        if (toward == Vector2.Zero)
            toward = Vector2.Up;
        Vector2 side = new(-toward.Y, toward.X);
        var steel = new Color(0.85f, 0.83f, 0.78f, 0.98f);
        var shadow = new Color(0.3f, 0.29f, 0.27f, 0.98f);
        Vector2 tipA = mouse + toward * 26f + side * 4f;
        Vector2 tipB = mouse + toward * 26f - side * 4f;
        Vector2 backA = mouse - toward * 30f + side * 4f;
        Vector2 backB = mouse - toward * 30f - side * 4f;
        DrawLine(tipA + new Vector2(1.5f, 1.5f), backA + new Vector2(1.5f, 1.5f), shadow, 3.5f, true);
        DrawLine(tipB + new Vector2(1.5f, 1.5f), backB + new Vector2(1.5f, 1.5f), shadow, 3.5f, true);
        DrawLine(tipA, backA, steel, 3f, true);
        DrawLine(tipB, backB, steel, 3f, true);
        DrawLine(backA, backB, steel, 3f, true);
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

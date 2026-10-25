using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Her father's trade, remembered with Mara hands.
///
/// A / D rocks the pick across the pins. Holding the left mouse button
/// and dragging sideways turns the tension wrench and the plug with it.
/// Near the hidden sweet spot the plug travels; away from it the plug
/// binds early, the pick starts to complain, and forcing the drag can
/// snap a pick. Four picks (the one in the lock and three spares) are
/// allowed per sitting — running out only sends Mara back to breathe
/// and begin again; the set itself is never lost.
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
    private const int PicksPerSitting = 4;

    private enum PickState
    {
        Intro,
        Picking,
        Broken,
        Success,
        Leaving,
    }

    private static readonly Rect2 LeaveRect = new(28, 572, 226, 44);
    private static readonly Vector2 LockCenter = new(576, 324);
    private static readonly string[] NearLines =
    [
        "There. Did you hear that? The little clean sound. Stay there.",
        "That's not the knock. That's the pins. Easy. Easy.",
    ];

    private Texture2D _background;
    private FontFile _font;
    private Label _titleLabel;
    private Label _helpLabel;
    private Label _statusLabel;
    private Label _leaveLabel;

    private bool _targetTrunk;
    private float _sweetDegrees;
    private float _toleranceDegrees;
    private float _stressScale;
    private float _pickDegrees;
    private float _plugDegrees;
    private float _stress;
    private int _picksLeft = PicksPerSitting;
    private int _lastPickTickBand;
    private int _lastPlugTickStep;
    private bool _dragging;
    private bool _nearLineSaid;
    private bool _leaveCursorOwned;
    private PickState _state = PickState.Intro;

    public override void _Ready()
    {
        _background = GD.Load<Texture2D>(BackgroundPath);
        _font = GD.Load<FontFile>(FontPath);

        var gs = GameState.Instance;
        _targetTrunk = gs != null &&
            (gs.HasFlag("lockpick_target_trunk") ||
             (!gs.HasFlag("lockpick_target_vent") && gs.HasFlag("room5_vent_open")));
        _toleranceDegrees = _targetTrunk ? 7f : 11f;
        _stressScale = _targetTrunk ? 1.3f : 1f;

        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _sweetDegrees = rng.RandfRange(-58f, 58f);
        _lastPickTickBand = Mathf.RoundToInt(_pickDegrees / 6f);

        // A is also Anchor elsewhere in the house. Here it belongs to the
        // pick alone; verb/clarity modes do not enter the lock with her.
        MemoryVerbSystem.Cancel(silent: true);
        ClaritySystem.Disarm(silent: true);

        BuildLabels();
        QueueRedraw();
        _ = PlayIntroAsync();
    }

    private void BuildLabels()
    {
        _titleLabel = MakeLabel(
            _targetTrunk ? "The little trunk's lock" : "The vent latch",
            new Vector2(326, 18), new Vector2(500, 38), 29,
            new Color(0.96f, 0.9f, 0.76f, 0.97f));
        _helpLabel = MakeLabel(
            "A / D — rock the pick      Hold + drag mouse — turn the wrench\nLet go when it complains. Force breaks picks.",
            new Vector2(276, 58), new Vector2(600, 62), 20,
            new Color(0.9f, 0.86f, 0.76f, 0.94f));
        _statusLabel = MakeLabel(string.Empty,
            new Vector2(326, 560), new Vector2(500, 42), 21,
            new Color(0.94f, 0.9f, 0.8f, 0.97f));
        _leaveLabel = MakeLabel("Put the tools away",
            LeaveRect.Position, LeaveRect.Size, 20,
            new Color(0.95f, 0.88f, 0.72f, 0.97f));
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
                await Say("Small lock. Old lock. The kind Dad called honest — which meant it would tell you exactly where you were wrong.");
            }
            else
            {
                await Say("Pick across the pins. Wrench under it. Dad's voice, from over my shoulder: don't push a sound you don't like.");
            }
            if (!StillHere())
                return;
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
        // Mouse work waits for HUD/GUI first, so Bag, Journal, Pause and
        // navigation remain ordinary buttons over the lock close-up.
        if (_state != PickState.Picking || GetTree().Paused || Dialogue.IsShowing)
            return;

        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                GetViewport().SetInputAsHandled();
                if (LeaveRect.HasPoint(mouseButton.Position))
                {
                    _ = LeaveAsync();
                    return;
                }
                _dragging = true;
            }
            else if (_dragging)
            {
                _dragging = false;
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_dragging && @event is InputEventMouseMotion motion)
        {
            GetViewport().SetInputAsHandled();
            DragWrench(motion.Relative.X);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateCursor();

        if (_state != PickState.Picking)
        {
            QueueRedraw();
            return;
        }

        // If a pause/panel swallowed the physical release, do not let a
        // later bare mouse-move keep turning the wrench.
        if (_dragging && !Input.IsMouseButtonPressed(MouseButton.Left))
            _dragging = false;

        float direction = 0f;
        if (Input.IsPhysicalKeyPressed(Key.A))
            direction -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D))
            direction += 1f;

        if (direction != 0f && !Dialogue.IsShowing && !GetTree().Paused)
        {
            _pickDegrees = Mathf.Clamp(
                _pickDegrees + direction * 105f * dt,
                -MaxPickDegrees, MaxPickDegrees);
            int band = Mathf.RoundToInt(_pickDegrees / 6f);
            if (band != _lastPickTickBand)
            {
                _lastPickTickBand = band;
                PlayPickTick();
            }
        }

        if (!_dragging)
        {
            // Released, the plug springs home and the pick forgives her.
            _plugDegrees = Mathf.MoveToward(_plugDegrees, 0f, 85f * dt);
            _stress = Mathf.MoveToward(_stress, 0f, 0.5f * dt);
        }
        else if (PickError() <= _toleranceDegrees)
        {
            _stress = Mathf.MoveToward(_stress, 0f, 0.16f * dt);
            if (!_nearLineSaid && _plugDegrees > 22f)
            {
                _nearLineSaid = true;
                GameState.Instance?.Toast(NearLines[_targetTrunk ? 1 : 0]);
            }
        }

        int plugStep = Mathf.FloorToInt(_plugDegrees / 12f);
        if (plugStep != _lastPlugTickStep)
        {
            _lastPlugTickStep = plugStep;
            if (_dragging)
                AudioManager.Play(Sfx.LockpickTick, -15f, 0.82f + plugStep * 0.045f);
        }

        if (_stress >= 1f)
            _ = BreakPickAsync();

        UpdateStatus();
        QueueRedraw();
    }

    private void DragWrench(float mouseDeltaX)
    {
        if (_state != PickState.Picking || mouseDeltaX == 0f)
            return;

        float requested = Mathf.Clamp(mouseDeltaX * 0.24f, -3.2f, 3.2f);
        if (requested < 0f)
        {
            _plugDegrees = Mathf.Max(0f, _plugDegrees + requested);
            _stress = Mathf.MoveToward(_stress, 0f, 0.05f);
            return;
        }

        float error = PickError();
        if (error <= _toleranceDegrees)
        {
            _plugDegrees = Mathf.Min(PlugOpenDegrees, _plugDegrees + requested);
            _stress = Mathf.MoveToward(_stress, 0f, 0.035f);
        }
        else
        {
            // The closer the pick, the farther the plug can travel before
            // the wrong pins catch it. This travel is the readable clue.
            float bindLimit = Mathf.Clamp(68f - error * 0.68f, 17f, 62f);
            float before = _plugDegrees;
            _plugDegrees = Mathf.Min(bindLimit, _plugDegrees + requested);
            float travelled = Mathf.Max(0f, _plugDegrees - before);
            float forced = Mathf.Max(0f, requested - travelled);
            _stress += (travelled * 0.006f + forced * 0.055f) * _stressScale;
            if (forced > 0.2f)
                AudioManager.Play(Sfx.LockpickTick, -13f, 0.62f);
        }

        if (_plugDegrees >= PlugOpenDegrees - 0.5f)
            _ = SucceedAsync();
    }

    private float PickError() => Mathf.Abs(_pickDegrees - _sweetDegrees);

    private void PlayPickTick()
    {
        float closeness = 1f - Mathf.Clamp(PickError() / 70f, 0f, 1f);
        AudioManager.Play(
            Sfx.LockpickTick,
            -18f + closeness * 5f,
            0.76f + closeness * 0.42f);
    }

    private async Task BreakPickAsync()
    {
        if (_state != PickState.Picking)
            return;

        _state = PickState.Broken;
        _dragging = false;
        _picksLeft = Mathf.Max(0, _picksLeft - 1);
        _plugDegrees = 0f;
        _stress = 0f;
        AudioManager.Play(Sfx.LockpickSnap, -5f);
        Dialogue.BeginInputBlock();
        try
        {
            await WaitSeconds(0.45f);
            if (!StillHere())
                return;

            if (_picksLeft > 0)
            {
                await Say(_picksLeft == 1
                    ? "Snap. One pick left in the roll. Dad is doing the face. I can feel the face from here."
                    : "Snap. Sorry, Dad. The roll has spares because daughters have wrists.");
                if (!StillHere())
                    return;
                _state = PickState.Picking;
                UpdateStatus();
            }
            else
            {
                await Say("That's the last pick in the roll willing to be looked at. The lock can keep its secret while my hands stop shaking.");
                if (StillHere())
                    ReturnFromLock();
            }
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private async Task SucceedAsync()
    {
        if (_state != PickState.Picking)
            return;

        _state = PickState.Success;
        _dragging = false;
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
        _dragging = false;
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
        string pickWord = _picksLeft == 1 ? "pick" : "picks";
        _statusLabel.Text = _state switch
        {
            PickState.Success => "It turns.",
            PickState.Broken when _picksLeft > 0 => $"A pick snapped — {_picksLeft} {pickWord} left.",
            PickState.Broken => "No picks left for this sitting.",
            _ => $"{_picksLeft} {pickWord} in the roll",
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
        DrawStress();

        // A held-breath vignette, in plain strips so it stays in the
        // game's chunky visual language.
        DrawRect(new Rect2(0, 0, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
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
        if (_stress > 0.42f && _state == PickState.Picking &&
            !Settings.ReduceMotionOrDefault())
        {
            float shake = (_stress - 0.42f) * 7f;
            jitter = side * (float)Math.Sin(Time.GetTicksMsec() / 28.0) * shake;
        }

        var steel = new Color(0.84f, 0.85f, 0.82f, 0.99f);
        var darkSteel = new Color(0.32f, 0.33f, 0.33f, 0.99f);
        Vector2 tip = LockCenter + direction * 12f + jitter;
        Vector2 shaftEnd = LockCenter + direction * 176f + jitter;
        Vector2 handleEnd = LockCenter + direction * 258f + jitter;

        if (_state == PickState.Broken)
        {
            Vector2 fallen = tip + new Vector2(18, 38);
            DrawLine(tip, fallen, steel, 4f, true);
            DrawLine(shaftEnd + new Vector2(-10, 16), handleEnd, darkSteel, 13f, true);
            DrawLine(shaftEnd + new Vector2(-42, 10), shaftEnd + new Vector2(-10, 16), steel, 4f, true);
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

    private void DrawStress()
    {
        var frame = new Rect2(426, 528, 300, 12);
        DrawRect(frame, new Color(0.02f, 0.02f, 0.02f, 0.82f), true);
        DrawRect(frame, new Color(0.7f, 0.62f, 0.48f, 0.65f), false, 1f);
        if (_stress <= 0.01f)
            return;
        var fill = new Rect2(frame.Position, new Vector2(frame.Size.X * Mathf.Clamp(_stress, 0f, 1f), frame.Size.Y));
        Color color = _stress < 0.5f
            ? new Color(0.95f, 0.76f, 0.38f, 0.95f)
            : new Color(0.92f, 0.32f, 0.22f, 0.98f);
        DrawRect(fill, color, true);
    }

    private static Vector2 DirectionFromDegrees(float degrees)
    {
        float rad = Mathf.DegToRad(degrees);
        return new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
    }
}

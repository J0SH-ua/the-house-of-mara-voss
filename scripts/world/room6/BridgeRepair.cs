using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The bridge on the empty road — the last broken thing between the
/// garden and the porch light.
///
/// The road out of Room 6 crosses water, and in this memory the
/// crossing is broken the way it broke her in life: two planks gone
/// to kindling, one hanging by its nails, a railing that gave. Mara
/// cannot cross it on wishes. Without her father's hammer and nails
/// (the little tool cabinet under the house, behind the iron cellar
/// key) she can only stand here and know whose tools would mend it.
///
/// With them, it is work, at elbow height over October water: six
/// nails, two to a plank, each wanting two true strikes landed as
/// its glint crosses the gold. A glanced blow shifts the plank and
/// puts her within the water's reach — every fall strains MEMORY,
/// and enough of them will break it outright, the ordinary way.
/// Nailed planks stay nailed, whatever the water says. When the
/// last one holds, the bridge is only a bridge again, and the porch
/// light is the rest of the way.
/// </summary>
public partial class BridgeRepair : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    private const string BackgroundPath = "res://assets/images/bgs/Journey_Empty_Road.png";
    private const string Room6ScenePath = "res://scenes/world/room_6.tscn";
    private const string PorchScenePath = "res://scenes/world/porch_447.tscn";
    private const string ToolsFlag = "item_hammer_nails";
    private const string FixedFlag = "bridge_fixed";

    private const int PlankCount = 7;
    private const int FirstBrokenPlank = 2; // planks 2,3,4 are down
    private const int StrikesPerNail = 2;
    private const float DeckTop = 452f;
    private const float DeckBottom = 486f;
    private const float SpanLeft = 121f;
    private const float PlankWidth = 130f;

    private static readonly Rect2 LeaveRect = new(16, 468, 216, 44);

    private enum BridgeState
    {
        Intro,
        NoTools,
        Hammering,
        CompleteBeat,
        Crossable,
        Leaving,
    }

    private static readonly string[] FallLines =
    [
        "—! The plank tips— the rail— got it. Got it. Heart, behave.",
        "The whole span argues. Down there is October water and I am NOT— just— breathe.",
        "Again. Slower. He'd say: let the hammer fall, Mara. Don't throw it.",
        "My hands are shaking worse than the bridge. Which is saying something.",
    ];

    private Texture2D _background;
    private FontFile _font;
    private Label _helpLabel;
    private Label _leaveLabel;
    private readonly RandomNumberGenerator _rng = new();

    private BridgeState _state = BridgeState.Intro;
    private bool _busy;
    private bool _hasTools;
    private int _strikesOnCurrent;
    private int _falls;
    private float _lurch;
    private bool _cursorOwned;
    private bool _hintedAim;

    private static string NailFlag(int nail) => $"bridge_nail_{nail + 1}";

    private bool NailDone(int nail) =>
        GameState.Instance?.HasFlag(NailFlag(nail)) ?? false;

    private int NailsDone
    {
        get
        {
            int n = 0;
            for (int i = 0; i < 6; i++)
                if (NailDone(i)) n++;
            return n;
        }
    }

    private int CurrentNail
    {
        get
        {
            for (int i = 0; i < 6; i++)
                if (!NailDone(i)) return i;
            return -1;
        }
    }

    private bool BridgeFixed => GameState.Instance?.HasFlag(FixedFlag) ?? false;

    private static Vector2 NailPosition(int nail)
    {
        int plank = FirstBrokenPlank + nail / 2;
        float cx = SpanLeft + plank * PlankWidth + PlankWidth * 0.5f;
        return nail % 2 == 0
            ? new Vector2(cx - 36f, DeckTop + 9f)
            : new Vector2(cx + 36f, DeckTop + 23f);
    }

    private float Glint(int nail)
    {
        float freq = 0.85f + 0.13f * NailsDone;
        double seconds = Time.GetTicksMsec() / 1000.0;
        return 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * freq * seconds + nail * 1.7);
    }

    public override void _Ready()
    {
        _background = GD.Load<Texture2D>(BackgroundPath);
        _font = GD.Load<FontFile>(FontPath);
        _rng.Randomize();
        _hasTools = GameState.Instance?.HasFlag(ToolsFlag) ?? false;
        BuildLabels();
        QueueRedraw();
        _ = PlayIntroAsync();
    }

    private void BuildLabels()
    {
        MakeLabel("The bridge, on the empty road",
            new Vector2(376, 22), new Vector2(400, 38), 29,
            new Color(0.96f, 0.9f, 0.76f, 0.97f));
        _helpLabel = MakeLabel(string.Empty,
            new Vector2(276, 566), new Vector2(600, 38), 20,
            new Color(0.9f, 0.86f, 0.76f, 0.94f));
        _leaveLabel = MakeLabel(
            BridgeFixed ? "Cross to the porch light" : "Back to the garden",
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
            BridgeState.NoTools => "Bare hands won't mend this. Somewhere below the house, her father's tools are waiting.",
            BridgeState.Hammering => $"Strike as the glint crosses the gold — two true strikes a nail.  ·  {NailsDone} of 6 nails home",
            BridgeState.Crossable => "It holds. Click the far side to cross.",
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

            if (BridgeFixed)
            {
                _state = BridgeState.Crossable;
                await Say("The bridge. Nailed true, holding quiet. It is only a bridge now. That's allowed to be enough.");
                return;
            }

            await Say("The road goes down to the water here. It always did. Stones. A wooden span. A railing—");
            if (!StillHere())
                return;
            await Say("Stop. Breathe. I know where I am. I have always known where I am; that's rather the problem.");
            if (!StillHere())
                return;
            await Say("It's broken. Planks gone to kindling, one hanging by its nails. In this memory it's broken the way it broke— no. The way it broke her. The way it broke everything after.");
            if (!StillHere())
                return;

            if (!_hasTools)
            {
                _state = BridgeState.NoTools;
                await Say("I am not crossing that on wishes. Dad mended every crossing in this parish twice over. His tools are below the house, in his little cabinet — the iron key's lock. That's where mending lives.");
                if (!StillHere())
                    return;
                GameState.Instance?.Toast("Something of her father's could mend this: his tool cabinet, under the house. Step Back to the garden when ready.");
                UpdateHelp();
                return;
            }

            _state = BridgeState.Hammering;
            await Say("Hammer. Nails. Her father's hands, borrowed. …Okay. One plank at a time. I am not thinking about the water. I am thinking about nails.");
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
            mouseButton.ButtonIndex != MouseButton.Left || !mouseButton.Pressed)
            return;
        if (!InputFree())
            return;

        Vector2 pos = GetGlobalMousePosition();

        if (LeaveRect.HasPoint(pos))
        {
            GetViewport().SetInputAsHandled();
            _ = LeaveAsync();
            return;
        }

        if (_state == BridgeState.Crossable)
        {
            GetViewport().SetInputAsHandled();
            _ = LeaveAsync();
            return;
        }

        if (_state == BridgeState.NoTools)
        {
            GetViewport().SetInputAsHandled();
            _ = RefuseCrossingAsync();
            return;
        }

        if (_state == BridgeState.Hammering)
        {
            GetViewport().SetInputAsHandled();
            StrikeAt(pos);
        }
    }

    private void StrikeAt(Vector2 pos)
    {
        int nail = CurrentNail;
        if (nail < 0 || _busy)
            return;

        Vector2 target = NailPosition(nail);
        if (pos.DistanceTo(target) > 48f)
        {
            AudioManager.Play(Sfx.HammerHit, -16f, 0.6f);
            if (!_hintedAim)
            {
                _hintedAim = true;
                GameState.Instance?.Toast("Wood, not nail. The next nail is the one glinting inside the gold ring.");
            }
            return;
        }

        if (Glint(nail) >= 0.82f)
        {
            _strikesOnCurrent++;
            AudioManager.Play(Sfx.HammerHit, -4f, 0.95f + _strikesOnCurrent * 0.08f);
            Hotspots.SpawnRipple(target, new Color(1f, 0.88f, 0.55f));
            if (_strikesOnCurrent >= StrikesPerNail)
            {
                _strikesOnCurrent = 0;
                var gs = GameState.Instance;
                gs?.SetFlag(NailFlag(nail), save: false);
                gs?.SaveGame("bridge nail home");
                AudioManager.Play(Sfx.BridgeCreak, -12f, 1.1f);
                if (CurrentNail < 0)
                {
                    _ = CompleteAsync();
                    return;
                }
            }
            UpdateHelp();
            QueueRedraw();
        }
        else
        {
            _ = FallAsync();
        }
    }

    private async Task FallAsync()
    {
        if (_busy)
            return;
        _busy = true;
        _falls++;
        _lurch = 0.55f;
        _strikesOnCurrent = 0;
        try
        {
            AudioManager.Play(Sfx.HammerGlance, -6f);
            AudioManager.Play(Sfx.BridgeCreak, -5f, 0.9f);
            Dialogue.BeginInputBlock();
            try
            {
                await Say(FallLines[Mathf.Min(_falls - 1, FallLines.Length - 1)]);
            }
            finally
            {
                Dialogue.EndInputBlock();
            }
            // Every fall is paid for in the only currency this house
            // takes. Enough of them, and the memory breaks the
            // ordinary way — nailed planks stay nailed regardless.
            GameState.Instance?.AddCorruption(2);
        }
        finally
        {
            _busy = false;
            if (StillHere())
                QueueRedraw();
        }
    }

    private async Task RefuseCrossingAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            AudioManager.Play(Sfx.BridgeCreak, -10f, 0.8f);
            await Say("No. Not on that. Not on a plank hanging by one nail and twelve years of weather. Tools first. Like he taught every fence in Eldfin.");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task CompleteAsync()
    {
        if (_busy)
            return;
        _busy = true;
        _state = BridgeState.CompleteBeat;
        Dialogue.BeginInputBlock();
        try
        {
            AudioManager.Play(Sfx.BridgeCreak, -9f, 1.15f);
            AudioManager.Play(Sfx.PuzzleSolve, -9f, 0.95f);
            var gs = GameState.Instance;
            gs?.NotifyPuzzleSolved();
            gs?.SetFlag(FixedFlag, save: false);
            gs?.SaveGame("bridge mended");
            if (_leaveLabel != null)
                _leaveLabel.Text = "Cross to the porch light";
            QueueRedraw();

            await Say("Last nail. …It holds. It just— holds. Like it was only ever a bridge.");
            if (!StillHere())
                return;
            await Say("I can't mend the other one. The real one. Nobody can mend a railing twelve years after. But this one, in here, will carry me. That's— that's allowed to matter.");
            if (!StillHere())
                return;
            _state = BridgeState.Crossable;
            UpdateHelp();
        }
        finally
        {
            Dialogue.EndInputBlock();
            _busy = false;
            if (StillHere())
                QueueRedraw();
        }
    }

    private async Task LeaveAsync()
    {
        if (_busy || _state == BridgeState.Leaving)
            return;
        BridgeState from = _state;
        _state = BridgeState.Leaving;
        await WaitSeconds(0.15f);
        if (!StillHere())
            return;

        if (BridgeFixed || from == BridgeState.Crossable)
        {
            AudioManager.PlayVaried(Sfx.FootstepDirtA, -11f, 0.95f, 0.03f);
            SceneManager.ChangeScene(PorchScenePath);
        }
        else
        {
            AudioManager.PlayVaried(Sfx.FootstepDirtB, -12f, 0.9f, 0.03f);
            SceneManager.ChangeScene(Room6ScenePath);
        }

        await WaitSeconds(0.6f);
        if (StillHere() && _state == BridgeState.Leaving)
            _state = from;
    }

    public override void _Process(double delta)
    {
        _lurch = Mathf.Max(0f, _lurch - (float)delta);
        UpdateCursor();
        QueueRedraw();
    }

    private void UpdateCursor()
    {
        Vector2 pos = GetGlobalMousePosition();
        bool over = InputFree() && (LeaveRect.HasPoint(pos) ||
            _state == BridgeState.Crossable ||
            (_state == BridgeState.Hammering && CurrentNail >= 0 &&
             pos.DistanceTo(NailPosition(CurrentNail)) <= 52f));
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
        bool lurching = _lurch > 0.01f && !Settings.ReduceMotionOrDefault();
        if (lurching)
        {
            float s = 7f * (_lurch / 0.55f);
            DrawSetTransform(new Vector2(
                _rng.RandfRange(-s, s), _rng.RandfRange(-s * 0.5f, s * 0.8f)),
                _rng.RandfRange(-0.006f, 0.006f) * (_lurch / 0.55f), Vector2.One);
        }

        DrawRoad();
        DrawBridge();
        if (_state == BridgeState.Hammering)
            DrawHammer();

        if (lurching)
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        DrawLeaveControl();
        DrawRect(new Rect2(0, 0, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
    }

    private void DrawRoad()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        if (_background != null)
            DrawTextureRect(_background, screen, false);
        else
            DrawRect(screen, new Color(0.06f, 0.07f, 0.1f), true);
        // Dusk sits a little deeper at the water.
        DrawRect(screen, new Color(0.02f, 0.03f, 0.06f, 0.22f), true);
    }

    private bool PlankFixed(int plank)
    {
        if (BridgeFixed)
            return true;
        int local = plank - FirstBrokenPlank;
        if (local < 0 || local > 2)
            return true;
        return NailDone(local * 2) && NailDone(local * 2 + 1);
    }

    private void DrawBridge()
    {
        // The canal, doing its October best to be looked at last.
        var water = new Rect2(0, DeckBottom + 8f, 1152, 648f - DeckBottom - 8f);
        DrawRect(water, new Color(0.03f, 0.06f, 0.085f, 0.97f), true);
        float drift = (float)(Time.GetTicksMsec() / 1000.0);
        var glint = new Color(0.75f, 0.82f, 0.9f, 0.16f);
        for (int i = 0; i < 9; i++)
        {
            float y = DeckBottom + 30f + i * 16f;
            float x = (80f + i * 137f + drift * (6f + i)) % 1152f;
            DrawLine(new Vector2(x, y), new Vector2(x + 34f, y), glint, 2f, true);
        }

        // Banks and abutments.
        var stone = new Color(0.24f, 0.25f, 0.26f, 1f);
        DrawRect(new Rect2(SpanLeft - 46f, DeckTop - 6f, 46, 120), stone, true);
        DrawRect(new Rect2(SpanLeft + PlankCount * PlankWidth, DeckTop - 6f, 46, 120), stone, true);

        // Stringers under the deck.
        DrawRect(new Rect2(SpanLeft - 10f, DeckBottom, PlankCount * PlankWidth + 20f, 10),
            new Color(0.16f, 0.11f, 0.07f), true);

        for (int plank = 0; plank < PlankCount; plank++)
            DrawPlank(plank);
        DrawRailing();
        if (_state == BridgeState.Hammering || _state == BridgeState.CompleteBeat)
            DrawNails();
    }

    private void DrawPlank(int plank)
    {
        float x = SpanLeft + plank * PlankWidth;
        bool fix = PlankFixed(plank);
        var top = new Color(0.34f, 0.23f, 0.13f);
        var side = new Color(0.2f, 0.13f, 0.075f);
        var edge = new Color(0.5f, 0.36f, 0.2f, 0.85f);

        if (fix)
        {
            var rect = new Rect2(x + 2, DeckTop, PlankWidth - 4, DeckBottom - DeckTop);
            DrawRect(rect, side, true);
            DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 9)), top, true);
            DrawRect(rect, edge, false, 1.5f);
            return;
        }

        // Sagging, twisted a thumb's width out of true.
        float cx = x + PlankWidth * 0.5f;
        float tilt = plank % 2 == 0 ? 0.055f : -0.048f;
        DrawSetTransform(new Vector2(cx, DeckTop + 12f), tilt, Vector2.One);
        var local = new Rect2(-PlankWidth * 0.5f + 2, -12, PlankWidth - 4, DeckBottom - DeckTop);
        DrawRect(local, side, true);
        DrawRect(new Rect2(local.Position, new Vector2(local.Size.X, 9)), top, true);
        DrawRect(local, edge, false, 1.5f);
        // The splintered break, mid-plank.
        DrawLine(new Vector2(-14, -12), new Vector2(-4, 12), edge, 2f, true);
        DrawLine(new Vector2(-4, 12), new Vector2(8, -2), edge, 2f, true);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawRailing()
    {
        var rail = new Color(0.28f, 0.19f, 0.11f, 1f);
        var railTop = new Color(0.42f, 0.29f, 0.16f, 1f);
        float postTop = DeckTop - 52f;
        for (int i = 0; i <= PlankCount; i++)
        {
            float x = SpanLeft + i * PlankWidth;
            DrawRect(new Rect2(x - 4, postTop, 8, DeckTop - postTop), rail, true);
        }
        // The rail itself, in plank-length runs. Over the broken
        // middle it hangs splintered until its planks hold.
        for (int plank = 0; plank < PlankCount; plank++)
        {
            float x = SpanLeft + plank * PlankWidth;
            if (PlankFixed(plank))
            {
                DrawLine(new Vector2(x, postTop + 3), new Vector2(x + PlankWidth, postTop + 3), railTop, 7f, true);
            }
            else
            {
                DrawLine(new Vector2(x, postTop + 3), new Vector2(x + PlankWidth * 0.4f, postTop + 16), railTop, 6f, true);
                DrawLine(new Vector2(x + PlankWidth, postTop + 3), new Vector2(x + PlankWidth * 0.66f, postTop + 20), railTop, 6f, true);
            }
        }
    }

    private void DrawNails()
    {
        int current = CurrentNail;
        for (int nail = 0; nail < 6; nail++)
        {
            Vector2 pos = NailPosition(nail);
            if (NailDone(nail))
            {
                DrawCircle(pos, 4.5f, new Color(0.16f, 0.15f, 0.14f, 0.98f));
                DrawCircle(pos + new Vector2(-1.2f, -1.2f), 1.5f, new Color(0.75f, 0.74f, 0.7f, 0.8f));
                continue;
            }

            bool isCurrent = nail == current;
            float partial = isCurrent && _strikesOnCurrent > 0 ? 0.45f : 0f;
            float r = 5.5f - 1.6f * partial;
            DrawCircle(pos, r + 1.5f, new Color(0.05f, 0.045f, 0.04f, 0.85f));
            DrawCircle(pos, r, new Color(0.78f, 0.77f, 0.72f, 0.98f));

            if (!isCurrent || _state != BridgeState.Hammering)
                continue;

            // The glint travels; the gold waits. Strike as they cross.
            float g = Glint(nail);
            float ringR = 26f - (1f - g) * 12f;
            DrawArc(pos, 14f, 0f, Mathf.Tau, 32,
                new Color(1f, 0.84f, 0.45f, 0.75f), 2.5f, true);
            DrawArc(pos, ringR, 0f, Mathf.Tau, 40,
                new Color(0.9f, 0.88f, 0.82f, 0.35f + 0.5f * g), 2.5f, true);
            if (g >= 0.82f)
                DrawCircle(pos, 3f, new Color(1f, 0.92f, 0.6f, 0.95f));
        }
    }

    private void DrawHammer()
    {
        Vector2 mouse = GetGlobalMousePosition();
        if (!new Rect2(Vector2.Zero, new Vector2(1152, 648)).HasPoint(mouse))
            return;

        // Her father's hammer, in her hand: worn handle, honest head.
        Vector2 grip = mouse + new Vector2(16, 20);
        Vector2 neck = mouse + new Vector2(2, 2);
        var wood = new Color(0.55f, 0.38f, 0.2f, 0.98f);
        var steel = new Color(0.62f, 0.62f, 0.6f, 0.98f);
        DrawLine(grip + new Vector2(1.5f, 1.5f), neck + new Vector2(1.5f, 1.5f),
            new Color(0, 0, 0, 0.45f), 6f, true);
        DrawLine(grip, neck, wood, 5f, true);
        DrawRect(new Rect2(neck + new Vector2(-13, -7), new Vector2(26, 12)), steel, true);
        DrawRect(new Rect2(neck + new Vector2(-13, -7), new Vector2(26, 12)),
            new Color(0.2f, 0.2f, 0.2f, 0.9f), false, 1.5f);
        DrawLine(neck + new Vector2(13, -4), neck + new Vector2(22, 2), steel, 4f, true);
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

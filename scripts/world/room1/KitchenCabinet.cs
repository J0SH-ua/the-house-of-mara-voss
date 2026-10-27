using System.Threading.Tasks;
using Godot;

/// <summary>
/// The cabinet under the kitchen counter — the one this house never
/// showed Mara on the way in, because she never had to remember it
/// before the dark came back.
///
/// It only exists once the blackout has handed her the memory of it:
/// the DrawerExamine1 divert leads here during the power cut, and
/// the counter has simply... more cabinet than it had. Inside are
/// the things a father keeps for exactly this — candles, matches,
/// and on the top shelf, where his hand could find it blind, the
/// big torch. The batteries, in this house, were never once dead.
/// </summary>
public partial class KitchenCabinet : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    private const string FlashlightIconPath = "res://assets/images/core/items/flashlight.png";
    private const string Room1ScenePath = "res://scenes/world/room_1.tscn";
    private const string FlashlightFlag = "item_flashlight";

    private static readonly Rect2 CabinetRect = new(396, 210, 360, 300);
    private static readonly Rect2 FlashlightRect = new(508, 252, 136, 96);
    private static readonly Rect2 LeaveRect = new(16, 468, 216, 44);

    private FontFile _font;
    private Texture2D _flashlightTexture;
    private Label _helpLabel;
    private Label _leaveLabel;

    private bool _busy;
    private bool _doorOpen;
    private float _doorSwing;
    private bool _cursorOwned;
    private bool _left;

    private bool FlashlightTaken => GameState.Instance?.HasFlag(FlashlightFlag) ?? false;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>(FontPath);
        _flashlightTexture = GD.Load<Texture2D>(FlashlightIconPath);
        BuildLabels();
        QueueRedraw();
        _ = PlayIntroAsync();
    }

    private void BuildLabels()
    {
        MakeLabel("Under the counter",
            new Vector2(376, 22), new Vector2(400, 38), 29,
            new Color(0.96f, 0.9f, 0.76f, 0.97f));
        _helpLabel = MakeLabel("The cabinet that wasn't here this morning. Open it the way he did — by feel.",
            new Vector2(276, 566), new Vector2(600, 38), 20,
            new Color(0.9f, 0.86f, 0.76f, 0.94f));
        _leaveLabel = MakeLabel("Back to the kitchen",
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

            // A Clarity rewind lands here: back at the moment the
            // torch came to hand, with no memory of the lock, the
            // breaking, or the price — only the shape of having
            // lost something, already fading.
            var gsRewind = GameState.Instance;
            if (gsRewind != null && gsRewind.HasFlag("lockpick_rewound"))
            {
                gsRewind.ClearFlag("lockpick_rewound", save: false);
                _doorOpen = true;
                _doorSwing = 1f;
                if (_helpLabel != null)
                    _helpLabel.Text = "Candles, matches, and a torch-shaped space. The cabinet has done its providing.";
                await Say("Huh. Feels like I've done this before? — Nevermind. I need to open the vent.");
                if (!StillHere())
                    return;
                return;
            }

            if (FlashlightTaken)
            {
                _doorOpen = true;
                _doorSwing = 1f;
                if (_helpLabel != null)
                    _helpLabel.Text = "Candles, matches, and a torch-shaped space. The cabinet has done its providing.";
                await Say("Candles. Matches standing to attention. And a pale torch-shaped space where it waited twelve years to be needed again.");
                return;
            }

            await Say("There. Under the counter, where the shadow always was. It was here this morning too — I just hadn't needed to remember it yet. That's how this house works. That's how I work, apparently.");
            if (!StillHere())
                return;
            await Say("Dad could find this door one-handed, in the pitch black, with two girls shrieking in the hall on purpose. I watched him do it. My hand remembers even if the rest of me was busy being told off.");
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private bool InputFree() =>
        !_busy && !_left && !GetTree().Paused && !Dialogue.IsShowing && !Dialogue.IsInputBlocked &&
        !(GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());

    public override void _UnhandledInput(InputEvent @event)
    {
        // A click, or F: the house's hand, for the thing under the
        // cursor. Same reach, same rules, either way.
        bool clicked = @event is InputEventMouseButton mouseButton && mouseButton.Pressed &&
            mouseButton.ButtonIndex == MouseButton.Left;
        bool useKey = @event is InputEventKey keyEvent && keyEvent.Pressed &&
            !keyEvent.Echo && keyEvent.PhysicalKeycode == Key.F;
        if (!clicked && !useKey)
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

        if (!_doorOpen && CabinetRect.HasPoint(pos))
        {
            GetViewport().SetInputAsHandled();
            _ = OpenCabinetAsync();
            return;
        }

        if (_doorOpen && !FlashlightTaken && FlashlightRect.HasPoint(pos))
        {
            GetViewport().SetInputAsHandled();
            _ = TakeFlashlightAsync();
        }
    }

    public override void _Process(double delta)
    {
        _doorSwing = Mathf.MoveToward(_doorSwing, _doorOpen ? 1f : 0f, (float)delta / 0.45f);
        UpdateCursor();
        QueueRedraw();
    }

    private async Task OpenCabinetAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            AudioManager.Play(Sfx.DrawerOpen, -7f, 0.72f);
            _doorOpen = true;
            if (_helpLabel != null)
                _helpLabel.Text = FlashlightTaken
                    ? "Candles, matches, and a torch-shaped space."
                    : "Top shelf. Where a hand goes without looking.";
            await WaitSeconds(0.5f);
            if (!StillHere())
                return;
            if (!FlashlightTaken)
            {
                await Say("Candles. Matches. A coil of string nobody can explain. And on the top shelf — weight. Shape. The big torch, lying exactly where a hand comes down in the dark.");
            }
        }
        finally
        {
            _busy = false;
            if (StillHere())
                QueueRedraw();
        }
    }

    private async Task TakeFlashlightAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs == null || gs.HasFlag(FlashlightFlag))
                return;

            AudioManager.Play(Sfx.PhotoPickup, -9f, 0.85f);
            await Say("Heavy as a small hammer. His initials scratched in the barrel where some borrower tried once too often. Thumb on the switch, the way I held it for him a hundred times —");
            if (!StillHere())
                return;

            AudioManager.Play(Sfx.FlashlightClick, -5f);
            Hotspots.SpawnRipple(FlashlightRect.GetCenter(), new Color(1f, 0.92f, 0.6f));
            gs.SetFlag(FlashlightFlag, save: false);
            gs.UnlockClue("apartment_flashlight");
            await Say("Click. Light. Of course. Of course the batteries are fine. In this house they were never, ever dead. He wouldn't have allowed it, and neither — it turns out — would I.");
            if (!StillHere())
                return;

            gs.Toast("Father's flashlight — kept in Mara's bag (B). Now: back to the room where the light went out, and the way down he always took.");
            gs.SaveGame("flashlight from kitchen cabinet");
            if (_helpLabel != null)
                _helpLabel.Text = "Light, at last. Back the way she came — the house is dark, but it is not deep.";
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
        if (_busy || _left)
            return;
        _left = true;
        AudioManager.Play(Sfx.DrawerClose, -9f, 0.8f);
        await WaitSeconds(0.15f);
        if (!StillHere())
            return;
        SceneManager.ChangeScene(Room1ScenePath);
    }

    private void UpdateCursor()
    {
        Vector2 pos = GetGlobalMousePosition();
        bool over = InputFree() && (LeaveRect.HasPoint(pos) ||
            (!_doorOpen && CabinetRect.HasPoint(pos)) ||
            (_doorOpen && !FlashlightTaken && FlashlightRect.HasPoint(pos)));
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
        // The kitchen is behind her and black; what a hand remembers
        // is the counter's edge and what lives under it.
        DrawRect(new Rect2(0, 0, 1152, 648), new Color(0.045f, 0.04f, 0.035f), true);
        for (int i = 0; i < 8; i++)
            DrawRect(new Rect2(i * 148f, 0, 3, 648), new Color(0.03f, 0.025f, 0.02f, 0.8f), true);

        // Counter top, running over the cabinet like a tide line.
        DrawRect(new Rect2(238, 168, 676, 34), new Color(0.28f, 0.205f, 0.13f), true);
        DrawRect(new Rect2(238, 168, 676, 7), new Color(0.48f, 0.355f, 0.21f), true);
        DrawRect(new Rect2(238, 202, 676, 11), new Color(0.12f, 0.085f, 0.052f), true);

        // A deeper, older under-counter cabinet: framed stiles, inset
        // drawer fronts, a broad center cupboard, and a shadowed plinth.
        var cabinetBody = new Rect2(238, 213, 676, 320);
        DrawRect(cabinetBody, new Color(0.22f, 0.15f, 0.09f), true);
        DrawRect(cabinetBody, new Color(0.46f, 0.33f, 0.19f, 0.85f), false, 4f);
        DrawRect(new Rect2(252, 224, 648, 18), new Color(0.29f, 0.205f, 0.12f), true);
        DrawRect(new Rect2(252, 507, 648, 16), new Color(0.12f, 0.085f, 0.05f), true);
        DrawRect(new Rect2(266, 242, 118, 262), new Color(0.15f, 0.105f, 0.065f), true);
        DrawRect(new Rect2(768, 242, 118, 262), new Color(0.15f, 0.105f, 0.065f), true);
        for (int i = 0; i < 3; i++)
        {
            float y = 250f + i * 82f;
            var drawer = new Rect2(271, y, 108, 72);
            DrawRect(drawer, new Color(0.25f, 0.175f, 0.1f), true);
            DrawRect(drawer.Grow(-7f), new Color(0.2f, 0.135f, 0.08f), true);
            DrawRect(drawer, new Color(0.5f, 0.36f, 0.2f, 0.72f), false, 2f);
            DrawRect(new Rect2(319, y + 30, 13, 7), new Color(0.67f, 0.5f, 0.26f), true);
            DrawCircle(new Vector2(325.5f, y + 33.5f), 2f, new Color(0.92f, 0.75f, 0.42f));
        }
        var sideDoor = new Rect2(774, 244, 106, 255);
        DrawRect(sideDoor, new Color(0.23f, 0.16f, 0.095f), true);
        DrawRect(sideDoor.Grow(-10f), new Color(0.19f, 0.13f, 0.075f), true);
        DrawRect(sideDoor, new Color(0.49f, 0.35f, 0.2f, 0.76f), false, 2f);
        DrawRect(new Rect2(811, 363, 7, 28), new Color(0.68f, 0.51f, 0.27f), true);

        DrawCabinetInterior();
        DrawCabinetDoor();
        DrawLeaveControl();

        DrawRect(new Rect2(0, 0, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
    }

    private void DrawCabinetInterior()
    {
        var inner = CabinetRect.Grow(-10f);
        DrawRect(inner, new Color(0.05f, 0.04f, 0.03f, 1f), true);
        if (_doorSwing <= 0.05f)
            return;

        // Shelves: torch above, candles and ordinary kept things below.
        DrawRect(new Rect2(inner.Position.X + 6, 352, inner.Size.X - 12, 8),
            new Color(0.3f, 0.22f, 0.13f), true);
        DrawRect(new Rect2(inner.Position.X + 6, 440, inner.Size.X - 12, 8),
            new Color(0.3f, 0.22f, 0.13f), true);

        if (!FlashlightTaken && _flashlightTexture != null)
        {
            DrawTextureRect(_flashlightTexture, FlashlightRect, false);
        }
        else
        {
            // The pale space it waited in.
            DrawRect(FlashlightRect, new Color(0.16f, 0.13f, 0.1f, 0.8f), true);
        }

        // Candles standing to attention.
        float[] candleXs = [440f, 474f, 506f, 540f];
        foreach (float x in candleXs)
        {
            DrawRect(new Rect2(x, 392, 14, 48), new Color(0.88f, 0.84f, 0.74f, 0.95f), true);
            DrawRect(new Rect2(x + 5, 384, 3, 9), new Color(0.2f, 0.16f, 0.1f), true);
        }
        // Matchbox and the string nobody can explain.
        DrawRect(new Rect2(600, 414, 58, 26), new Color(0.55f, 0.3f, 0.16f, 0.95f), true);
        DrawRect(new Rect2(600, 414, 58, 7), new Color(0.75f, 0.62f, 0.3f, 0.95f), true);
        DrawCircle(new Vector2(694, 424), 17f, new Color(0.7f, 0.62f, 0.45f, 0.7f));
        DrawArc(new Vector2(694, 424), 11f, 0f, Mathf.Tau, 24,
            new Color(0.45f, 0.38f, 0.26f, 0.9f), 2f, true);
        // Spare batteries, upright, ready. Of course they are.
        for (int i = 0; i < 4; i++)
        {
            var batt = new Rect2(452f + i * 26f, 462, 16, 34);
            DrawRect(batt, new Color(0.25f, 0.25f, 0.28f, 0.98f), true);
            DrawRect(new Rect2(batt.Position, new Vector2(16, 8)), new Color(0.72f, 0.5f, 0.2f, 0.98f), true);
            DrawRect(new Rect2(batt.Position + new Vector2(5, -4), new Vector2(6, 4)),
                new Color(0.8f, 0.8f, 0.75f, 0.98f), true);
        }
        // A jam jar of screws, because every house has one.
        DrawRect(new Rect2(640, 458, 44, 38), new Color(0.6f, 0.66f, 0.6f, 0.35f), true);
        DrawRect(new Rect2(636, 450, 52, 10), new Color(0.5f, 0.48f, 0.44f, 0.95f), true);
    }

    private void DrawCabinetDoor()
    {
        // The door swings left on its hinge as _doorSwing runs: a flat
        // front narrowing into an angled slab beside the opening.
        float swing = _doorSwing;
        float frontWidth = Mathf.Lerp(CabinetRect.Size.X, 46f, swing);
        float shiftX = Mathf.Lerp(0f, -34f, swing);
        var door = new Rect2(CabinetRect.Position + new Vector2(shiftX, swing * 6f),
            new Vector2(frontWidth, CabinetRect.Size.Y - swing * 12f));

        var wood = new Color(0.25f, 0.17f, 0.095f, 1f);
        var woodEdge = new Color(0.52f, 0.37f, 0.20f, 1f);
        DrawRect(door, new Color(0.08f, 0.055f, 0.035f, 0.85f), true);
        DrawRect(new Rect2(door.Position + new Vector2(5, 4), door.Size - new Vector2(10, 8)), wood, true);
        DrawRect(door, woodEdge, false, 3f);
        if (swing < 0.6f)
        {
            var panel = door.Grow(-34f);
            DrawRect(panel, new Color(0.21f, 0.14f, 0.075f, 1f), true);
            DrawRect(panel.Grow(-7f), new Color(0.27f, 0.185f, 0.105f, 1f), false, 2f);
            DrawRect(new Rect2(door.Position.X + 11, door.Position.Y + 12, 5, door.Size.Y - 24),
                new Color(0.66f, 0.48f, 0.25f, 0.58f), true);
            DrawRect(new Rect2(door.End.X - 16, door.Position.Y + 12, 5, door.Size.Y - 24),
                new Color(0.08f, 0.055f, 0.035f, 0.7f), true);
            // Hinge straps and a low, aged brass pull.
            for (int i = 0; i < 2; i++)
            {
                float y = door.Position.Y + 58f + i * (door.Size.Y - 116f);
                DrawRect(new Rect2(door.Position.X + 5, y, 25, 8), new Color(0.57f, 0.42f, 0.23f));
                DrawCircle(new Vector2(door.Position.X + 12, y + 4), 2f, new Color(0.85f, 0.69f, 0.39f));
            }
            Vector2 knob = new(door.End.X - 33f, door.GetCenter().Y);
            DrawCircle(knob + new Vector2(2, 3), 10f, new Color(0, 0, 0, 0.48f));
            DrawCircle(knob, 9f, new Color(0.64f, 0.47f, 0.23f, 1f));
            DrawCircle(knob + new Vector2(-3, -3), 3f, new Color(0.84f, 0.67f, 0.36f, 0.9f));
            bool hover = !_doorOpen && InputFree() && CabinetRect.HasPoint(GetGlobalMousePosition());
            if (hover)
                DrawRect(CabinetRect, new Color(0.92f, 0.79f, 0.56f, 0.22f), false, 2f);
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

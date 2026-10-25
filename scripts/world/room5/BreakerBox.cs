using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The breaker box — the little heart her father came down to throw
/// in every blackout of her childhood, torch in his teeth.
///
/// Backup power is small and must be aimed. Six switches, one for
/// each kept room of the old house, and inside the door a card in
/// his pencil: "Backup's only a little heart. Three thumps in it,
/// no more. Give them to the rooms that get up in the morning." The
/// right pattern is the lived-in house — kettle and radio, the way
/// through, a mother's chair by the fire — and not the rooms that
/// only keep things. Ask for more than three and a fuse somewhere
/// small objects out loud. Ask wrong and the hum sags, and Mara,
/// counting, gets closer to saying his rule plainly. When the
/// pattern is his, the big handle takes, three green eyes open, and
/// somewhere upstairs a lamp remembers it was never the bulb.
/// </summary>
public partial class BreakerBox : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    private const string BasementScenePath = "res://scenes/world/basement.tscn";
    private const string PowerFlag = "backup_power_on";

    private sealed class BreakerSwitch
    {
        public string Label;
        public string Note;
        public bool LivedIn;
        public Rect2 Rect;
        public Label LabelControl;
        public Label NoteControl;
    }

    private static readonly Rect2 PanelRect = new(276, 108, 600, 452);
    private static readonly Rect2 NoteRect = new(304, 142, 216, 150);
    private static readonly Rect2 ThrowRect = new(446, 488, 260, 54);
    private static readonly Rect2 LeaveRect = new(16, 468, 216, 44);

    private readonly BreakerSwitch[] _switches = new BreakerSwitch[6];
    private readonly bool[] _on = [false, true, false, false, false, true];

    private FontFile _font;
    private Label _helpLabel;
    private Label _leaveLabel;
    private Label _throwLabel;
    private Label _noteLabel;

    private bool _busy;
    private bool _thrown;
    private int _fails;
    private bool _cursorOwned;
    private bool _left;

    private bool PowerOn => GameState.Instance?.HasFlag(PowerFlag) ?? false;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>(FontPath);

        string[] labels = ["KITCHEN", "PANTRY", "HALL", "Ida's ROOM", "SITTING ROOM", "SPARE ROOM"];
        string[] notes =
        [
            "kettle, radio, toast",
            "jars, keeping",
            "the way through",
            "kept exactly",
            "Mum's chair, the fire",
            "nobody's — yet",
        ];
        bool[] livedIn = [true, false, true, false, true, false];

        for (int i = 0; i < 6; i++)
        {
            int col = i % 2;
            int row = i / 2;
            var rect = new Rect2(556f + col * 168f, 196f + row * 92f, 148f, 76f);
            _switches[i] = new BreakerSwitch
            {
                Label = labels[i],
                Note = notes[i],
                LivedIn = livedIn[i],
                Rect = rect,
                LabelControl = MakeLabel(labels[i],
                    new Vector2(rect.Position.X - 10, rect.Position.Y - 30),
                    new Vector2(rect.Size.X + 20, 26), 19,
                    new Color(0.95f, 0.9f, 0.76f, 0.97f)),
                NoteControl = MakeLabel(notes[i],
                    new Vector2(rect.Position.X - 10, rect.End.Y + 2),
                    new Vector2(rect.Size.X + 20, 22), 15,
                    new Color(0.78f, 0.72f, 0.6f, 0.9f)),
            };
        }

        MakeLabel("The breaker box",
            new Vector2(376, 22), new Vector2(400, 38), 29,
            new Color(0.96f, 0.9f, 0.76f, 0.97f));
        _noteLabel = MakeLabel(
            "Backup's only a little heart.\nThree thumps in it, no more.\nGive them to the rooms that\nget up in the morning.\n— D.",
            NoteRect.Position + new Vector2(10, 12), NoteRect.Size - new Vector2(20, 20), 17,
            new Color(0.3f, 0.26f, 0.2f, 0.98f));
        _noteLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _helpLabel = MakeLabel("Click a switch to flip it. Set his pattern, then throw the big handle.",
            new Vector2(276, 566), new Vector2(600, 38), 20,
            new Color(0.9f, 0.86f, 0.76f, 0.94f));
        _throwLabel = MakeLabel("EMERGENCY — THROW",
            ThrowRect.Position, ThrowRect.Size, 22,
            new Color(0.98f, 0.9f, 0.72f, 0.98f));
        _leaveLabel = MakeLabel("Back to the dark",
            LeaveRect.Position, LeaveRect.Size, 20,
            new Color(0.95f, 0.88f, 0.72f, 0.97f));

        if (PowerOn)
        {
            for (int i = 0; i < 6; i++)
                _on[i] = _switches[i].LivedIn;
            _thrown = true;
            _helpLabel.Text = "Set, thrown, holding. Three rooms have their mornings back.";
            _leaveLabel.Text = "Back under the house";
        }

        QueueRedraw();
        _ = PlayIntroAsync();
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

            if (PowerOn)
            {
                await Say("Still humming. Still his pattern. Three green eyes keeping the house's mornings warm.");
                return;
            }

            await Say("The door's inside is all pencil. His handwriting, the careful kind, the kind for things other people will have to do one day without him.");
            if (!StillHere())
                return;
            await Say("\"Backup's only a little heart. Three thumps in it, no more. Give them to the rooms that get up in the morning.\" ...All right, Dad. Which rooms get up in the morning?");
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

        if (_thrown)
            return;

        if (ThrowRect.HasPoint(pos))
        {
            GetViewport().SetInputAsHandled();
            _ = ThrowHandleAsync();
            return;
        }

        for (int i = 0; i < _switches.Length; i++)
        {
            if (_switches[i].Rect.HasPoint(pos))
            {
                GetViewport().SetInputAsHandled();
                _on[i] = !_on[i];
                AudioManager.Play(Sfx.KeyType, -2f, _on[i] ? 0.72f : 0.58f);
                QueueRedraw();
                return;
            }
        }
    }

    private async Task ThrowHandleAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs == null)
                return;

            int onCount = 0;
            bool anyKeptOn = false;
            bool anyLivedOff = false;
            for (int i = 0; i < _switches.Length; i++)
            {
                if (_on[i])
                    onCount++;
                if (_on[i] && !_switches[i].LivedIn)
                    anyKeptOn = true;
                if (!_on[i] && _switches[i].LivedIn)
                    anyLivedOff = true;
            }

            AudioManager.Play(Sfx.LockOpen, -1f, 0.5f);

            if (onCount > 3)
            {
                await WaitSeconds(0.3f);
                if (!StillHere())
                    return;
                AudioManager.Play(Sfx.ScreenCrack2, -3f, 1.4f);
                for (int i = 0; i < _on.Length; i++)
                    _on[i] = false;
                QueueRedraw();
                _fails++;
                await Say("POP — somewhere small, a fuse gives up on the spot. Too much asked. \"Three thumps in it, no more.\" He did write it down for me. He wrote everything down for me.");
                return;
            }

            if (!anyKeptOn && !anyLivedOff && onCount == 3)
            {
                _thrown = true;
                QueueRedraw();
                await WaitSeconds(0.45f);
                if (!StillHere())
                    return;
                AudioManager.Play(Sfx.WordChime, -4f, 0.8f);
                gs.SetFlag(PowerFlag, save: false);
                gs.ClearFlag("blackout_active", save: false);
                await Say("THUMP. And then — a hum, low and certain, rising through the soles of my shoes. Three small green eyes open in the box. Kettle. The way through. Mum's chair, and the fire.");
                if (!StillHere())
                    return;
                await Say("That's his pattern. Not the whole house lit like a festival — just the rooms that get up in the morning, given back their mornings. Somewhere over my head, right now, a lamp has come back on without being asked.");
                if (!StillHere())
                    return;
                gs.Toast("Backup power. The house breathes again — and somewhere above, October is showing through the coats.");
                gs.SaveGame("backup power on");
                AudioManager.Instance?.PlayAmbienceForScene(BasementScenePath);
                if (_helpLabel != null)
                    _helpLabel.Text = "Set, thrown, holding. Up the duct, and mind the lamp on your way past it.";
                if (_leaveLabel != null)
                    _leaveLabel.Text = "Back under the house";
                return;
            }

            _fails++;
            await WaitSeconds(0.35f);
            if (!StillHere())
                return;
            AudioManager.Play(Sfx.Denied, -5f, 0.65f);
            if (anyKeptOn)
            {
                await Say("The hum starts — and labours, and sags into nothing. Power's running to a room that doesn't want mornings. A room that's only kept. The little heart can hear the difference even if I keep pretending I can't.");
            }
            else
            {
                await Say("The hum starts — and sags. Something that gets up in the morning is still sitting in the dark, waiting for its share. Three thumps, and all of them spoken for.");
            }
            if (!StillHere())
                return;
            if (_fails >= 2)
            {
                await Say("Say it plainly, then. Kettle, radio, toast — that gets up. The way through — that gets up. Mum's chair and the fire — that gets up. Everything else in that house is kept, or nobody's. Okay, Dad. Okay.");
            }
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
        AudioManager.Play(Sfx.DrawerClose, -5f, 0.7f);
        await WaitSeconds(0.15f);
        if (!StillHere())
            return;
        SceneManager.ChangeScene(BasementScenePath);
    }

    public override void _Process(double delta)
    {
        UpdateCursor();
        QueueRedraw();
    }

    private void UpdateCursor()
    {
        Vector2 pos = GetGlobalMousePosition();
        bool over = InputFree() && (LeaveRect.HasPoint(pos) ||
            (!_thrown && (ThrowRect.HasPoint(pos) || AnySwitchAt(pos))));
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

    private bool AnySwitchAt(Vector2 pos)
    {
        foreach (var sw in _switches)
        {
            if (sw.Rect.HasPoint(pos))
                return true;
        }
        return false;
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
        // Torchlit stone and the grey box, in flat chunky bands.
        DrawRect(new Rect2(0, 0, 1152, 648), new Color(0.04f, 0.04f, 0.045f), true);
        for (int row = 0; row < 7; row++)
        {
            float y = row * 96f;
            DrawRect(new Rect2(0, y, 1152, 3), new Color(0.025f, 0.025f, 0.03f, 0.9f), true);
            for (int col = 0; col < 5; col++)
            {
                float x = col * 240f + (row % 2 == 0 ? 0f : 120f);
                DrawRect(new Rect2(x, y, 3, 96), new Color(0.025f, 0.025f, 0.03f, 0.7f), true);
            }
        }
        DrawCircle(new Vector2(576, 330), 330f, new Color(1f, 0.85f, 0.55f, 0.05f));

        // The box, its door swung toward the viewer-left, hinges right.
        DrawRect(PanelRect, new Color(0.2f, 0.21f, 0.22f, 1f), true);
        DrawRect(PanelRect, new Color(0.55f, 0.56f, 0.52f, 0.95f), false, 4f);
        DrawRect(PanelRect.Grow(-12f), new Color(0.13f, 0.14f, 0.15f, 1f), true);

        // His card, taped inside the door at reading height.
        DrawRect(NoteRect, new Color(0.88f, 0.83f, 0.7f, 0.98f), true);
        DrawRect(NoteRect, new Color(0.5f, 0.44f, 0.3f, 0.9f), false, 2f);
        DrawRect(new Rect2(NoteRect.Position + new Vector2(78, -6), new Vector2(60, 14)),
            new Color(0.8f, 0.76f, 0.6f, 0.75f), true);

        DrawSwitches();
        DrawThrowHandle();
        DrawLeaveControl();

        DrawRect(new Rect2(0, 0, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
    }

    private void DrawSwitches()
    {
        Vector2 mouse = GetGlobalMousePosition();
        for (int i = 0; i < _switches.Length; i++)
        {
            var sw = _switches[i];
            bool on = _on[i];
            bool hover = !_thrown && InputFree() && sw.Rect.HasPoint(mouse);

            var plate = sw.Rect;
            DrawRect(plate, new Color(0.16f, 0.17f, 0.18f, 1f), true);
            DrawRect(plate, hover
                ? new Color(1f, 0.86f, 0.52f, 0.8f)
                : new Color(0.48f, 0.49f, 0.46f, 0.8f), false, 2f);

            // The slot, and the toggle standing in it: up is on, the
            // way light switches were in that house.
            var slot = new Rect2(plate.GetCenter() - new Vector2(11, 26), new Vector2(22, 52));
            DrawRect(slot, new Color(0.05f, 0.05f, 0.055f, 1f), true);
            Vector2 knobPos = slot.GetCenter() + new Vector2(0, on ? -15f : 15f);
            DrawCircle(knobPos + new Vector2(2, 3), 13f, new Color(0, 0, 0, 0.5f));
            DrawCircle(knobPos, 13f, on
                ? new Color(0.85f, 0.6f, 0.22f, 1f)
                : new Color(0.5f, 0.48f, 0.44f, 1f));
            DrawCircle(knobPos + new Vector2(-4, -4), 4f, new Color(1f, 0.9f, 0.7f, 0.85f));

            // Its little lamp: green only once the heart is beating
            // through this switch — before that, on is just a wish.
            Vector2 lamp = plate.Position + new Vector2(18, 18);
            bool lit = _thrown && on;
            if (lit)
            {
                DrawCircle(lamp, 11f, new Color(0.35f, 0.9f, 0.4f, 0.25f));
                DrawCircle(lamp, 5.5f, new Color(0.45f, 0.95f, 0.48f, 1f));
            }
            else
            {
                DrawCircle(lamp, 5.5f, new Color(0.2f, 0.24f, 0.2f, 1f));
            }

            if (sw.LabelControl != null)
                sw.LabelControl.Modulate = on ? new Color(1.1f, 1.02f, 0.82f) : Colors.White;
        }
    }

    private void DrawThrowHandle()
    {
        bool hover = !_thrown && InputFree() && ThrowRect.HasPoint(GetGlobalMousePosition());
        DrawRect(ThrowRect,
            _thrown ? new Color(0.14f, 0.22f, 0.13f, 0.95f)
            : hover ? new Color(0.3f, 0.16f, 0.08f, 0.95f)
            : new Color(0.2f, 0.11f, 0.06f, 0.95f), true);
        DrawRect(ThrowRect, _thrown
            ? new Color(0.45f, 0.9f, 0.48f, 0.9f)
            : new Color(0.78f, 0.55f, 0.3f, hover ? 0.95f : 0.7f), false, 2f);
        if (_throwLabel != null)
            _throwLabel.Text = _thrown ? "EMERGENCY — HOLDING" : "EMERGENCY — THROW";
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

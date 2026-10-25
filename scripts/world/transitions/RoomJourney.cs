using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// A short first-person journey between two completed rooms.
///
/// The camera visibly travels: down a memory hallway toward the next door,
/// or along the empty road until the lit porch is close enough to name.
/// The destination is not loaded until the player clicks the door (or the
/// distant porch light). Each wrapper scene stores its own destination and
/// journey id, so saving/quitting mid-journey resumes in the right place.
///
/// Room 4 keeps its authored brass-door close-up and Room 5 keeps its
/// wardrobe/garden passage; this script covers the plainer stretches of
/// house and road between Rooms 1-2, 2-3, 3-4, and 6-Porch.
/// </summary>
public partial class RoomJourney : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";

    [Export(PropertyHint.File, "*.tscn")]
    public string DestinationScenePath { get; set; } = "res://scenes/world/room_1.tscn";

    [Export]
    public string JourneyId { get; set; } = "Room1ToRoom2";

    private sealed class JourneySpec
    {
        public string TexturePath { get; init; }
        public string[] Lines { get; init; }
        public string[] RepeatLines { get; init; }
        public Vector2 Focus { get; init; }
        public float FinalScale { get; init; }
        public Rect2 TargetRect { get; init; }
        public string Prompt { get; init; }
        public float CameraSeconds { get; init; }
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
                "One more door. One room at a time.",
            ],
            RepeatLines = ["The house puts the same door in front of me again."],
            Focus = new Vector2(580f, 318f),
            FinalScale = 1.7f,
            TargetRect = new Rect2(372f, 12f, 410f, 620f),
            Prompt = "Click the door to open it",
            CameraSeconds = 3.4f,
        },
        ["Room2ToRoom3"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Door.png",
            Lines =
            [
                "The sitting room goes quiet behind me.",
                "Whatever this next room kept, it kept it close.",
            ],
            RepeatLines = ["The house puts the same door in front of me again."],
            Focus = new Vector2(580f, 318f),
            FinalScale = 1.7f,
            TargetRect = new Rect2(372f, 12f, 410f, 620f),
            Prompt = "Click the door to open it",
            CameraSeconds = 3.4f,
        },
        ["Room3ToRoom4"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Door.png",
            Lines =
            [
                "My steps sound too light. Like socks on boards.",
                "Somewhere ahead, a hallway is holding its sentence.",
            ],
            RepeatLines = ["The house puts the same door in front of me again."],
            Focus = new Vector2(580f, 318f),
            FinalScale = 1.7f,
            TargetRect = new Rect2(372f, 12f, 410f, 620f),
            Prompt = "Click the door to open it",
            CameraSeconds = 3.4f,
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

    private bool _readyToOpen;
    private bool _opening;
    private bool _hovering;
    private bool _hoverCursorOwned;
    private bool _reduceMotion;
    private bool _seenBefore;
    private float _cameraSeconds = 3.4f;
    private float _elapsed;
    private float _progress;

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

        _seenBefore = GameState.Instance?.HasFlag(SeenFlag) ?? false;
        _reduceMotion = Settings.ReduceMotionOrDefault();
        _cameraSeconds = _seenBefore ? Math.Min(1.7f, _spec.CameraSeconds) : _spec.CameraSeconds;
        _progress = _reduceMotion ? 1f : 0f;

        BuildPrompt();
        QueueRedraw();
        _ = RunJourneyAsync();
    }

    public override void _ExitTree()
    {
        ClearHover();
    }

    public override void _Process(double delta)
    {
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
        Vector2 focus = new Vector2(576f, 324f).Lerp(_spec.Focus, eased);

        // A barely-there walking bob, gone once Mara arrives (or for
        // Reduce Motion). It is in source pixels, so it stays subtle.
        if (!_reduceMotion && _progress < 1f)
            focus.Y += (float)Math.Sin(_elapsed * 5.2f) * 1.6f * (1f - _progress);

        Vector2 sourcePos = new(
            Mathf.Clamp(focus.X - sourceSize.X * 0.5f, 0f, 1152f - sourceSize.X),
            Mathf.Clamp(focus.Y - sourceSize.Y * 0.5f, 0f, 648f - sourceSize.Y));
        DrawTextureRectRegion(_texture, screen, new Rect2(sourcePos, sourceSize), Colors.White);

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

using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Room 7 — the last room.
///
/// This replaces the old six independent ShardSlot nodes (which accepted any
/// carried fragment in any frame) with the finished ordering puzzle: choose
/// an exact fragment from the tray, place it in an exact hollow frame, and
/// take it back freely until all six stand in lived order. Release then
/// opens the final authored choice between waking and keeping the house.
/// </summary>
public partial class Room7Final : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string BackgroundPath = "res://assets/images/bgs/room_7_bg.png";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    private const string InOrderFlag = "room7_in_order";
    private const string ChoiceMadeFlag = "room7_choice_made";

    private static readonly Rect2 PanelRect = new(236, 96, 680, 330);
    private static readonly string[] ShortNames =
    [
        "Brass Key",
        "Photograph",
        "Preserved Names",
        "Hallway Sentence",
        "Unsent Apology",
        "Last Afternoon",
    ];

    private Font _font;
    private Texture2D _background;

    /// <summary>Every fragment is a visible keepsake, in the tray and in its
    /// frame — the same items Mara carries in her bag.</summary>
    private static readonly string[] FragmentIconPaths =
    [
        "res://assets/images/core/shards/brass_key.png",
        "res://assets/images/core/shards/photo.png",
        "res://assets/images/core/shards/fragment_jar.png",
        "res://assets/images/core/shards/fragment_frame.png",
        "res://assets/images/core/shards/fragment_letter.png",
        "res://assets/images/core/shards/fragment_apple.png",
    ];
    private readonly Texture2D[] _fragmentIcons = new Texture2D[GameState.MaxShards];
    private readonly Button[] _trayButtons = new Button[GameState.MaxShards];
    private readonly Label[] _frameTitles = new Label[GameState.MaxShards];

    private PanelContainer _choicePanel;
    private Button _wakingButton;

    private int _selectedShard = -1;
    private int _hoveredSlot = -1;
    private bool _hoverCursorOwned;
    private ulong _lastHoverTickMsec;
    private bool _busy;
    private bool _choiceOpen;
    private bool _endingStarted;

    public override void _Ready()
    {
        SetProcess(true);
        SetProcessInput(true);

        _font = GD.Load<FontFile>(FontPath);
        _background = GD.Load<Texture2D>(BackgroundPath);
        for (int i = 0; i < GameState.MaxShards; i++)
            _fragmentIcons[i] = GD.Load<Texture2D>(FragmentIconPaths[i]);
        if (_background == null)
            Logger.Error($"[Room7] Could not load background: {BackgroundPath}");

        BuildFrameLabels();
        BuildTrayButtons();
        BuildChoicePanel();

        if (GameState.Instance != null)
        {
            GameState.Instance.ShardsChanged += UpdateVisuals;
            GameState.Instance.VerbsChanged += UpdateVisuals;
            GameState.Instance.StoryFlagsChanged += UpdateVisuals;
        }

        UpdateVisuals();
        QueueRedraw();
        _ = MaybePlayLastRoomLessonAsync();
    }

    private async Task MaybePlayLastRoomLessonAsync()
    {
        const string lessonFlag = "story_lesson_room7_release_room";
        var gs = GameState.Instance;
        if (_busy || gs == null || gs.HasFlag(lessonFlag) || gs.HasFlag(ChoiceMadeFlag) ||
            !string.IsNullOrEmpty(gs.CompletedEnding))
            return;

        _busy = true;
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(this))
                return;

            if (gs.CountRecovered() < GameState.MaxShards)
            {
                await SaySequence(
                    "The last room has six hollow frames because Mara's story still has places she has not brought back with her.",
                    $"Only {gs.CountRecovered()} of the six fragments are recovered. The frames will wait. They are very good at that.");
            }
            else
            {
                await SaySequence(
                    "You have carried six pieces of one girl through one woman's unfinished night. Understand, before you touch a frame: nothing chosen here gives Ida back. It only decides what carrying her means.",
                    "Six hollow frames. Six pieces of one life. Nothing in this room is labelled.");
            }

            if (GodotObject.IsInstanceValid(this))
            {
                gs.SetFlag(lessonFlag, save: false);
                gs.SaveGame("story lesson: last room Release");
            }
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                _busy = false;
                UpdateVisuals();
            }
        }
    }

    public override void _ExitTree()
    {
        if (GameState.Instance != null)
        {
            GameState.Instance.ShardsChanged -= UpdateVisuals;
            GameState.Instance.VerbsChanged -= UpdateVisuals;
            GameState.Instance.StoryFlagsChanged -= UpdateVisuals;
        }
        ClearHover();
    }

    public override void _Process(double delta)
    {
        if (_busy || _choiceOpen || _endingStarted || Dialogue.IsInputBlocked ||
            Dialogue.IsShowing || GetTree().Paused || SceneManager.IsChanging)
        {
            SetHoveredSlot(-1);
            return;
        }

        Vector2 mouse = GetGlobalMousePosition();
        SetHoveredSlot(IsOverUi(mouse) ? -1 : FindSlot(mouse));
        if (_hoveredSlot >= 0)
            QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        // A click, or F: the house's hand, for the thing under the
        // cursor. Same reach, same rules, either way.
        bool clicked = @event is InputEventMouseButton mouse && mouse.Pressed &&
            mouse.ButtonIndex == MouseButton.Left;
        bool useKey = @event is InputEventKey key && key.Pressed &&
            !key.Echo && key.PhysicalKeycode == Key.F;
        if (!clicked && !useKey)
            return;

        if (_busy || _choiceOpen || _endingStarted || Dialogue.IsInputBlocked ||
            Dialogue.IsShowing || GetTree().Paused || SceneManager.IsChanging)
            return;

        Vector2 position = GetGlobalMousePosition();
        if (IsOverUi(position) || !PanelRect.HasPoint(position))
            return;

        // Clarity addressed false surfaces in the TileMap rooms. Here the
        // fragments themselves are already true; their order is the puzzle.
        if (ClaritySystem.IsArmed)
        {
            GetTree().Root.SetInputAsHandled();
            AudioManager.Play(Sfx.Denied, -10f, 1.02f);
            GameState.Instance?.Toast("Clarity cannot order a life.");
            return;
        }

        var activeVerb = MemoryVerbSystem.Active;
        if (activeVerb == MemoryVerb.Release)
        {
            GetTree().Root.SetInputAsHandled();
            Hotspots.SpawnRipple(position, new Color(1f, 0.88f, 0.55f));
            HandleReleaseClick(position);
            return;
        }

        if (activeVerb != MemoryVerb.None)
        {
            GetTree().Root.SetInputAsHandled();
            _ = WrongVerbAsync(activeVerb);
            return;
        }

        int slot = FindSlot(position);
        if (slot < 0)
            return;

        GetTree().Root.SetInputAsHandled();
        Hotspots.SpawnRipple(position);
        HandleSlotClick(slot);
    }

    public override void _Draw()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        if (_background != null)
            DrawTextureRect(_background, screen, false, new Color(0.92f, 0.88f, 0.82f));
        else
            DrawRect(screen, new Color(0.025f, 0.022f, 0.02f));

        DrawRect(screen, new Color(0.02f, 0.015f, 0.01f, 0.22f));
        DrawRect(PanelRect, new Color(0.035f, 0.03f, 0.026f, 0.8f));
        DrawRect(PanelRect, new Color(0.82f, 0.68f, 0.42f, 0.72f), false, 2f);

        var trayRect = new Rect2(96, 446, 960, 76);
        DrawRect(trayRect, new Color(0.035f, 0.03f, 0.026f, 0.78f));
        DrawRect(trayRect, new Color(0.72f, 0.6f, 0.4f, 0.42f), false, 1.5f);

        var gs = GameState.Instance;
        for (int slot = 0; slot < GameState.MaxShards; slot++)
        {
            Rect2 rect = GetFrameRect(slot);
            int placedShard = gs != null ? gs.PlacedShardBySlot[slot] : -1;
            bool placed = placedShard >= 0;
            bool hovered = slot == _hoveredSlot;

            // A placed piece simply rests. The house does not grade pieces
            // one at a time — it answers only when all six stand together.
            Color fill = !placed
                ? new Color(0.07f, 0.06f, 0.05f, 0.78f)
                : new Color(0.2f, 0.16f, 0.1f, 0.92f);
            Color border = hovered
                ? new Color(1f, 0.9f, 0.6f, 0.98f)
                : !placed
                    ? new Color(0.58f, 0.5f, 0.4f, 0.55f)
                    : new Color(0.86f, 0.72f, 0.48f, 0.9f);

            DrawRect(rect, fill);
            DrawRect(rect, border, false, hovered ? 3f : 2f);
            DrawRect(rect.Grow(-8f), new Color(border.R, border.G, border.B, 0.24f), false, 1f);

            if (!placed && _selectedShard >= 0)
            {
                DrawArc(rect.GetCenter(), 24f, 0f, Mathf.Tau, 36,
                    new Color(1f, 0.88f, 0.55f, hovered ? 0.72f : 0.34f), 1.5f, true);
            }

            if (placed)
            {
                Vector2 c = rect.GetCenter() + new Vector2(0, -8);
                var iconTex = _fragmentIcons[placedShard];
                if (iconTex != null)
                {
                    // The keepsake itself stands in the frame, in its own
                    // colours wherever Mara has put it.
                    DrawTextureRect(iconTex,
                        new Rect2(c - new Vector2(28, 28), new Vector2(56, 56)),
                        false,
                        Colors.White);
                }
                else
                {
                    Vector2[] diamond =
                    [
                        c + new Vector2(0, -27),
                        c + new Vector2(20, 0),
                        c + new Vector2(0, 27),
                        c + new Vector2(-20, 0),
                    ];
                    DrawColoredPolygon(diamond,
                        new Color(0.92f, 0.78f, 0.52f, 0.85f));
                }
                DrawArc(c, 31f, 0f, Mathf.Tau, 40, border, 1.4f, true);
                DrawCircle(c, 3f, new Color(1f, 0.97f, 0.86f, 0.95f));
            }
        }

        if (gs != null && gs.FragmentsInOrder())
        {
            var gold = new Color(1f, 0.88f, 0.52f, 0.94f);
            var shadow = new Color(0.05f, 0.03f, 0.01f, 0.55f);
            for (int i = 1; i < GameState.MaxShards; i++)
            {
                Vector2 from = GetFrameRect(i - 1).GetCenter() + new Vector2(0, -8);
                Vector2 to = GetFrameRect(i).GetCenter() + new Vector2(0, -8);
                DrawLine(from + new Vector2(2, 2), to + new Vector2(2, 2), shadow, 5f, true);
                DrawLine(from, to, gold, 2.4f, true);
            }
        }

        if (_choiceOpen)
            DrawRect(screen, new Color(0f, 0f, 0f, 0.34f));
    }

    // ------------------------------------------------------------ construction

    private void BuildFrameLabels()
    {
        var title = CreateLabel("THE LAST ROOM", new Vector2(326, 102), 24,
            new Color(0.94f, 0.82f, 0.58f, 0.96f), new Vector2(500, 30));
        title.Name = "RoomTitle";

        for (int i = 0; i < GameState.MaxShards; i++)
        {
            Rect2 rect = GetFrameRect(i);

            // Nothing here is numbered. The frames are six hollows; the order
            // they want is the order Mara lived, and only she knows it.
            var label = CreateLabel("hollow", rect.Position + new Vector2(10, 64), 17,
                new Color(0.82f, 0.78f, 0.7f, 0.78f), new Vector2(142, 42));
            _frameTitles[i] = label;
        }
    }

    private void BuildTrayButtons()
    {
        // The tray is Mara's bag tipped out on the table: the pieces lie in
        // the jumbled order she carried them in — the same jumble as the
        // Bag and the Journal — never the order they were lived. Which
        // piece belongs in which hollow has to come from remembering,
        // not from reading the tray left to right.
        var displayOrder = GameState.Instance?.FragmentDisplayOrder;
        for (int i = 0; i < GameState.MaxShards; i++)
        {
            int shard = i;
            var button = CreateButton(string.Empty, 16);
            button.Name = $"FragmentTray{shard}";
            // Tray buttons keep their two-line names; the keepsake textures
            // themselves appear in the frames above and in Mara's bag.
            int displayPos = displayOrder != null
                ? System.Array.IndexOf(displayOrder, shard)
                : shard;
            button.Position = new Vector2(111 + displayPos * 155, 458);
            button.Size = new Vector2(150, 52);
            button.FocusMode = Control.FocusModeEnum.None;
            button.Pressed += () => OnTrayButtonPressed(shard);
            _trayButtons[shard] = button;
            AddChild(button);
        }
    }

    private void BuildChoicePanel()
    {
        _choicePanel = new PanelContainer
        {
            Name = "EndingChoicePanel",
            Visible = false,
            Position = new Vector2(326, 158),
            Size = new Vector2(500, 332),
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 3000,
        };
        _choicePanel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        AddChild(_choicePanel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 11);
        _choicePanel.AddChild(vbox);

        var title = new Label
        {
            Text = "The memory is whole",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ApplyLabelTheme(title, 30, new Color(0.97f, 0.88f, 0.66f));
        vbox.AddChild(title);

        var body = new Label
        {
            Text = "Mara can carry Ida into the life that continued — or Mara can remain where every room still says she is coming back.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            CustomMinimumSize = new Vector2(430, 72),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ApplyLabelTheme(body, 20, new Color(0.94f, 0.9f, 0.82f));
        vbox.AddChild(body);

        _wakingButton = CreateButton("Let Mara wake", 21);
        _wakingButton.CustomMinimumSize = new Vector2(430, 42);
        _wakingButton.Pressed += () => ChooseEnding("waking");
        vbox.AddChild(_wakingButton);

        var keptButton = CreateButton("Keep the house", 21);
        keptButton.CustomMinimumSize = new Vector2(430, 42);
        keptButton.Pressed += () => ChooseEnding("kept_house");
        vbox.AddChild(keptButton);

        var notYetButton = CreateButton("Not yet", 19);
        notYetButton.CustomMinimumSize = new Vector2(430, 36);
        notYetButton.Pressed += CloseChoice;
        vbox.AddChild(notYetButton);
    }

    private Button CreateButton(string text, int fontSize)
    {
        var button = new Button
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        if (_font != null)
            button.AddThemeFontOverride("font", _font);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeColorOverride("font_color", new Color(0.94f, 0.9f, 0.82f));
        button.AddThemeColorOverride("font_hover_color", new Color(1f, 0.94f, 0.76f));
        button.AddThemeColorOverride("font_disabled_color", new Color(0.48f, 0.45f, 0.4f, 0.75f));
        ApplyButtonStyle(button, false);
        button.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -11f);
        button.Pressed += () => AudioManager.Play(Sfx.UiClick, -7f);
        return button;
    }

    private static StyleBoxFlat MakePanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.045f, 0.038f, 0.032f, 0.96f),
            BorderColor = new Color(0.82f, 0.68f, 0.42f, 0.78f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(5);
        style.ContentMarginLeft = 28;
        style.ContentMarginRight = 28;
        style.ContentMarginTop = 20;
        style.ContentMarginBottom = 20;
        return style;
    }

    private static void ApplyButtonStyle(Button button, bool selected)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = selected
                ? new Color(0.48f, 0.34f, 0.12f, 0.94f)
                : new Color(0.07f, 0.06f, 0.05f, 0.88f),
            BorderColor = selected
                ? new Color(1f, 0.86f, 0.5f, 0.95f)
                : new Color(0.72f, 0.6f, 0.4f, 0.5f),
        };
        normal.SetBorderWidthAll(selected ? 2 : 1);
        normal.SetCornerRadiusAll(4);
        normal.SetContentMarginAll(6);

        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.58f, 0.42f, 0.16f, 0.94f),
            BorderColor = new Color(1f, 0.9f, 0.58f, 0.95f),
        };
        hover.SetBorderWidthAll(1);
        hover.SetCornerRadiusAll(4);
        hover.SetContentMarginAll(6);

        var disabled = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.55f),
            BorderColor = new Color(0.45f, 0.4f, 0.32f, 0.28f),
        };
        disabled.SetBorderWidthAll(1);
        disabled.SetCornerRadiusAll(4);
        disabled.SetContentMarginAll(6);

        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", hover);
        button.AddThemeStyleboxOverride("disabled", disabled);
    }

    private Label CreateLabel(string text, Vector2 position, int fontSize, Color color, Vector2 size)
    {
        var label = new Label
        {
            Text = text ?? string.Empty,
            Position = position,
            Size = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 2600,
        };
        ApplyLabelTheme(label, fontSize, color);
        AddChild(label);
        return label;
    }

    private void ApplyLabelTheme(Label label, int fontSize, Color color)
    {
        if (_font != null)
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
    }

    // ------------------------------------------------------------ interaction

    private void OnTrayButtonPressed(int shard)
    {
        if (_busy || _choiceOpen || _endingStarted || Dialogue.IsInputBlocked ||
            Dialogue.IsShowing || GetTree().Paused || SceneManager.IsChanging)
            return;

        var gs = GameState.Instance;
        if (gs == null)
            return;

        if (System.Array.IndexOf(gs.PlacedShardBySlot, shard) >= 0)
        {
            AudioManager.Play(Sfx.Denied, -9f);
            gs.Toast("That fragment is already in a frame.");
            return;
        }

        if (!IsCarried(shard))
        {
            AudioManager.Play(Sfx.Denied, -9f);
            gs.Toast("That part of the morning is still missing from the house.");
            return;
        }

        if (_selectedShard == shard)
        {
            _selectedShard = -1;
            gs.Toast("Fragment set down.");
        }
        else
        {
            _selectedShard = shard;
            gs.Toast($"{GameState.ShardTitles[shard]} — chosen.");
        }
        UpdateVisuals();
    }

    private void HandleSlotClick(int slot)
    {
        var gs = GameState.Instance;
        if (gs == null)
            return;

        int placedShard = gs.PlacedShardBySlot[slot];
        bool slotFilled = placedShard >= 0 ||
                          (Globals.Instance != null && Globals.Instance.SHARDS_PLACED[slot]);

        if (slotFilled)
        {
            if (gs.TryRemoveShardFromSlot(slot, out int removedShard))
            {
                if (gs.HasFlag(InOrderFlag))
                    gs.ClearFlag(InOrderFlag);
                _selectedShard = removedShard;
                AudioManager.Play(Sfx.ShardPlace, -7f, 0.78f);
                Hotspots.SpawnRipple(GetFrameRect(slot).GetCenter(),
                    new Color(0.68f, 0.82f, 1f));
                gs.Toast($"{ShortNames[removedShard]} taken back.");
            }
            else
            {
                AudioManager.Play(Sfx.Denied, -8f);
            }
            UpdateVisuals();
            return;
        }

        if (_selectedShard < 0)
        {
            AudioManager.Play(Sfx.Denied, -8f);
            gs.Toast("The frame is hollow.");
            return;
        }

        int shardToPlace = _selectedShard;
        ShardPlaceResult result = gs.TryPlaceSpecificShard(slot, shardToPlace);
        switch (result)
        {
            case ShardPlaceResult.Placed:
            case ShardPlaceResult.AllPlaced:
                _selectedShard = -1;
                AudioManager.Play(Sfx.ShardPlace, -5f);
                Hotspots.SpawnRipple(GetFrameRect(slot).GetCenter(),
                    new Color(1f, 0.88f, 0.55f));
                UpdateVisuals();
                if (result == ShardPlaceResult.AllPlaced)
                    _ = HandleAllPlacedAsync();
                else
                    gs.Toast($"It rests there. ({gs.CountPlaced()}/{GameState.MaxShards} returned)");
                break;

            case ShardPlaceResult.AlreadyPlaced:
                AudioManager.Play(Sfx.Denied, -9f);
                UpdateVisuals();
                break;

            default:
                _selectedShard = -1;
                AudioManager.Play(Sfx.Denied, -8f);
                gs.Toast("Your hands are empty. The house still holds that piece.");
                UpdateVisuals();
                break;
        }
    }

    private async Task HandleAllPlacedAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs != null && gs.FragmentsInOrder())
            {
                gs.SetFlag(InOrderFlag);
                gs.MarkRoomCompleted("Room7");
                AudioManager.Play(Sfx.PuzzleSolve, -5f);
                AudioManager.Play(Sfx.ClarifyReveal, -8f, 1.08f);
                UpdateVisuals();
                await SaySequence(
                    "One morning, in the order she lived it: the key, the photograph, the names she preserved, the sentence in the hall, the apology she never sent, the last afternoon.",
                    "Nothing is missing now except the decision.");
            }
            else
            {
                AudioManager.Play(Sfx.CorruptionGlitch, -9f, 0.82f);
                await Say("All six pieces are here, but the house has arranged them into a kinder lie.");
            }
        }
        finally
        {
            _busy = false;
            UpdateVisuals();
        }
    }

    private void HandleReleaseClick(Vector2 position)
    {
        var gs = GameState.Instance;
        if (gs == null)
            return;

        if (gs.CountPlaced() < GameState.MaxShards)
        {
            _ = WrongReleaseAsync("Release cannot decide an unfinished memory.");
            return;
        }

        if (!gs.FragmentsInOrder())
        {
            _ = WrongReleaseAsync("Release would preserve the house's arrangement, not Mara's life.");
            return;
        }

        gs.SetFlag(InOrderFlag);
        gs.MarkRoomCompleted("Room7");
        MemoryVerbSystem.ConsumeActive(MemoryVerb.Release);
        AudioManager.Play(Sfx.ClarifyReveal, -5f, 1.12f);
        AudioManager.Play(Sfx.PuzzleSolve, -8f, 1.08f);
        Hotspots.SpawnRipple(PanelRect.GetCenter(), new Color(1f, 0.9f, 0.58f));

        // Before Mara decides anything, the house finally shows her — and
        // the player — the whole of what happened, in its own rooms.
        if (GameUI.Instance != null)
        {
            _busy = true;
            UpdateVisuals();
            GameUI.Instance.PlayFinaleTruthReveal(() =>
            {
                if (!GodotObject.IsInstanceValid(this))
                    return;
                _busy = false;
                ShowChoice();
            });
        }
        else
        {
            ShowChoice();
        }
    }

    private async Task WrongReleaseAsync(string line)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -8f, 0.86f);
            await Say(line, 4.5f, 23);
        }
        finally
        {
            _busy = false;
            UpdateVisuals();
        }
    }

    private async Task WrongVerbAsync(MemoryVerb verb)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -9f, 0.92f);
            string line = verb switch
            {
                MemoryVerb.Echo => "Echo can hear every room at once here. It cannot choose which one Mara wakes in.",
                MemoryVerb.Anchor => "Anchoring one fragment would only preserve it apart from the life around it.",
                MemoryVerb.Trace => "The line has already been traced to this room. What remains is not another connection, but an order — and then a decision.",
                _ => "That way of remembering has done its work. This room asks for order, then Release.",
            };
            await Say(line, 4f, 23);
        }
        finally
        {
            _busy = false;
            UpdateVisuals();
        }
    }

    private void ShowChoice()
    {
        _choiceOpen = true;
        _selectedShard = -1;
        SetHoveredSlot(-1);
        if (_choicePanel != null)
            _choicePanel.Visible = true;
        UpdateVisuals();
        _wakingButton?.GrabFocus();
    }

    private void CloseChoice()
    {
        if (_endingStarted)
            return;
        _choiceOpen = false;
        if (_choicePanel != null)
            _choicePanel.Visible = false;
        GameState.Instance?.Toast("Not yet. The whole memory will wait with Mara a little longer.");
        UpdateVisuals();
    }

    private void ChooseEnding(string endingId)
    {
        if (_endingStarted || (endingId != "waking" && endingId != "kept_house"))
            return;

        _endingStarted = true;
        _choiceOpen = false;
        _busy = true;
        _selectedShard = -1;
        SetHoveredSlot(-1);
        if (_choicePanel != null)
            _choicePanel.Visible = false;

        var gs = GameState.Instance;
        gs?.SetFlag(ChoiceMadeFlag);
        gs?.MarkRoomCompleted("Room7");
        AudioManager.Play(endingId == "waking" ? Sfx.DoorUnlock : Sfx.ClarifyReveal,
            -5f, endingId == "waking" ? 1f : 0.9f);
        UpdateVisuals();

        if (GameUI.Instance != null)
        {
            GameUI.Instance.PlayAuthoredEnding(endingId, withIdaConversation: true);
        }
        else
        {
            // Defensive fallback for running the room without the autoload UI.
            gs?.FinishRun(endingId);
            GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
        }
    }

    // ------------------------------------------------------------ state/visuals

    private void UpdateVisuals()
    {
        var gs = GameState.Instance;

        for (int shard = 0; shard < GameState.MaxShards; shard++)
        {
            var button = _trayButtons[shard];
            if (button == null)
                continue;

            bool recovered = gs != null && gs.IsShardRecovered(shard);
            bool carried = IsCarried(shard);
            int placedSlot = gs != null ? System.Array.IndexOf(gs.PlacedShardBySlot, shard) : -1;
            bool selected = _selectedShard == shard;

            button.Text = !recovered
                ? "· · ·"
                : placedSlot >= 0
                    ? $"{ShortNames[shard]}\nreturned"
                    : selected
                        ? $"◆ {ShortNames[shard]}"
                        : ShortNames[shard];
            button.TooltipText = !recovered
                ? "Not recovered yet"
                : placedSlot >= 0
                    ? $"{GameState.ShardTitles[shard]} — returned. Its frame can give it back."
                    : GameState.ShardTitles[shard];
            button.Disabled = !carried || _busy || _choiceOpen || _endingStarted ||
                              Dialogue.IsShowing || GetTree().Paused;
            ApplyButtonStyle(button, selected);
        }

        for (int slot = 0; slot < GameState.MaxShards; slot++)
        {
            var label = _frameTitles[slot];
            if (label == null)
                continue;

            int placedShard = gs != null ? gs.PlacedShardBySlot[slot] : -1;
            if (placedShard >= 0)
            {
                label.Text = ShortNames[placedShard];
                label.AddThemeColorOverride("font_color", new Color(0.95f, 0.87f, 0.68f));
            }
            else
            {
                label.Text = "hollow";
                label.AddThemeColorOverride("font_color",
                    new Color(0.82f, 0.78f, 0.7f, 0.62f));
            }
        }

        QueueRedraw();
    }

    private static bool IsCarried(int shard)
    {
        return shard >= 0 && shard < Globals.SHARDS_COLLECTED.Count &&
               Globals.SHARDS_COLLECTED[shard];
    }

    private static Rect2 GetFrameRect(int slot)
    {
        float x = 281f + (slot % 3) * 220f;
        float y = slot < 3 ? 135f : 285f;
        return new Rect2(x, y, 162f, 110f);
    }

    private static int FindSlot(Vector2 position)
    {
        for (int i = 0; i < GameState.MaxShards; i++)
        {
            if (GetFrameRect(i).HasPoint(position))
                return i;
        }
        return -1;
    }

    private void SetHoveredSlot(int next)
    {
        if (_hoveredSlot == next)
            return;

        if (_hoverCursorOwned)
        {
            CursorHelper.EndHover(this);
            _hoverCursorOwned = false;
        }

        _hoveredSlot = next;
        if (_hoveredSlot >= 0)
        {
            CursorHelper.BeginHover(this, PointerCursorPath, 32, 32);
            _hoverCursorOwned = true;

            ulong now = Time.GetTicksMsec();
            if (now - _lastHoverTickMsec > 90)
            {
                _lastHoverTickMsec = now;
                AudioManager.Play(Sfx.UiHover, -13f);
            }
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
        _hoveredSlot = -1;
    }

    private async Task Say(string text, float duration = 4.5f, int fontSize = 24)
    {
        if (!GodotObject.IsInstanceValid(this) || string.IsNullOrWhiteSpace(text))
            return;
        await Dialogue.ShowText(this, text, duration, 576f, 324f, FontPath, fontSize);
    }

    private async Task SaySequence(params string[] lines)
    {
        if (lines == null)
            return;
        foreach (var line in lines)
        {
            if (!GodotObject.IsInstanceValid(this))
                return;
            await Say(line, 5f, 24);
        }
    }

    /// <summary>Room _Input runs before root GameUI buttons; never claim their area.</summary>
    private bool IsOverUi(Vector2 position)
    {
        if (position.Y >= 606f)
            return true;

        if (position.X >= 1070f && position.X <= 1144f && position.Y >= 136f && position.Y <= 208f) // icon-only Bag button, top-right
            return true;

        if (position.X >= 12f && position.X <= 276f && position.Y >= 552f &&
            SceneManager.TryGetBackTarget(SceneManager.CurrentScenePath, out _, out _))
            return true;

        if (position.X >= 876f && position.Y >= 552f &&
            GameState.Instance != null && GameState.Instance.TryGetLeaveScene(out _))
            return true;

        var gs = GameState.Instance;
        bool anyVerb = gs != null &&
                       (gs.HasVerb(MemoryVerb.Echo) || gs.HasVerb(MemoryVerb.Anchor) ||
                        gs.HasVerb(MemoryVerb.Trace) || gs.HasVerb(MemoryVerb.Release));
        return anyVerb && position.X >= 220f && position.X <= 932f &&
               position.Y >= 540f && position.Y <= 604f;
    }
}

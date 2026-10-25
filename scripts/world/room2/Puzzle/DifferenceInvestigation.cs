using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Room 2's investigation behind the photograph: "what changed?"
///
/// Mara is given one ordinary sitting-room evening for exactly five
/// seconds. The changed details are deliberately hidden while the intro
/// dialogue is still speaking, so those five seconds are the whole of
/// her look. The house then presents its edited version: the mantel
/// clock has run from 4:00 to 4:47, the red book has changed chairs,
/// the letter has vanished from the table, and the teacup has gone
/// over. The player clicks the changed places; finding all four earns
/// the little door key taped behind the photograph.
///
/// This is a standalone Node2D rather than a MemoryRoomBase room for
/// one reason: MemoryRoomBase registers Tab/hint targets for every
/// hotspot. A spot-the-difference test whose answers can be ringed by
/// the general hotspot reveal would not be a memory test at all.
/// </summary>
public partial class DifferenceInvestigation : Node2D
{
    private const string BackgroundPath = "res://assets/images/bgs/Difference_SittingRoom_BG.png";
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string SearchCursorPath = "res://assets/images/core/cursor_magnifying_glass.png";
    private const string Room2PuzzleScenePath = "res://scenes/world/room_2_puzzle.tscn";

    private const string SolvedFlag = "difference_room2_solved";
    private const string DoorKeyFlag = "item_hall_key_door";
    private const string IntroFlag = "difference_intro_heard";
    private const float MemoriseSeconds = 5f;

    private enum Phase
    {
        Intro,
        Memorise,
        Search,
        Resolve,
        Complete,
        Leaving,
    }

    private sealed class Difference
    {
        public string Id { get; }
        public Rect2 Rect { get; }
        public string SfxId { get; }
        public float SfxPitch { get; }
        public string FoundLine { get; }

        public Difference(string id, Rect2 rect, string sfxId, float sfxPitch, string foundLine)
        {
            Id = id;
            Rect = rect;
            SfxId = sfxId;
            SfxPitch = sfxPitch;
            FoundLine = foundLine;
        }

        public Vector2 Center => Rect.GetCenter();
    }

    // Search rectangles in the room's 1152x648 design space.
    private static readonly Difference[] Differences =
    [
        new("ChangedClock", new Rect2(168, 86, 82, 82), Sfx.ClockTick, 0.82f,
            "Four o'clock became 4:47 while the room was only pretending to wait with her. Of course it did."),
        new("MovedBook", new Rect2(748, 440, 78, 32), Sfx.PageTurn, 0.92f,
            "The red book changed chairs. Somebody sat where nobody is sitting now — or memory moved the reader and hoped I wouldn't ask where she went."),
        new("VanishedLetter", new Rect2(570, 432, 72, 40), Sfx.PageTurn, 1.12f,
            "The letter was on the table. Now even its rectangle of cleaner wood is trying to look innocent."),
        new("TippedCup", new Rect2(640, 425, 62, 48), Sfx.Examine, 0.78f,
            "The cup stood upright. Now the tea is out across the table, still warm enough in memory to smell faintly sweet."),
    ];

    private static readonly string[] WrongLines =
    [
        "No. That part is as it was.",
        "Still the same. Don't let the house make everything suspicious.",
        "No — that much stayed innocent.",
    ];

    private Texture2D _background;
    private FontFile _font;
    private Label _titleLabel;
    private Label _phaseLabel;
    private readonly HashSet<string> _found = new();

    private Phase _phase = Phase.Intro;
    private bool _detailsVisible;
    private bool _changedVisible;
    private float _memoriseRemaining;
    private int _lastCountdownWhole = -1;
    private bool _cursorOwned;
    private int _wrongClicks;
    private Vector2 _lastWrongPosition;
    private ulong _lastWrongMsec;

    public override void _Ready()
    {
        _background = GD.Load<Texture2D>(BackgroundPath);
        if (_background == null)
            Logger.Error($"[DifferenceInvestigation] Could not load background: {BackgroundPath}");
        _font = GD.Load<FontFile>(FontPath);

        // This is ordinary looking, done honestly. Clarity and a Memory
        // Verb carried in from a later revisit do not get to answer it.
        MemoryVerbSystem.Cancel(silent: true);
        ClaritySystem.Disarm(silent: true);

        BuildLabels();
        UpdatePhaseLabel();
        QueueRedraw();
        _ = BeginAsync();
    }

    private void BuildLabels()
    {
        _titleLabel = MakeLabel("The sitting room, remembered",
            new Vector2(326, 12), new Vector2(500, 34), 27,
            new Color(0.97f, 0.91f, 0.78f, 0.98f));
        _phaseLabel = MakeLabel(string.Empty,
            new Vector2(326, 45), new Vector2(500, 36), 21,
            new Color(0.93f, 0.88f, 0.76f, 0.96f));
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

    private async Task BeginAsync()
    {
        Dialogue.BeginInputBlock();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!StillHere())
                return;

            var gs = GameState.Instance;
            if (gs != null && (gs.HasFlag(SolvedFlag) || gs.IsShardRecovered(1)))
            {
                _phase = Phase.Leaving;
                SceneManager.ChangeScene(Room2PuzzleScenePath);
                return;
            }

            if (gs != null && !gs.HasFlag(IntroFlag))
            {
                await Say("Not the room as it flatters itself. The mantel. Two chairs. The little table between them. Hold it the way you'd hold a breath.");
                if (!StillHere())
                    return;
                await Say("Five seconds. Then tell me what the house moved while nobody was watching.");
                if (!StillHere())
                    return;
                gs.SetFlag(IntroFlag, save: false);
            }
            else
            {
                await Say("Again, then. Five seconds. No flinching at the parts that change.");
                if (!StillHere())
                    return;
            }
        }
        finally
        {
            Dialogue.EndInputBlock();
        }

        if (StillHere() && _phase == Phase.Intro)
            BeginMemorise();
    }

    private void BeginMemorise()
    {
        _phase = Phase.Memorise;
        _detailsVisible = true;
        _changedVisible = false;
        _memoriseRemaining = MemoriseSeconds;
        _lastCountdownWhole = -1;
        UpdatePhaseLabel();
        QueueRedraw();
    }

    private void BeginSearch()
    {
        _phase = Phase.Search;
        _detailsVisible = true;
        _changedVisible = true;
        _memoriseRemaining = 0f;
        AudioManager.Play(Sfx.ClarifyReveal, -13f, 0.74f);
        AudioManager.Play(Sfx.PageTurn, -14f, 0.72f);
        UpdatePhaseLabel();
        QueueRedraw();
        GameState.Instance?.Toast("What changed? Click each changed place. Press H if the room needs narrowing.");
    }

    public override void _Process(double delta)
    {
        UpdateCursor();

        bool blocked = GetTree().Paused || Dialogue.IsShowing || Dialogue.IsInputBlocked ||
            SceneManager.IsChanging ||
            (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());

        if (_phase == Phase.Memorise && !blocked)
        {
            _memoriseRemaining -= (float)delta;
            int whole = Mathf.Clamp(Mathf.CeilToInt(_memoriseRemaining), 0, (int)MemoriseSeconds);
            if (whole != _lastCountdownWhole)
            {
                _lastCountdownWhole = whole;
                if (whole > 0)
                    AudioManager.Play(Sfx.ClockTick, -13f, 0.9f + (5 - whole) * 0.05f);
                UpdatePhaseLabel();
            }

            if (_memoriseRemaining <= 0f)
                BeginSearch();
        }

        if (_phase == Phase.Memorise || _phase == Phase.Search || _phase == Phase.Resolve)
            QueueRedraw();
    }

    private void UpdateCursor()
    {
        Vector2 mouse = GetGlobalMousePosition();
        bool wantCursor = _phase == Phase.Search &&
            !GetTree().Paused && !Dialogue.IsShowing && !Dialogue.IsInputBlocked &&
            !IsOverUi(mouse) &&
            new Rect2(Vector2.Zero, new Vector2(1152, 648)).HasPoint(mouse);

        if (wantCursor && !_cursorOwned)
        {
            CursorHelper.BeginHover(this, SearchCursorPath, 32, 32);
            _cursorOwned = true;
        }
        else if (!wantCursor && _cursorOwned)
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

    public override void _Input(InputEvent @event)
    {
        if (_phase != Phase.Search || GetTree().Paused || SceneManager.IsChanging ||
            Dialogue.IsShowing || Dialogue.IsInputBlocked)
            return;

        if (@event is not InputEventMouseButton mouse || !mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Left)
            return;

        if (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen())
            return;

        Vector2 position = GetGlobalMousePosition();
        if (IsOverUi(position))
            return;

        GetTree().Root.SetInputAsHandled();
        var difference = FindDifference(position);
        if (difference == null)
        {
            RegisterWrongClick(position);
            return;
        }

        if (_found.Contains(difference.Id))
        {
            AudioManager.Play(Sfx.UiClick, -13f, 0.95f);
            GameState.Instance?.Toast("Already noticed. Memory is allowed to keep that one.");
            return;
        }

        _ = CorrectAsync(difference);
    }

    private static Difference FindDifference(Vector2 position)
    {
        // Later entries are the smaller table details, so they win the
        // one-pixel kiss between the letter and cup rectangles.
        for (int i = Differences.Length - 1; i >= 0; i--)
        {
            if (Differences[i].Rect.HasPoint(position))
                return Differences[i];
        }
        return null;
    }

    private void RegisterWrongClick(Vector2 position)
    {
        _wrongClicks++;
        AudioManager.Play(Sfx.Denied, -10f, 0.94f);

        var gs = GameState.Instance;
        if (gs != null)
        {
            // GameState deliberately gives this scene no tracker room,
            // so the investigation grades its own guesses with the
            // game's published loose-click costs.
            ulong now = Time.GetTicksMsec();
            bool repeated = now - _lastWrongMsec <= 1400 &&
                position.DistanceSquaredTo(_lastWrongPosition) <= 110f * 110f;
            _lastWrongPosition = position;
            _lastWrongMsec = now;
            gs.AddClickCorruption(repeated
                ? GameState.RepeatedNonInteractableClickPenalty
                : GameState.NonInteractableClickPenalty);
        }

        string toast = _wrongClicks == 1
            ? $"{WrongLines[0]} Wrong guesses strain MEMORY, like empty clicks."
            : _wrongClicks >= 4
                ? "Still the same. Press H — let the house narrow it before guessing frays the room."
                : WrongLines[_wrongClicks % WrongLines.Length];
        gs?.Toast(toast);
    }

    private async Task CorrectAsync(Difference difference)
    {
        if (_phase != Phase.Search || !_found.Add(difference.Id))
            return;

        _phase = Phase.Resolve;
        UpdateCursor();
        float chimePitch = 0.9f + _found.Count * 0.07f;
        AudioManager.PlayVaried(difference.SfxId, -7f, difference.SfxPitch, 0.02f);
        AudioManager.Play(Sfx.WordChime, -9f, chimePitch);
        Hotspots.SpawnRipple(difference.Center, new Color(1f, 0.88f, 0.55f));
        UpdatePhaseLabel();
        QueueRedraw();

        Dialogue.BeginInputBlock();
        try
        {
            await Say(difference.FoundLine);
        }
        finally
        {
            Dialogue.EndInputBlock();
        }

        if (!StillHere())
            return;

        if (_found.Count >= Differences.Length)
            await CompleteAsync();
        else
        {
            _phase = Phase.Search;
            UpdatePhaseLabel();
            QueueRedraw();
        }
    }

    private async Task CompleteAsync()
    {
        _phase = Phase.Complete;
        UpdatePhaseLabel();
        QueueRedraw();

        Dialogue.BeginInputBlock();
        try
        {
            await Say("That's the evening. Not polished. Not merciful. Just accurately untidy.");
            if (!StillHere())
                return;
            await Say("And accuracy has weight. Behind the photograph, old tape gives with a tiny tearing sound.");
            if (!StillHere())
                return;
            AudioManager.Play(Sfx.KeyType, -8f, 0.95f);
            await Say("A key. Not a metaphor wearing key-coloured paint. iron — and its bow is a little door, down to a knob too small for any hand but memory's.");
            if (!StillHere())
                return;
        }
        finally
        {
            Dialogue.EndInputBlock();
        }

        var gs = GameState.Instance;
        if (gs != null)
        {
            gs.NotifyPuzzleSolved();
            gs.SetFlag(SolvedFlag, save: false);
            gs.SetFlag(DoorKeyFlag, save: false);
            gs.UnlockClue("difference_room");
            gs.UnlockClue("hall_key_door");
            gs.Toast("Little door key — kept in Mara's bag (B). The photograph can be faced now.");
            gs.SaveGame("difference investigation solved");
        }

        AudioManager.Play(Sfx.PuzzleSolve, -7f, 1.02f);
        _phase = Phase.Leaving;
        SceneManager.ChangeScene(Room2PuzzleScenePath);
    }

    private void UpdatePhaseLabel()
    {
        if (_phaseLabel == null)
            return;

        _phaseLabel.Text = _phase switch
        {
            Phase.Memorise => $"Look. Really look.  {Mathf.Clamp(Mathf.CeilToInt(_memoriseRemaining), 0, (int)MemoriseSeconds)}",
            Phase.Search => $"What changed?  ·  found {_found.Count} of {Differences.Length}",
            Phase.Resolve => $"What changed?  ·  found {_found.Count} of {Differences.Length}",
            Phase.Complete => "Four changes, remembered in Mara's own hand.",
            _ => string.Empty,
        };
    }

    private async Task Say(string line)
    {
        await Dialogue.ShowText(this, line, 5.5f, 576f, 324f, fontSize: 24);
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();

    private bool IsOverUi(Vector2 position)
    {
        var ui = GameUI.Instance;
        if (ui != null)
            return ui.IsPointOverInteractiveHud(position);

        // Standalone/one-frame fallback: protect only the Bag button's
        // familiar corner, not a strip of the investigation.
        return position.X >= 1070f && position.X <= 1144f &&
               position.Y >= 136f && position.Y <= 208f;
    }

    // ------------------------------------------------------------ drawing

    public override void _Draw()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        if (_background != null)
            DrawTextureRect(_background, screen, false);
        else
            DrawRect(screen, new Color(0.08f, 0.066f, 0.05f), true);

        if (!_detailsVisible)
        {
            // Before the five seconds begin, the mantel clock is only a
            // blank face. The player cannot start memorising early
            // through the introduction dialogue.
            DrawClockFace(blank: true);
        }
        else if (!_changedVisible)
        {
            DrawClockFace(blank: false, hour: 4f, minute: 0f);
            DrawBook(new Vector2(493, 439));
            DrawLetter(new Vector2(578, 438));
            DrawUprightCup(new Vector2(671, 448));
        }
        else
        {
            DrawClockFace(blank: false, hour: 4f + 47f / 60f, minute: 47f);
            DrawBook(new Vector2(755, 446));
            // The letter is gone. Nothing is drawn on purpose.
            DrawTippedCup(new Vector2(671, 450));
            DrawFoundRings();
        }

        DrawPhasePanel();
    }

    private void DrawPhasePanel()
    {
        var panel = new Rect2(300, 8, 552, 84);
        DrawRect(panel, new Color(0.025f, 0.02f, 0.016f, 0.72f), true);
        DrawRect(panel, new Color(0.74f, 0.6f, 0.36f, 0.55f), false, 1.5f);

        if (_phase == Phase.Memorise)
        {
            float frac = Mathf.Clamp(_memoriseRemaining / MemoriseSeconds, 0f, 1f);
            var track = new Rect2(426, 81, 300, 4);
            DrawRect(track, new Color(1f, 0.92f, 0.68f, 0.16f), true);
            DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * frac, track.Size.Y)),
                new Color(1f, 0.86f, 0.52f, 0.9f), true);
        }
        else if (_detailsVisible)
        {
            for (int i = 0; i < Differences.Length; i++)
            {
                Vector2 center = new(552 + i * 16, 83);
                bool found = _found.Contains(Differences[i].Id);
                DrawCircle(center, 4.5f, found
                    ? new Color(1f, 0.86f, 0.5f, 0.95f)
                    : new Color(1f, 0.92f, 0.75f, 0.24f));
            }
        }
    }

    private void DrawFoundRings()
    {
        float pulse = Settings.ReduceMotionOrDefault()
            ? 0f
            : (float)Math.Sin(Time.GetTicksMsec() / 230.0) * 2.2f;
        var gold = new Color(1f, 0.88f, 0.52f, 0.92f);
        var goldSoft = new Color(1f, 0.88f, 0.52f, 0.28f);

        foreach (var difference in Differences)
        {
            if (!_found.Contains(difference.Id))
                continue;
            float radius = Mathf.Min(difference.Rect.Size.X, difference.Rect.Size.Y) * 0.58f + pulse;
            DrawArc(difference.Center, radius, 0f, Mathf.Tau, 44, gold, 2.2f, true);
            DrawArc(difference.Center, radius * 0.68f, 0f, Mathf.Tau, 36, goldSoft, 1.2f, true);
            DrawCircle(difference.Center, 2.4f, gold);
        }
    }

    private void DrawClockFace(bool blank, float hour = 4f, float minute = 0f)
    {
        Vector2 center = new(209, 122);
        const float radius = 30f;
        var ink = new Color(0.13f, 0.1f, 0.08f);
        var face = new Color(0.93f, 0.87f, 0.72f);

        DrawCircle(center + new Vector2(2, 3), radius + 2f, new Color(0.05f, 0.035f, 0.025f, 0.55f));
        DrawCircle(center, radius, face);
        DrawArc(center, radius, 0f, Mathf.Tau, 48, new Color(0.34f, 0.22f, 0.13f), 3f, true);
        DrawArc(center, radius - 4f, 0f, Mathf.Tau, 44, new Color(0.13f, 0.1f, 0.08f, 0.35f), 1.2f, true);

        if (blank)
        {
            // A face with the breath held out of it: enough shape to be
            // a clock, no time to memorise before Mara's five seconds.
            DrawCircle(center, 3f, new Color(0.13f, 0.1f, 0.08f, 0.22f));
            return;
        }

        for (int i = 0; i < 12; i++)
        {
            float angle = -Mathf.Pi / 2f + i * Mathf.Tau / 12f;
            Vector2 dir = Vector2.FromAngle(angle);
            float inner = i % 3 == 0 ? 20f : 23f;
            DrawLine(center + dir * inner, center + dir * 26f, ink, i % 3 == 0 ? 2.4f : 1.4f, true);
        }

        float hourAngle = -Mathf.Pi / 2f + (hour % 12f) * Mathf.Tau / 12f;
        float minuteAngle = -Mathf.Pi / 2f + (minute % 60f) * Mathf.Tau / 60f;
        DrawLine(center, center + Vector2.FromAngle(hourAngle) * 16f, ink, 4.2f, true);
        DrawLine(center, center + Vector2.FromAngle(minuteAngle) * 23f, ink, 3f, true);
        DrawCircle(center, 3.4f, ink);
    }

    private void DrawBook(Vector2 topLeft)
    {
        var shadow = new Rect2(topLeft + new Vector2(3, 7), new Vector2(62, 15));
        DrawRect(shadow, new Color(0.03f, 0.02f, 0.015f, 0.42f), true);

        DrawRect(new Rect2(topLeft + new Vector2(4, 12), new Vector2(56, 7)),
            new Color(0.88f, 0.8f, 0.64f), true);
        DrawRect(new Rect2(topLeft + new Vector2(0, 5), new Vector2(60, 13)),
            new Color(0.48f, 0.13f, 0.11f), true);

        Vector2[] top =
        [
            topLeft + new Vector2(0, 5),
            topLeft + new Vector2(9, 0),
            topLeft + new Vector2(66, 0),
            topLeft + new Vector2(60, 5),
        ];
        DrawColoredPolygon(top, new Color(0.62f, 0.19f, 0.15f));
        DrawRect(new Rect2(topLeft + new Vector2(7, 5), new Vector2(3, 13)),
            new Color(0.3f, 0.07f, 0.06f), true);
        DrawRect(new Rect2(topLeft + new Vector2(49, 9), new Vector2(7, 5)),
            new Color(0.82f, 0.64f, 0.32f), true);
        DrawRect(new Rect2(topLeft + new Vector2(4, 17), new Vector2(56, 2)),
            new Color(0.08f, 0.05f, 0.04f, 0.7f), true);
    }

    private void DrawLetter(Vector2 topLeft)
    {
        Vector2[] paper =
        [
            topLeft + new Vector2(0, 5),
            topLeft + new Vector2(54, 0),
            topLeft + new Vector2(58, 22),
            topLeft + new Vector2(4, 27),
        ];
        Vector2[] shadow =
        [
            paper[0] + new Vector2(2, 3),
            paper[1] + new Vector2(2, 3),
            paper[2] + new Vector2(2, 3),
            paper[3] + new Vector2(2, 3),
        ];
        DrawColoredPolygon(shadow, new Color(0.03f, 0.02f, 0.015f, 0.38f));
        DrawColoredPolygon(paper, new Color(0.9f, 0.84f, 0.68f));

        var edge = new Color(0.35f, 0.28f, 0.2f, 0.8f);
        for (int i = 0; i < paper.Length; i++)
            DrawLine(paper[i], paper[(i + 1) % paper.Length], edge, 1.2f, true);

        var ink = new Color(0.24f, 0.22f, 0.22f, 0.78f);
        DrawLine(topLeft + new Vector2(9, 8), topLeft + new Vector2(45, 4), ink, 1.4f, true);
        DrawLine(topLeft + new Vector2(10, 14), topLeft + new Vector2(39, 10), ink, 1.2f, true);
        DrawLine(topLeft + new Vector2(11, 20), topLeft + new Vector2(48, 16), ink, 1.2f, true);
    }

    private void DrawUprightCup(Vector2 center)
    {
        DrawEllipse(center + new Vector2(0, 15), new Vector2(29, 7), new Color(0.28f, 0.2f, 0.15f));
        DrawEllipse(center + new Vector2(0, 14), new Vector2(27, 6), new Color(0.88f, 0.82f, 0.7f));

        Vector2[] cup =
        [
            center + new Vector2(-19, -10),
            center + new Vector2(19, -10),
            center + new Vector2(15, 11),
            center + new Vector2(-15, 11),
        ];
        DrawColoredPolygon(cup, new Color(0.9f, 0.84f, 0.71f));
        DrawEllipse(center + new Vector2(0, -10), new Vector2(19, 4.5f), new Color(0.32f, 0.17f, 0.08f));
        DrawArc(center + new Vector2(20, 0), 7f, -Mathf.Pi * 0.45f, Mathf.Pi * 0.45f, 18,
            new Color(0.9f, 0.84f, 0.71f), 3f, true);

        var steam = new Color(1f, 0.96f, 0.86f, 0.42f);
        DrawLine(center + new Vector2(-7, -16), center + new Vector2(-4, -24), steam, 1.6f, true);
        DrawLine(center + new Vector2(5, -16), center + new Vector2(8, -25), steam, 1.6f, true);
    }

    private void DrawTippedCup(Vector2 center)
    {
        DrawEllipse(center + new Vector2(-16, 16), new Vector2(24, 5.5f), new Color(0.84f, 0.78f, 0.65f));
        DrawEllipse(center + new Vector2(8, 15), new Vector2(32, 7), new Color(0.3f, 0.15f, 0.065f, 0.92f));
        DrawRect(new Rect2(center + new Vector2(22, 14), new Vector2(5, 13)),
            new Color(0.3f, 0.15f, 0.065f, 0.75f), true);

        Vector2[] cup =
        [
            center + new Vector2(-24, -7),
            center + new Vector2(8, -14),
            center + new Vector2(13, 9),
            center + new Vector2(-18, 14),
        ];
        DrawColoredPolygon(cup, new Color(0.89f, 0.83f, 0.7f));
        DrawEllipse(center + new Vector2(11, -2), new Vector2(6, 12), new Color(0.32f, 0.17f, 0.08f));
        DrawEllipse(center + new Vector2(13, -2), new Vector2(3.5f, 8), new Color(0.9f, 0.84f, 0.71f));
        DrawArc(center + new Vector2(-23, -8), 7f, Mathf.Pi * 0.7f, Mathf.Pi * 1.55f, 18,
            new Color(0.89f, 0.83f, 0.7f), 3f, true);
    }

    private void DrawEllipse(Vector2 center, Vector2 radii, Color color)
    {
        const int segments = 28;
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.Tau / segments;
            points[i] = center + new Vector2(Mathf.Cos(angle) * radii.X, Mathf.Sin(angle) * radii.Y);
        }
        DrawColoredPolygon(points, color);
    }
}

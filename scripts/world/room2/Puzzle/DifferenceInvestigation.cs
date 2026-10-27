using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Room 2's investigation: the room in the photograph, remembered wrong.
///
/// Behind the sitting-room photograph is a second photograph — the
/// room itself, the way it was. Mara studies it (in hand, and in her
/// bag whenever she likes), and then the house does the rest: a wind
/// takes the fire, the dark takes the room, the fire catches again
/// on *this* room — the same room, with something off. A memory held
/// wrong will not hold at all: the ceiling starts to come down in
/// dust and cracks while Mara points at everything the house moved
/// (the clock run to 4:47, the red book changed chairs, the letter
/// vanished, the cup gone over). Name them all before the room goes
/// and the evening steadies — and sets a little door key down beside
/// the globe, in the real room, to be picked up by hand. Run out of
/// time and the memory does what strained memories do: it breaks.
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
    private const string Room2ScenePath = "res://scenes/world/room_2.tscn";

    private const string SolvedFlag = "difference_room2_solved";
    private const string DoorKeyFlag = "item_hall_key_door";
    private const string ArrivalHeardFlag = "difference_arrival_heard";
    private const float CrumbleSeconds = 35f;
    // A wrong guess doesn't just cost seconds — the breaking
    // itself speeds up, this much more for every one.
    private const float WrongClickAcceleration = 0.35f;

    // The painted fireplace's mouth, in 1152×648 design space — where
    // the fire lives, and where the ashes sit while it doesn't.
    private static readonly Rect2 FireRect = new(100, 332, 158, 196);

    private enum Phase
    {
        Arrival,
        Relight,
        Search,
        Resolve,
        SolveBeat,
        Breaking,
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

    // Cracks that walk the glass itself as the shaking worsens —
    // not the walls: the screen she's being seen through, giving up
    // a segment at a time while the crumble runs.
    private static readonly Vector2[][] ScreenCrackChains =
    [
        [new Vector2(576, -8), new Vector2(552, 84), new Vector2(588, 168), new Vector2(556, 262), new Vector2(590, 352), new Vector2(570, 452)],
        [new Vector2(-8, 210), new Vector2(96, 238), new Vector2(188, 216), new Vector2(284, 252), new Vector2(372, 232)],
        [new Vector2(1160, 430), new Vector2(1052, 406), new Vector2(962, 438), new Vector2(868, 412), new Vector2(786, 440)],
        [new Vector2(300, 656), new Vector2(342, 560), new Vector2(316, 468), new Vector2(356, 384)],
    ];

    // Cracks that walk the walls as the room gives up, point by point.
    private static readonly Vector2[][] CrackChains =
    [
        [new Vector2(420, 40), new Vector2(452, 96), new Vector2(436, 150), new Vector2(470, 210), new Vector2(458, 268)],
        [new Vector2(820, 26), new Vector2(796, 84), new Vector2(826, 138), new Vector2(800, 196)],
        [new Vector2(96, 560), new Vector2(160, 516), new Vector2(214, 528), new Vector2(272, 486), new Vector2(330, 496)],
        [new Vector2(1050, 520), new Vector2(996, 472), new Vector2(1010, 420), new Vector2(962, 372)],
    ];

    private Texture2D _background;
    private FontFile _font;
    private Label _titleLabel;
    private Label _phaseLabel;
    private readonly HashSet<string> _found = new();
    private readonly RandomNumberGenerator _rng = new();

    private Phase _phase = Phase.Arrival;
    private float _lightLevel = 0.04f;
    private float _lightTarget = 0.04f;
    private float _crumbleRemaining = CrumbleSeconds;
    private float _crumbleRate = 1f;
    // The migraine's weather: red at the edges, flickering.
    private float _migraineRedLeft;
    private int _lastCountdownWhole = -1;
    private bool _warned30;
    private bool _warned15;
    private float _rumbleTimer;
    private bool _broken;
    private bool _sawShatter;
    private bool _left;
    private float _glitchTimer = 3f;
    private float _glitchLeft;
    // While she breathes a migraine down, the room breathes with her.
    private float _breathLeft;
    private bool _cursorOwned;
    private int _wrongClicks;
    private Vector2 _lastWrongPosition;
    private ulong _lastWrongMsec;

    private struct DustMote { public Vector2 Pos; public float Speed; public float Size; }
    private readonly DustMote[] _dust = new DustMote[26];

    public override void _Ready()
    {
        _background = GD.Load<Texture2D>(BackgroundPath);
        if (_background == null)
            Logger.Error($"[DifferenceInvestigation] Could not load background: {BackgroundPath}");
        _font = GD.Load<FontFile>(FontPath);
        _rng.Randomize();
        for (int i = 0; i < _dust.Length; i++)
        {
            _dust[i] = new DustMote
            {
                Pos = new Vector2(_rng.RandfRange(40f, 1112f), _rng.RandfRange(0f, 648f)),
                Speed = _rng.RandfRange(14f, 42f),
                Size = _rng.RandfRange(1.5f, 3.2f),
            };
        }

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
            if (gs != null && gs.HasFlag(DoorKeyFlag))
            {
                _phase = Phase.Leaving;
                SceneManager.ChangeScene(Room2ScenePath);
                return;
            }

            // Dark first. The wind has done its work; somewhere a
            // fire that was lit a moment ago is only a smell.
            _phase = Phase.Arrival;
            _lightLevel = 0.04f;
            _lightTarget = 0.04f;
            UpdatePhaseLabel();
            QueueRedraw();
            await WaitSeconds(1.15f);
            if (!StillHere())
                return;

            // It catches the way memories do: all at once, and certain.
            // The wrong room has relit the fire — moving flame and
            // all — exactly as the real one burned a minute ago.
            _phase = Phase.Relight;
            gs?.ClearFlag("difference_fire_out", save: false);
            AudioManager.Play(Sfx.FireWhoomph, -7f);
            _lightTarget = 1f;
            await WaitSeconds(1.5f);
            if (!StillHere())
                return;

            bool firstArrival = gs != null && !gs.HasFlag(ArrivalHeardFlag);
            if (firstArrival)
            {
                await Say("The fire catches. And— oh. Oh, I know this room. This is the room in the photograph. The mantel. The two chairs. The little table between them.");
                if (!StillHere())
                    return;
                await Say("…Only something's off. I can feel it before I can see it, like a word said in the wrong order. Something here is not the way the photograph kept it.");
                if (!StillHere())
                    return;

                // Remembering this hard has a price — and it is not
                // reported. It lands, the way it lands on the journeys:
                // a cry first, the light strobing, then the slow work
                // of breathing it down while the room breathes too.
                AudioManager.Play(Sfx.ScreenCrack2, -8f, 0.75f);
                _lightTarget = 0.12f;
                _glitchLeft = 0.6f;
                _migraineRedLeft = 2.6f;
                await Say("Ah—! Ah. There. THERE it is—");
                if (!StillHere())
                    return;
                for (int i = 0; i < 5; i++)
                {
                    _lightTarget = i % 2 == 0 ? 0.85f : 0.18f;
                    _glitchLeft = 0.16f;
                    AudioManager.Play(Sfx.ScreenCrack3, -14f, 0.8f + i * 0.06f);
                    await WaitSeconds(0.28f);
                    if (!StillHere())
                        return;
                }
                _lightTarget = 1f;
                _breathLeft = 9f;
                AudioManager.Play(Sfx.BreathSlow, -12f, 0.9f);
                await Say("—right behind the eye. The migraine that lives in this house with me. …Breathe. Just breathe, Mara. It passes. It has manners like that. The room, on the other hand—");
                if (!StillHere())
                    return;
                gs?.SetFlag(ArrivalHeardFlag, save: false);
            }
            else
            {
                await Say("The room again. Wrong again. Hold together — both of us.");
                if (!StillHere())
                    return;
            }
        }
        finally
        {
            Dialogue.EndInputBlock();
        }

        if (StillHere() && _phase == Phase.Relight)
            BeginSearch();
    }

    private void BeginSearch()
    {
        _phase = Phase.Search;
        _crumbleRemaining = CrumbleSeconds;
        _crumbleRate = 1f;
        _lastCountdownWhole = -1;
        _rumbleTimer = 0.4f;
        AudioManager.Play(Sfx.CrumbleRumble, -12f, 0.95f);
        UpdatePhaseLabel();
        QueueRedraw();
        GameState.Instance?.Toast("The memory is breaking. Click everything that's wrong — the photograph is in the bag (B->look closer).");
        _ = CrumbleOpeningLineAsync();
    }

    private async Task CrumbleOpeningLineAsync()
    {
        Dialogue.BeginInputBlock();
        try
        {
            await Say("The ceiling just— dust. It's coming apart. A memory held wrong won't hold at all. Find what's wrong. Find it the way the photograph kept it. Quickly—!");
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Max((float)delta, 0f);
        UpdateCursor();

        // Firelight, whatever state the argument is in.
        _lightLevel = Mathf.MoveToward(_lightLevel, _lightTarget, dt * 0.85f);

        bool blocked = GetTree().Paused || Dialogue.IsShowing || Dialogue.IsInputBlocked ||
            SceneManager.IsChanging ||
            (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());

        if (_phase == Phase.Search)
        {
            float gone = 1f - Mathf.Clamp(_crumbleRemaining / CrumbleSeconds, 0f, 1f);
            UpdateDust(dt, 0.4f + gone);
            ApplyShake(gone);

            if (!blocked)
            {
                _crumbleRemaining -= dt * _crumbleRate;

                int whole = Mathf.Clamp(Mathf.CeilToInt(_crumbleRemaining), 0, (int)CrumbleSeconds);
                if (whole != _lastCountdownWhole)
                {
                    _lastCountdownWhole = whole;
                    if (whole <= 10 && whole > 0)
                        AudioManager.Play(Sfx.ClockTick, -11f, 0.85f);
                    UpdatePhaseLabel();
                }

                if (!_warned30 && _crumbleRemaining <= 20f)
                {
                    _warned30 = true;
                    GameState.Instance?.Toast("\"It's going— the walls— think, Mara. The photograph. The photograph knows.\"");
                }
                if (!_warned15 && _crumbleRemaining <= 10f)
                {
                    _warned15 = true;
                    GameState.Instance?.Toast("\"Hold on. HOLD ON—\"");
                    AudioManager.Play(Sfx.ScreenCrack2, -9f, 0.9f);
                }

                _rumbleTimer -= dt;
                if (_rumbleTimer <= 0f)
                {
                    _rumbleTimer = _rng.RandfRange(1.4f, 2.4f);
                    AudioManager.Play(Sfx.CrumbleRumble, -13f, _rng.RandfRange(0.82f, 1.0f));
                }

                // The migraine keeps touching the edges of the room:
                // light stutters, the walls jump a frame sideways.
                _glitchTimer -= dt;
                if (_glitchTimer <= 0f)
                {
                    _glitchTimer = _rng.RandfRange(2.2f, 4.6f);
                    _glitchLeft = _rng.RandfRange(0.14f, 0.3f);
                    AudioManager.Play(Sfx.ScreenCrack3, -15f, _rng.RandfRange(0.8f, 1.1f));
                }

                if (_crumbleRemaining <= 0f)
                    _ = BreakDownAsync();
            }
        }
        else if (_breathLeft > 0f && !Settings.ReduceMotionOrDefault())
        {
            // Not the crumble's shaking — her own slow tide. The
            // whole room rises and settles on the breath she's
            // using to talk the migraine down.
            _breathLeft = Mathf.Max(0f, _breathLeft - dt);
            double t = Time.GetTicksMsec() / 1000.0;
            float tide = (float)Math.Sin(t * (Mathf.Tau / 4.4));
            Position = new Vector2(tide * 2.0f,
                (float)Math.Sin(t * (Mathf.Tau / 4.4) + 0.6) * 3.2f);
        }
        else
        {
            Position = Vector2.Zero;
        }
        _glitchLeft = Mathf.Max(0f, _glitchLeft - dt);
        _migraineRedLeft = Mathf.Max(0f, _migraineRedLeft - dt);

        if (_broken && !_left)
        {
            bool shattering = GameUI.Instance?.IsShatterPlaying ?? false;
            if (shattering)
                _sawShatter = true;
            else if (_sawShatter)
            {
                // Gathered back together, she wakes in the sitting
                // room itself — the photograph is in her bag, and
                // the retry starts from there now, never the drawer.
                _left = true;
                _phase = Phase.Leaving;
                SceneManager.ChangeScene(Room2ScenePath);
            }
        }

        QueueRedraw();
    }

    private void ApplyShake(float gone)
    {
        if (Settings.ReduceMotionOrDefault())
        {
            Position = Vector2.Zero;
            return;
        }
        float amp = (3.0f + 7.0f * gone) * (1f - 0.13f * _found.Count);
        if (_glitchLeft > 0f)
            amp *= 2.4f;
        Position = new Vector2(
            _rng.RandfRange(-amp, amp),
            _rng.RandfRange(-amp * 0.7f, amp * 0.7f));
    }

    private void UpdateDust(float dt, float rate)
    {
        for (int i = 0; i < _dust.Length; i++)
        {
            var m = _dust[i];
            m.Pos.Y += m.Speed * rate * dt;
            m.Pos.X += Mathf.Sin((Time.GetTicksMsec() + (ulong)(i * 397)) / 900.0f) * 6f * dt;
            if (m.Pos.Y > 652f)
            {
                m.Pos.Y = -4f;
                m.Pos.X = _rng.RandfRange(40f, 1112f);
            }
            _dust[i] = m;
        }
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
        Position = Vector2.Zero;
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

        // A click, or F: the house's hand, for the thing under the
        // cursor. Same reach, same rules, either way.
        bool clicked = @event is InputEventMouseButton mouse && mouse.Pressed &&
            mouse.ButtonIndex == MouseButton.Left;
        bool useKey = @event is InputEventKey keyEvent && keyEvent.Pressed &&
            !keyEvent.Echo && keyEvent.PhysicalKeycode == Key.F;
        if (!clicked && !useKey)
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
        // Not a fee. An acceleration: from here the memory breaks
        // faster, and every further wrong guess presses harder.
        _crumbleRate += WrongClickAcceleration;
        AudioManager.Play(Sfx.Denied, -10f, 0.94f);
        AudioManager.Play(Sfx.CrumbleRumble, -11f, 0.8f);

        var gs = GameState.Instance;
        if (gs != null)
        {
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
            ? $"{WrongLines[0]} And the ceiling gives a little more — wrong guesses make the breaking run faster."
            : _wrongClicks >= 4
                ? "Still the same. The photograph, Mara. Bag (B) — look at it properly before the room goes."
                : WrongLines[_wrongClicks % WrongLines.Length];
        gs?.Toast(toast);
        UpdatePhaseLabel();
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

        if (!StillHere() || _broken)
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
        _phase = Phase.SolveBeat;
        Position = Vector2.Zero;
        UpdatePhaseLabel();
        QueueRedraw();

        Dialogue.BeginInputBlock();
        try
        {
            await Say("That's the evening. Not polished. Not merciful. Just accurately untidy — and the room… stops. Listen. It stopped coming down.");
            if (!StillHere())
                return;

            // The fire goes out the way it came in: all at once —
            // and the moving flames are gone before the light has
            // even begun to fall, exactly as the first wind took it.
            GameState.Instance?.SetFlag("difference_fire_out", save: false);
            AudioManager.Play(Sfx.WindGust, -16f, 0.7f);
            Dialogue.Gust(1.1f, 0.45f);
            _lightTarget = 0.04f;
            await WaitSeconds(1.0f);
            if (!StillHere())
                return;

            GameState.Instance?.ClearFlag("difference_fire_out", save: false);
            AudioManager.Play(Sfx.FireWhoomph, -9f, 0.9f);
            _lightTarget = 1f;
            await WaitSeconds(1.2f);
            if (!StillHere())
                return;

            await Say("And on the little table — beside the globe, exactly where the letter used to live its ordinary life and no key was a minute ago — a key. The room set it down like a held breath let out. He was right to trust the condition. Annoyingly, lovingly right.");
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
            gs.UnlockClue("difference_room");
            gs.Toast("The memory held. A little door key is waiting on the little table in the sitting room, beside the globe — click it to pick it up.");
            gs.SaveGame("difference investigation solved");
        }

        AudioManager.Play(Sfx.PuzzleSolve, -7f, 1.02f);
        _phase = Phase.Leaving;
        SceneManager.ChangeScene(Room2ScenePath);
    }

    private async Task BreakDownAsync()
    {
        if (_phase != Phase.Search || _broken)
            return;
        _broken = true;
        _phase = Phase.Breaking;
        Position = Vector2.Zero;
        UpdateCursor();

        AudioManager.Play(Sfx.ScreenCrack1, -4f, 0.8f);
        AudioManager.Play(Sfx.CrumbleRumble, -5f, 0.7f);
        Dialogue.BeginInputBlock();
        try
        {
            await Say("No— no no, stay— the ceiling— I can't hold it if I can't—");
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
        if (!StillHere())
            return;

        // A memory held wrong past its limit does what strained
        // memories do in this house. The shatter machinery — and its
        // mercy — takes it from here.
        GameState.Instance?.AddCorruption(GameState.ShatterThreshold);
    }

    private void UpdatePhaseLabel()
    {
        if (_phaseLabel == null)
            return;

        _phaseLabel.Text = _phase switch
        {
            Phase.Arrival => "…",
            Phase.Search => $"The memory is breaking — what's wrong?  ·  named {_found.Count} of {Differences.Length}",
            Phase.Resolve => $"The memory is breaking — what's wrong?  ·  named {_found.Count} of {Differences.Length}",
            Phase.SolveBeat => "Four changes, remembered in Mara's own hand.",
            Phase.Breaking => "—",
            _ => string.Empty,
        };
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

    private bool IsOverUi(Vector2 position)
    {
        var ui = GameUI.Instance;
        if (ui != null)
            return ui.IsPointOverInteractiveHud(position);

        return position.X >= 1070f && position.X <= 1144f &&
               position.Y >= 136f && position.Y <= 208f;
    }

    // ------------------------------------------------------------ drawing

    public override void _Draw()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        if (_background != null)
        {
            DrawTextureRect(_background, screen, false);
            if (_glitchLeft > 0f && !Settings.ReduceMotionOrDefault())
            {
                float off = _rng.RandfRange(-9f, 9f);
                DrawTextureRect(_background, new Rect2(new Vector2(off, 0), screen.Size), false,
                    new Color(1f, 0.6f, 0.6f, 0.28f));
                DrawTextureRect(_background, new Rect2(new Vector2(-off * 0.7f, 2f), screen.Size), false,
                    new Color(0.55f, 0.75f, 1f, 0.22f));
            }
        }
        else
            DrawRect(screen, new Color(0.08f, 0.066f, 0.05f), true);

        if (_phase == Phase.Arrival)
        {
            SittingRoomDetails.DrawClockFace(this, blank: true);
        }
        else
        {
            SittingRoomDetails.DrawChangedSet(this);
            DrawFoundRings();
        }

        DrawFireState();
        DrawDarkness();
        if (_phase == Phase.Search || _phase == Phase.Resolve || _phase == Phase.Breaking)
        {
            DrawCracks();
            DrawDust();
        }
        DrawPhasePanel();
        DrawMigraineRed(screen);

        if (_phase == Phase.Breaking)
            DrawRect(screen, new Color(0.02f, 0.01f, 0.01f, 0.45f), true);

        // Last of all, over everything: the glass itself.
        if (_phase == Phase.Search || _phase == Phase.Resolve || _phase == Phase.Breaking)
            DrawScreenCracks();
    }

    /// <summary>The screen cracking in step with the shaking: bright
    /// hairlines that creep segment by segment as the crumble runs
    /// down, each with a fainter offset twin, like light catching
    /// both edges of a split in glass.</summary>
    private void DrawScreenCracks()
    {
        float gone = 1f - Mathf.Clamp(_crumbleRemaining / CrumbleSeconds, 0f, 1f);
        if (_phase == Phase.Breaking)
            gone = 1f;
        var bright = new Color(0.93f, 0.96f, 1f, 0.12f + 0.55f * gone);
        var faint = new Color(0.93f, 0.96f, 1f, 0.05f + 0.22f * gone);
        var twin = new Vector2(1.5f, 1.5f);
        foreach (var chain in ScreenCrackChains)
        {
            int segments = Mathf.Clamp(
                Mathf.FloorToInt(gone * (chain.Length - 1) + 0.001f), 0, chain.Length - 1);
            for (int i = 0; i < segments; i++)
            {
                DrawLine(chain[i] + twin, chain[i + 1] + twin, faint, 2.2f, true);
                DrawLine(chain[i], chain[i + 1], bright, 1.1f, true);
            }
        }
    }

    /// <summary>The migraine's colour: red breathing at the edges of
    /// her vision and a red wash strobing over the whole room —
    /// hard, fast flicker while it lands, the same red returning
    /// in miniature whenever it touches the crumble's glitches.</summary>
    private void DrawMigraineRed(Rect2 screen)
    {
        bool strong = _migraineRedLeft > 0f;
        if (!strong && _glitchLeft <= 0f)
            return;

        bool on = Settings.ReduceMotionOrDefault()
            ? true
            : (Time.GetTicksMsec() / 90) % 2 == 0;
        float edgeAlpha = strong ? (on ? 0.42f : 0.18f) : (on ? 0.22f : 0.08f);
        float washAlpha = strong ? (on ? 0.12f : 0.03f) : (on ? 0.05f : 0f);

        if (washAlpha > 0.005f)
            DrawRect(screen, new Color(0.62f, 0.03f, 0.02f, washAlpha), true);
        var edge = new Color(0.62f, 0.03f, 0.02f, edgeAlpha);
        DrawRect(new Rect2(0, 0, 1152, 56), edge, true);
        DrawRect(new Rect2(0, 592, 1152, 56), edge, true);
        DrawRect(new Rect2(0, 0, 56, 648), edge, true);
        DrawRect(new Rect2(1096, 0, 56, 648), edge, true);
    }

    /// <summary>While the fire is out (or catching), the painted flames
    /// are covered with ash-dark and a few sulking embers.</summary>
    private void DrawFireState()
    {
        float outAmount = 1f - Mathf.Clamp(_lightLevel, 0f, 1f);
        if (outAmount <= 0.02f)
            return;
        DrawRect(FireRect, new Color(0.015f, 0.014f, 0.02f, 0.92f * outAmount), true);
        var ember = new Color(0.95f, 0.42f, 0.14f, 0.5f * outAmount);
        DrawCircle(FireRect.GetCenter() + new Vector2(-22, 58), 3.2f, ember);
        DrawCircle(FireRect.GetCenter() + new Vector2(8, 66), 2.4f, ember);
        DrawCircle(FireRect.GetCenter() + new Vector2(30, 52), 2.8f, ember);
    }

    private void DrawDarkness()
    {
        float level = _glitchLeft > 0f ? Mathf.Min(_lightLevel, 0.5f) : _lightLevel;
        float dark = (1f - Mathf.Clamp(level, 0f, 1f)) * 0.88f;
        if (dark <= 0.01f)
            return;
        DrawRect(new Rect2(Vector2.Zero, new Vector2(1152, 648)),
            new Color(0.002f, 0.002f, 0.006f, dark), true);
    }

    private void DrawCracks()
    {
        float gone = 1f - Mathf.Clamp(_crumbleRemaining / CrumbleSeconds, 0f, 1f);
        if (_phase == Phase.Breaking)
            gone = 1f;
        var crack = new Color(0.85f, 0.8f, 0.7f, 0.16f + 0.5f * gone);
        foreach (var chain in CrackChains)
        {
            int segments = Mathf.Clamp(
                Mathf.FloorToInt(gone * (chain.Length - 1) + 0.001f), 0, chain.Length - 1);
            for (int i = 0; i < segments; i++)
                DrawLine(chain[i], chain[i + 1], crack, 1.6f, true);
        }
    }

    private void DrawDust()
    {
        var dustColor = new Color(0.85f, 0.78f, 0.62f, 0.35f);
        foreach (var mote in _dust)
            DrawRect(new Rect2(mote.Pos, Vector2.One * mote.Size), dustColor, true);
    }

    private void DrawPhasePanel()
    {
        var panel = new Rect2(300, 8, 552, 84);
        DrawRect(panel, new Color(0.025f, 0.02f, 0.016f, 0.72f), true);
        DrawRect(panel, new Color(0.74f, 0.6f, 0.36f, 0.55f), false, 1.5f);

        if (_phase == Phase.Search || _phase == Phase.Resolve || _phase == Phase.Breaking)
        {
            // The room's remaining patience, draining red.
            float frac = Mathf.Clamp(_crumbleRemaining / CrumbleSeconds, 0f, 1f);
            var track = new Rect2(366, 73, 420, 6);
            DrawRect(track, new Color(1f, 0.92f, 0.68f, 0.14f), true);
            var fill = frac > 0.35f
                ? new Color(1f, 0.86f, 0.52f, 0.9f)
                : new Color(1f, 0.36f, 0.26f, 0.95f);
            DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * frac, track.Size.Y)),
                fill, true);

            for (int i = 0; i < Differences.Length; i++)
            {
                Vector2 center = new(552 + i * 16, 86);
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
}

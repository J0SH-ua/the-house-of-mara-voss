using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The Porch, at 4:47 — the main-path stop between the garden road and the
/// last room.
///
/// Room 6 traces the way Ida went; the road at its end used to open straight
/// onto Room 7. It bends here instead: the front of the Eldfen house at the
/// minute Mara finally arrived. Three looks (the lit window, the steps, the
/// garden path) write "The porch, at 4:47" into the Journal. The front door
/// will not take Mara inside until all three have actually been faced:
/// this time, she does not rush across the porch without looking. Crossed
/// once, the porch counts as passed, and Room 7 steps back here.
///
/// Facing the porch only earns the right to knock. The door itself is
/// the last obstacle, and it is opened the way home was always
/// announced: knock the family's knock (two, a breath, three), be
/// seen at the lit window in its steady light, then hold the handle
/// steady to a slow count of four. All the while her own head
/// interrupts — migraines that lock her hands, flickers that take
/// the light, nausea that sways the whole porch and bends her aim.
/// Failed stages cost MEMORY like everything careless does here.
/// She has been let in by that knock ten thousand times. Once more.
/// </summary>
public partial class Porch447 : MemoryRoomBase
{
    private const string SeenFlag = "porch_seen";
    private const string PassedFlag = "porch_passed";
    private const string Room7ScenePath = "res://scenes/world/room_7.tscn";

    private enum RitualStage { None, Knock, Window, Hold, Done }
    private enum SanityKind { None, Migraine, Flicker, Nausea }

    private static readonly Vector2 HandleBase = new(264, 278);
    private const float HoldSecondsNeeded = 5f;
    private const float HoldRingRadius = 46f;

    private readonly RandomNumberGenerator _rng = new();
    private RitualStage _ritual = RitualStage.None;
    private bool _failing;
    private bool _advancing;

    private int _knockCount;
    private bool _knockStarted;
    private ulong _lastKnockMsec;
    private bool _knockResetToasted;

    private bool _windowLitPhase = true;
    private float _windowTimer = 2.2f;
    private float _steadyElapsed;
    private int _windowCatches;

    private float _holdProgress;
    private int _holdSlips;
    private bool _holdWasPressed;

    private SanityKind _sanity = SanityKind.None;
    private SanityKind _lastSanity = SanityKind.None;
    private float _sanityTimer = 7f;
    private float _sanityLeft;
    private Vector2 _sway;
    private bool _sawMigraine;
    private bool _sawFlicker;
    private bool _sawNausea;

    // From the window onward the nausea keeps its own schedule and
    // can ride on top of whatever else her head is doing.
    private float _nauseaTimer = 5f;
    private float _nauseaLeft;

    private bool NauseaOn => _nauseaLeft > 0f || _sanity == SanityKind.Nausea;

    protected override string RoomName => "Porch447";
    protected override string BackgroundPath => "res://assets/images/bgs/Porch_447_BG.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Later entries win overlaps: the grounds first, the door last.
        yield return new Hotspot("GardenPath", "The Garden Path",
            new Rect2(690, 290, 440, 320), new Vector2(905, 455), Sfx.Examine, 0.75f);
        yield return new Hotspot("LitWindow", "The Lit Window",
            new Rect2(345, 100, 195, 310), new Vector2(375, 272), Sfx.WordChime, 0.92f);
        yield return new Hotspot("PorchSteps", "The Porch Steps",
            new Rect2(285, 385, 300, 185), new Vector2(435, 478), Sfx.Examine, 0.68f);
        yield return new Hotspot("FrontDoor", "The Front Door",
            new Rect2(40, 105, 300, 240), new Vector2(210, 268),
            Sfx.DoorCreak, 0.9f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        _rng.Randomize();
        _ = PlayStoryLessonOnceAsync("story_lesson_porch_447",
            "The porch. Mara has crossed it a thousand times inside the memory. She has never once stopped on it.");
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        if (MemoryVerbSystem.Active != MemoryVerb.None)
        {
            _ = NoVerbHereAsync();
            return;
        }

        switch (hotspot.Id)
        {
            case "LitWindow":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The window's lit the way it was that day. Someone home. Someone thinking the afternoon would just end like afternoons do.",
                    "Mum left lights on for anyone coming home late. This is the last one that ever waited for both her girls.",
                }, "Still lit. In my head it always will be.");
                break;

            case "PorchSteps":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Here. Right here. This is where I stood at 4:47 and called her name until calling stopped meaning come home.",
                    "The steps still hold the shape of me arriving. Nothing answered. Just the swing, finishing its swing.",
                }, "Forty-seven minutes late. The steps keep the number so I don't have to say it.");
                break;

            case "GardenPath":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The path runs down past the apple trees to the gate. From here it looks so short. That's the part the house usually hides from me. How short it was.",
                    "A child's walk. A few minutes end to end. Twelve years long, in the other direction.",
                }, "Shorter than memory allows. It always was.");
                break;

            case "FrontDoor":
                _ = EnterLastRoomAsync();
                break;
        }
    }

    private async Task NoVerbHereAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -11f, 0.92f);
            await Say("The porch does not want a verb. Things only happened here.", 4.5f, 23);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task EnterLastRoomAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            if (!AllRequiredExamined())
            {
                AudioManager.Play(Sfx.Denied, -9f, 0.9f);
                var door = GetHotspot("FrontDoor");
                if (door != null)
                    Hotspots.SpawnRipple(door.Center, new Color(0.78f, 0.72f, 0.64f));
                await Say("My hand stops on the handle. Not yet. I rushed across this porch once without looking. This time I stay until I've seen it.", 5f, 23);
                return;
            }

            var gs = GameState.Instance;
            if (gs != null && gs.HasFlag(PassedFlag))
            {
                await PassThroughDoorAsync();
                return;
            }

            await StartRitualAsync();
        }
        finally
        {
            if (_ritual == RitualStage.None && GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private async Task PassThroughDoorAsync()
    {
        var gs = GameState.Instance;
        gs?.SetFlag(SeenFlag);
        gs?.SetFlag(PassedFlag);
        await SaySequence(
            "Mara puts her hand on the door she has opened ten thousand times in her head.",
            "This time she opens it with everything she knows carried in front of her, where she can see it.");

        AudioManager.Play(Sfx.DoorCreak, -7f, 0.88f);
        await WaitSeconds(0.25f);
        SceneManager.ChangeScene(Room7ScenePath);
    }

    // ------------------------------------------------------------ ritual

    private bool RitualActive =>
        _ritual is RitualStage.Knock or RitualStage.Window or RitualStage.Hold;

    private bool SanityLocksInput =>
        _sanity is SanityKind.Migraine or SanityKind.Flicker;

    private async Task StartRitualAsync()
    {
        AudioManager.Play(Sfx.DoorCreak, -10f, 0.85f);
        await SaySequence(
            "My hand stops on the handle — and won't turn. Not locked. Worse than locked. Unannounced.",
            "Nobody in this family ever just walked in. You knocked, and Mum listened from the kitchen, and the knock said who it was before the door ever moved. Two, a breath, three. Knock like family.",
            "And my head is already starting — behind one eye, like weather coming. Fine. Let it come. It doesn't get the door too.");
        if (!GodotObject.IsInstanceValid(this))
            return;
        _ritual = RitualStage.Knock;
        _knockCount = 0;
        _knockStarted = false;
        _sanity = SanityKind.None;
        _sanityTimer = _rng.RandfRange(5f, 7f);
        _nauseaTimer = 5f;
        _nauseaLeft = 0f;
        GameState.Instance?.Toast("Click the front door to knock: knock knock — a breath — knock knock knock.");
        QueueRedraw();
    }

    private async Task AdvanceStageAsync(RitualStage next)
    {
        if (_advancing)
            return;
        _advancing = true;
        try
        {
            _ritual = RitualStage.None;
            if (next == RitualStage.Window)
            {
                AudioManager.Play(Sfx.WordChime, -10f, 0.9f);
                await Say("From inside — floorboards. Somebody home. She'd check the window first, always, light spilling round the curtain. Be seen, then. When the light is steady.", 5f, 23);
                if (!GodotObject.IsInstanceValid(this))
                    return;
                _windowLitPhase = true;
                _windowTimer = 1.9f;
                _steadyElapsed = 0f;
                _windowCatches = 0;
                _ritual = RitualStage.Window;
                GameState.Instance?.Toast("Click the lit window while its light is steady — four times seen. Not in the flicker.");
            }
            else if (next == RitualStage.Hold)
            {
                AudioManager.Play(Sfx.WordChime, -10f, 1.0f);
                await Say("Seen. Known. Now the handle — and hold it like somebody coming home, not somebody breaking in. Steady. A slow count of five.", 5f, 23);
                if (!GodotObject.IsInstanceValid(this))
                    return;
                _holdProgress = 0f;
                _holdSlips = 0;
                _holdWasPressed = false;
                _ritual = RitualStage.Hold;
                GameState.Instance?.Toast("Press and HOLD with her hand inside the ring until it fills. However the porch sways.");
            }
            else if (next == RitualStage.Done)
            {
                _ritual = RitualStage.Done;
                EndSanity();
                await PassThroughDoorAsync();
            }
            QueueRedraw();
        }
        finally
        {
            _advancing = false;
        }
    }

    private async Task FailStageAsync(string what)
    {
        if (_failing || _advancing)
            return;
        _failing = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -8f, 0.85f);
            AudioManager.Play(Sfx.LockRattle, -9f, 0.8f);
            string line = what switch
            {
                "knock" => "Wrong rhythm. From inside, the floorboards stop — a stranger's knock gets the chain, not the door. Two. A breath. Three.",
                "window" => "The curtain stills. Wrong moment — nobody waves at a flicker. Wait for the light to be steady, then be seen.",
                _ => "Her hand comes off the handle. The door forgets her halfway through remembering. Again — steady this time.",
            };
            await Say(line, 4.6f, 23);
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
                return;
            GameState.Instance?.AddCorruption(2);

            if (what == "knock") { _knockCount = 0; _knockStarted = false; }
            else if (what == "window") { _windowCatches = 0; }
            else { _holdProgress = 0f; _holdSlips = 0; }
            QueueRedraw();
        }
        finally
        {
            _failing = false;
        }
    }

    private void RegisterKnock()
    {
        ulong now = Time.GetTicksMsec();
        AudioManager.Play(Sfx.KnockWood, -4f, 0.95f + _knockCount * 0.04f);
        var door = GetHotspot("FrontDoor");
        if (door != null)
            Hotspots.SpawnRipple(door.Center, new Color(1f, 0.88f, 0.55f));

        if (!_knockStarted)
        {
            _knockStarted = true;
            _knockCount = 1;
            _lastKnockMsec = now;
            _knockResetToasted = false;
            QueueRedraw();
            return;
        }

        float gap = (now - _lastKnockMsec) / 1000f;
        _lastKnockMsec = now;
        bool breath = _knockCount == 2;
        bool ok = breath ? gap is >= 0.85f and <= 1.7f
                         : gap is >= 0.16f and <= 0.75f;
        if (!ok)
        {
            _ = FailStageAsync("knock");
            return;
        }

        _knockCount++;
        QueueRedraw();
        if (_knockCount >= 5)
            _ = AdvanceStageAsync(RitualStage.Window);
    }

    private void RegisterWindowClick()
    {
        bool seen = _windowLitPhase && _steadyElapsed >= 0.25f;
        if (seen)
        {
            _windowCatches++;
            AudioManager.Play(Sfx.WordChime, -11f, 0.9f + _windowCatches * 0.08f);
            var win = GetHotspot("LitWindow");
            if (win != null)
                Hotspots.SpawnRipple(win.Center, new Color(1f, 0.88f, 0.55f));
            QueueRedraw();
            if (_windowCatches >= 4)
                _ = AdvanceStageAsync(RitualStage.Hold);
        }
        else
        {
            _ = FailStageAsync("window");
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!RitualActive || _failing || _advancing || GetTree().Paused ||
            Dialogue.IsShowing || Dialogue.IsInputBlocked)
            return;
        if (@event is not InputEventMouseButton mouse ||
            mouse.ButtonIndex != MouseButton.Left)
            return;

        Vector2 pos = EffectiveMouse();

        if (mouse.Pressed)
        {
            GetViewport().SetInputAsHandled();
            if (SanityLocksInput)
                return;

            if (_ritual == RitualStage.Knock)
            {
                var door = GetHotspot("FrontDoor");
                if (door != null && door.Rect.Grow(10f).HasPoint(pos))
                    RegisterKnock();
            }
            else if (_ritual == RitualStage.Window)
            {
                var win = GetHotspot("LitWindow");
                if (win != null && win.Rect.Grow(10f).HasPoint(pos))
                    RegisterWindowClick();
            }
            // Hold is read continuously in _Process; a press is just
            // her hand settling on the handle.
        }
        else if (_ritual == RitualStage.Hold && _holdWasPressed && _holdProgress > 0.05f)
        {
            _holdSlips++;
            _holdWasPressed = false;
            if (_holdSlips >= 3)
                _ = FailStageAsync("hold");
            else
                GameState.Instance?.Toast("Her hand slipped off. The count starts over — hold, don't hover.");
            _holdProgress = 0f;
        }
    }

    private Vector2 EffectiveMouse()
    {
        Vector2 pos = GetGlobalMousePosition();
        if (NauseaOn)
            pos += _sway;
        return pos;
    }

    private Vector2 RingCenter()
    {
        double s = Time.GetTicksMsec() / 1000.0;
        var drift = new Vector2(
            (float)System.Math.Sin(s * 0.85) * 11f,
            (float)System.Math.Cos(s * 0.75) * 8f);
        if (NauseaOn)
            drift += _sway * 0.7f;
        return HandleBase + drift;
    }

    // ------------------------------------------------------------ sanity

    private void StartSanity()
    {
        SanityKind kind;
        if (_ritual == RitualStage.Knock)
        {
            do { kind = (SanityKind)_rng.RandiRange(1, 3); }
            while (kind == _lastSanity);
        }
        else
        {
            // Later stages: migraine and flicker alternate the main
            // track; nausea rides its own schedule underneath.
            kind = _lastSanity == SanityKind.Migraine ? SanityKind.Flicker
                : _lastSanity == SanityKind.Flicker ? SanityKind.Migraine
                : _rng.Randf() < 0.5f ? SanityKind.Migraine : SanityKind.Flicker;
        }
        _sanity = kind;
        _lastSanity = kind;
        _sanityLeft = kind switch
        {
            SanityKind.Migraine => 1.6f,
            SanityKind.Flicker => 1.1f,
            _ => 2.6f,
        };

        switch (kind)
        {
            case SanityKind.Migraine:
                AudioManager.Play(Sfx.ScreenCrack2, -12f, 0.8f);
                if (!_sawMigraine)
                {
                    _sawMigraine = true;
                    GameState.Instance?.Toast("Migraine — right behind one eye. Her hands won't answer until it passes.");
                }
                break;
            case SanityKind.Flicker:
                AudioManager.Play(Sfx.UiHover, -12f, 1.7f);
                if (!_sawFlicker)
                {
                    _sawFlicker = true;
                    GameState.Instance?.Toast("The light goes — the house blinking. Nobody knocks well in the dark.");
                }
                break;
            case SanityKind.Nausea:
                AudioManager.Play(Sfx.CorruptionWhisper, -13f, 0.85f);
                if (!_sawNausea)
                {
                    _sawNausea = true;
                    GameState.Instance?.Toast("Nausea. The porch sways, and her aim with it. Aim by the small ring, not the hand.");
                }
                break;
        }
    }

    private void EndSanity()
    {
        _sanity = SanityKind.None;
        _sanityLeft = 0f;
        _nauseaLeft = 0f;
        _sanityTimer = _rng.RandfRange(4.5f, 7.5f);
        _sway = Vector2.Zero;
        Position = Vector2.Zero;
    }

    private void UpdateSanity(float dt)
    {
        double s = Time.GetTicksMsec() / 1000.0;

        // Nausea keeps its own schedule from the window onward and
        // can ride underneath a migraine or a flicker.
        if (_ritual is RitualStage.Window or RitualStage.Hold)
        {
            if (_nauseaLeft > 0f)
                _nauseaLeft -= dt;
            else
            {
                _nauseaTimer -= dt;
                if (_nauseaTimer <= 0f)
                {
                    _nauseaLeft = 2.6f;
                    _nauseaTimer = _rng.RandfRange(5f, 8.5f);
                    AudioManager.Play(Sfx.CorruptionWhisper, -13f, 0.85f);
                    if (!_sawNausea)
                    {
                        _sawNausea = true;
                        GameState.Instance?.Toast("Nausea. The porch sways, and her aim with it. Aim by the small ring, not the hand.");
                    }
                }
            }
        }
        if (NauseaOn)
        {
            _sway = new Vector2(
                (float)System.Math.Sin(s * 0.9) * 15f,
                (float)System.Math.Cos(s * 0.7) * 9f);
        }

        if (_sanity != SanityKind.None)
        {
            _sanityLeft -= dt;
            if (_sanityLeft <= 0f)
                EndSanity();
        }
        else
        {
            _sanityTimer -= dt;
            if (_sanityTimer <= 0f)
                StartSanity();
        }

        // One place decides where the room sits this frame.
        if (_sanity == SanityKind.Migraine && !Settings.ReduceMotionOrDefault())
        {
            Position = new Vector2(
                _rng.RandfRange(-3.5f, 3.5f), _rng.RandfRange(-2.5f, 2.5f));
        }
        else if (NauseaOn)
        {
            Position = _sway * 0.55f;
        }
        else
        {
            Position = Vector2.Zero;
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        float dt = Mathf.Max((float)delta, 0f);
        if (!RitualActive)
            return;

        bool blocked = GetTree().Paused || Dialogue.IsShowing || Dialogue.IsInputBlocked ||
            (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());
        if (!blocked && !_failing && !_advancing)
        {
            UpdateSanity(dt);

            if (_ritual == RitualStage.Knock && _knockStarted &&
                Time.GetTicksMsec() - _lastKnockMsec > 2300)
            {
                _knockStarted = false;
                _knockCount = 0;
                if (!_knockResetToasted)
                {
                    _knockResetToasted = true;
                    GameState.Instance?.Toast("The rhythm's gone. Two — a breath — three.");
                }
                QueueRedraw();
            }
            else if (_ritual == RitualStage.Window)
            {
                _windowTimer -= dt;
                if (_windowLitPhase)
                {
                    _steadyElapsed += dt;
                    if (_windowTimer <= 0f)
                    {
                        _windowLitPhase = false;
                        _windowTimer = _rng.RandfRange(0.6f, 1.3f);
                    }
                }
                else if (_windowTimer <= 0f)
                {
                    _windowLitPhase = true;
                    _steadyElapsed = 0f;
                    _windowTimer = _rng.RandfRange(1.2f, 2.2f);
                }
            }
            else if (_ritual == RitualStage.Hold)
            {
                bool held = Input.IsMouseButtonPressed(MouseButton.Left) && !SanityLocksInput;
                bool inside = EffectiveMouse().DistanceTo(RingCenter()) <= HoldRingRadius;
                if (held) _holdWasPressed = true;
                if (held && inside)
                {
                    _holdProgress += dt;
                    if (_holdProgress >= HoldSecondsNeeded)
                    {
                        _holdProgress = HoldSecondsNeeded;
                        _ = AdvanceStageAsync(RitualStage.Done);
                    }
                }
                else
                {
                    _holdProgress = Mathf.Max(0f, _holdProgress - dt * 2.2f);
                }
            }
        }
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        Position = Vector2.Zero;
        base._ExitTree();
    }

    // ------------------------------------------------============ drawing

    protected override void DrawRoom()
    {
        if (RitualActive)
        {
            DrawRitualGuides();
            DrawSanityOverlays();
        }
    }

    private void DrawRitualGuides()
    {
        if (_ritual == RitualStage.Knock)
        {
            var door = GetHotspot("FrontDoor");
            if (door != null)
            {
                float pulse = Settings.ReduceMotionOrDefault()
                    ? 0.7f
                    : 0.58f + 0.2f * (float)System.Math.Sin(Time.GetTicksMsec() / 260.0);
                DrawArc(door.Center, 64f, 0f, Mathf.Tau, 48,
                    new Color(1f, 0.88f, 0.52f, pulse * 0.6f), 2.5f, true);
                for (int i = 0; i < 5; i++)
                {
                    // the breath shows as a wider gap after the second pip
                    float gap = i > 1 ? 18f : 0f;
                    Vector2 pip = door.Center + new Vector2((i - 2) * 30f + gap - 9f, 96f);
                    DrawCircle(pip, 6.5f, i < _knockCount
                        ? new Color(1f, 0.84f, 0.45f, 0.98f)
                        : new Color(0.9f, 0.84f, 0.68f, 0.3f));
                }
            }
        }
        else if (_ritual == RitualStage.Window)
        {
            var win = GetHotspot("LitWindow");
            if (win != null)
            {
                bool litNow = _sanity == SanityKind.Flicker
                    ? (Time.GetTicksMsec() / 90) % 2 == 0
                    : _windowLitPhase
                        ? true
                        : (Time.GetTicksMsec() / 90) % 2 == 0;
                if (litNow)
                {
                    DrawRect(win.Rect, new Color(1f, 0.8f, 0.42f, 0.13f), true);
                    DrawRect(win.Rect, new Color(1f, 0.86f, 0.5f, 0.55f), false, 2f);
                }
                else
                {
                    DrawRect(win.Rect, new Color(0.01f, 0.012f, 0.03f, 0.82f), true);
                }
                for (int i = 0; i < 4; i++)
                {
                    Vector2 pip = win.Center + new Vector2((i - 1.5f) * 30f, 178f);
                    DrawCircle(pip, 6.5f, i < _windowCatches
                        ? new Color(1f, 0.84f, 0.45f, 0.98f)
                        : new Color(0.9f, 0.84f, 0.68f, 0.3f));
                }
            }
        }
        else if (_ritual == RitualStage.Hold)
        {
            Vector2 center = RingCenter();
            float frac = Mathf.Clamp(_holdProgress / HoldSecondsNeeded, 0f, 1f);
            DrawArc(center, HoldRingRadius, 0f, Mathf.Tau, 48,
                new Color(0.9f, 0.85f, 0.7f, 0.35f), 2.5f, true);
            if (frac > 0.01f)
            {
                DrawArc(center, HoldRingRadius, -Mathf.Pi / 2f,
                    -Mathf.Pi / 2f + frac * Mathf.Tau, 56,
                    new Color(1f, 0.84f, 0.45f, 0.95f), 4f, true);
            }
            DrawCircle(center, 4f, new Color(0.72f, 0.55f, 0.27f, 0.98f));
        }

        if (NauseaOn)
        {
            // Where her hand actually is, while the porch disagrees.
            Vector2 aim = EffectiveMouse();
            DrawArc(aim, 9f, 0f, Mathf.Tau, 24,
                new Color(1f, 0.9f, 0.66f, 0.8f), 1.8f, true);
            DrawCircle(aim, 1.8f, new Color(1f, 0.9f, 0.66f, 0.9f));
        }
    }

    private void DrawSanityOverlays()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        if (_sanity == SanityKind.Migraine)
        {
            float pulse = Settings.ReduceMotionOrDefault()
                ? 0.3f
                : 0.3f + 0.14f * (float)System.Math.Sin(Time.GetTicksMsec() / 110.0);
            var red = new Color(0.55f, 0.05f, 0.04f, pulse);
            DrawRect(new Rect2(0, 0, 1152, 54), red, true);
            DrawRect(new Rect2(0, 594, 1152, 54), red, true);
            DrawRect(new Rect2(0, 0, 54, 648), red, true);
            DrawRect(new Rect2(1098, 0, 54, 648), red, true);
        }
        else if (_sanity == SanityKind.Flicker)
        {
            bool off = (Time.GetTicksMsec() / 90) % 2 != 0;
            if (off)
                DrawRect(screen, new Color(0.001f, 0.001f, 0.004f, 0.9f), true);
        }
    }
}

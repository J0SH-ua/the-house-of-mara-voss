using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// A short first-person journey between two completed rooms.
///
/// The camera visibly travels: straight down one memory hallway, left at
/// the bent runner, right past the photographs, or along the empty road
/// until the lit porch is close enough to name. No score or ambience
/// follows her between rooms — only carpeted boards indoors or the dirt
/// road underfoot, and the ordinary sound of her dialogue. The walks are long enough
/// that Mara sometimes thinks aloud on the way. On every first arrival
/// she stops with the newly recovered fragment in her hand and tries to
/// remember what it could be for. Only sometimes does that inspection
/// pull her briefly into the room it came from as a migraine she has to
/// shake back before the door can be opened. The destination
/// is not loaded until the player clicks the door (or the distant porch
/// light). Each wrapper scene stores its own destination and journey id, so
/// saving/quitting mid-journey resumes in the right place.
///
/// Room 4 keeps its authored brass-door close-up and Room 5 keeps its
/// wardrobe/garden passage; this script covers the plainer stretches of
/// house and road between Rooms 1-2, 2-3, 3-4, and 6-Porch.
/// </summary>
public partial class RoomJourney : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    private const string HandTexturePath = "res://assets/images/core/hand_palm.png";
    private const float HeldPalmX = 0.51f;
    private const float HeldPalmY = 0.36f;
    private const float HeldHandAspect = 1.332f;
    private const float RecoverShakeSeconds = 0.55f;
    private const float MigraineChance = 0.65f;
    private const float FirstWalkingMonologueChance = 0.7f;

    private static readonly string[] FragmentIconPaths =
    [
        "res://assets/images/core/shards/brass_key.png",
        "res://assets/images/core/shards/photo.png",
        "res://assets/images/core/shards/fragment_jar.png",
        "res://assets/images/core/shards/fragment_frame.png",
        "res://assets/images/core/shards/fragment_letter.png",
        "res://assets/images/core/shards/fragment_apple.png",
    ];

    [Export(PropertyHint.File, "*.tscn")]
    public string DestinationScenePath { get; set; } = "res://scenes/world/room_1.tscn";

    [Export]
    public string JourneyId { get; set; } = "Room1ToRoom2";

    /// <summary>A walk SceneManager has asked this scene to perform
    /// instead of its exported one — how the same journey scenes are
    /// walked backwards. Set immediately before the scene change,
    /// consumed (and cleared) in _Ready.</summary>
    public static string PendingJourneyId;
    public static string PendingDestination;

    private sealed class JourneySpec
    {
        public string TexturePath { get; init; }
        public string[] Lines { get; init; }
        public string[] RandomWalkingLines { get; init; } = [];
        public Vector2 StartFocus { get; init; } = new(576f, 324f);
        public Vector2 Focus { get; init; }
        public float FinalScale { get; init; }
        public Rect2 TargetRect { get; init; }
        public string Prompt { get; init; }
        public float CameraSeconds { get; init; }
        public string[] FootstepSfx { get; init; } = [];
        public float FootstepVolumeDb { get; init; } = -12f;
        public float FootstepIntervalSeconds { get; init; } = 0.84f;
        public int FragmentIndex { get; init; } = -1;
        public string MemoryTexturePath { get; init; } = string.Empty;
        public string[] FragmentLines { get; init; } = [];
        public string[] CalmFragmentLines { get; init; } = [];
        public string[] RecoveryLines { get; init; } = [];
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
                "Okay. Kitchen's done. Whatever 'done' means here.",
                "A key in a drawer. Toast I can still actually taste. I keep waiting for it to add up to something and it just... sits there, being a morning.",
                "Next room smells of ash and paper already. The sitting room. Two chairs. I remember hating how quiet it could get in there.",
                "Right. One foot, then the other. That's the whole plan. It's worked so far.",
            ],
            RandomWalkingLines =
            [
                "My hand still smells faintly of toast. Ridiculous detail for a mind to keep.",
                "One foot, then the other. That was always the trick, wasn't it?",
                "The boards know my weight. They don't bother creaking like I'm a stranger.",
                "Did I leave a cup somewhere back there? I don't even know what day this is. Doesn't matter. Keep walking.",
                "If this is a dream, my subconscious has really committed to the carpet.",
            ],
            FragmentLines =
            [
                "The key's still warm in my hand. What are you for, now? A door I already opened shouldn't need carrying.",
                "Morning. Toast. Her voice from the next room—",
            ],
            CalmFragmentLines =
            [
                "No answer. Fine. You can be a mystery in my bag like everything else.",
            ],
            RecoveryLines =
            [
                "No. Stay with me. Breathe. The door first.",
            ],
            FragmentIndex = 0,
            MemoryTexturePath = "res://assets/images/bgs/Room_1_BG.png",
            Focus = new Vector2(580f, 318f),
            FinalScale = 1.7f,
            TargetRect = new Rect2(372f, 12f, 410f, 620f),
            Prompt = "Click the door to open it",
            CameraSeconds = 7.8f,
            FootstepSfx = [Sfx.FootstepCarpetA, Sfx.FootstepCarpetB],
            FootstepVolumeDb = -13f,
            FootstepIntervalSeconds = 0.84f,
        },
        ["Room2ToRoom3"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Left.png",
            Lines =
            [
                "That photograph. I keep putting her face back the way it was in my head, and the house keeps handing it to me turned a quarter away. It's rude, is what it is.",
                "Left at the runner. My feet keep knowing the way before I do. I can't decide whether to trust that or be annoyed about it.",
                "It's her room next, isn't it. The one at the end. Nobody's said so — the corridor just got that feeling, like the air's holding its breath politely.",
                "Don't run. Running is how this corner used to win.",
            ],
            RandomWalkingLines =
            [
                "Paper. Ash. Someone breathing in another room. Then none of it.",
                "I can still turn left without looking. My feet remembered before I did.",
                "Don't run. Running is how this corner used to win.",
                "My hands feel too big. No — too small. It changes as I walk. I hate that it changes.",
                "Quiet house. Even the quiet sounds like somebody being quiet on purpose.",
            ],
            FragmentLines =
            [
                "Her face keeps catching the light. What am I meant to do with you? Look until looking turns into staying?",
                "Two chairs. A page turning. Don't—",
            ],
            CalmFragmentLines =
            [
                "I can hold your face without falling into it. That has to count for something.",
            ],
            RecoveryLines =
            [
                "Not backwards. I can carry her without crawling inside the photograph.",
            ],
            FragmentIndex = 1,
            MemoryTexturePath = "res://assets/images/bgs/Room2_no_objects.png",
            StartFocus = new Vector2(690f, 330f),
            Focus = new Vector2(398f, 240f),
            FinalScale = 1.75f,
            TargetRect = new Rect2(450f, 80f, 250f, 490f),
            Prompt = "Click the door to open it",
            CameraSeconds = 8.7f,
            FootstepSfx = [Sfx.FootstepCarpetA, Sfx.FootstepCarpetB],
            FootstepVolumeDb = -13f,
            FootstepIntervalSeconds = 0.84f,
        },
        ["Room3ToRoom4"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Right.png",
            Lines =
            [
                "Spiders with names. Jars in a row. A whole room preserved like — no. Like it was all still being waited for. That's the word. Waited for.",
                "I keep turning the pieces over in my bag like they'll explain themselves if I line them up right. They haven't. Honestly, some cooperation would be nice.",
                "There's a door at the end of this with plates on it. I remember the plates. I don't remember being allowed anywhere near them with my hands full of jam.",
                "Okay. Right at the photographs. Even facing away, they know my steps.",
            ],
            RandomWalkingLines =
            [
                "I should stop apologising to furniture.",
                "Names in pencil. Small brave names.",
                "Her laugh could find me through two closed doors. Some days it still can.",
                "I should stop narrating. She'd have told me to stop narrating. 'Mara. It's a corridor.'",
                "Halfway somewhere. That's the most accurate address I've got.",
            ],
            FragmentLines =
            [
                "The little square of name tape keeps finding my fingers in the bag. Names. Pieces. Why does this house keep putting that day in my hands?",
                "A windowsill. Pencil letters. Her laugh—",
            ],
            CalmFragmentLines =
            [
                "Pieces. Fine. If the house wants pieces carried, I'll carry them awake.",
            ],
            RecoveryLines =
            [
                "Enough. Shake it off, Mara. The next room isn't waiting inside this one.",
            ],
            FragmentIndex = 2,
            MemoryTexturePath = "res://assets/images/bgs/Room_3_BG.png",
            StartFocus = new Vector2(460f, 330f),
            Focus = new Vector2(773f, 255f),
            FinalScale = 1.75f,
            TargetRect = new Rect2(430f, 80f, 290f, 490f),
            Prompt = "Click the door to open it",
            CameraSeconds = 8.7f,
            FootstepSfx = [Sfx.FootstepCarpetA, Sfx.FootstepCarpetB],
            FootstepVolumeDb = -13f,
            FootstepIntervalSeconds = 0.84f,
        },
        ["Room6ToPorch"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Empty_Road.png",
            Lines =
            [
                "Gate. Road. Nobody counting swings now.",
                "It is shorter than I made it in my head. Twelve years I made this road longer and longer and it's... just a road. That almost makes me angry.",
                "The porch light's on. Who leaves a light on in a memory? Mum. Mum does. Of course she does.",
                "Okay. Come on, feet. Nearly kept a promise's worth of road.",
            ],
            RandomWalkingLines =
            [
                "My shoes sound too loud for an empty road.",
                "I keep expecting my own voice calling from the gate.",
                "October air. Apples gone soft. Don't make a poem out of it, Mara. Walk.",
                "Gravel in my shoe. Even here. Of course there's gravel in my shoe.",
                "If I wave, nobody waves back. I checked. Obviously I checked.",
            ],
            FragmentLines =
            [
                "The apple bruises where my thumb holds it. Is every piece meant to be carried back to you?",
                "The swing. The gate. Four o'—",
            ],
            CalmFragmentLines =
            [
                "No pull this time. Just the road behind me, and you coming with me.",
            ],
            RecoveryLines =
            [
                "I know. I know. Walk anyway. She isn't on this road now; I am.",
            ],
            FragmentIndex = 5,
            MemoryTexturePath = "res://assets/images/bgs/Room_6_Final.png",
            Focus = new Vector2(635f, 292f),
            FinalScale = 2.45f,
            TargetRect = new Rect2(490f, 215f, 220f, 155f),
            Prompt = "Click the porch light to arrive",
            CameraSeconds = 12.0f,
            FootstepSfx = [Sfx.FootstepDirtA, Sfx.FootstepDirtB],
            FootstepVolumeDb = -11f,
            FootstepIntervalSeconds = 0.92f,
            OpenSfx = Sfx.DoorCreak,
            OpenPitch = 0.82f,
        },
        // ------------------------------------------------------
        // The same legs, walked backwards. No fragment rides these
        // — going back is ordinary walking, done at her own pace,
        // under her own held-down feet, the way it should always
        // have been instead of the house blinking her room to room.
        ["Room2ToRoom1"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Door.png",
            Lines =
            [
                "Back the way I came. The hall doesn't mind. It never did keep count of my comings and goings.",
            ],
            Focus = new Vector2(580f, 318f),
            FinalScale = 1.7f,
            TargetRect = new Rect2(372f, 12f, 410f, 620f),
            Prompt = "Click the door to open it",
            CameraSeconds = 6.2f,
            FootstepSfx = [Sfx.FootstepCarpetA, Sfx.FootstepCarpetB],
            FootstepVolumeDb = -13f,
            FootstepIntervalSeconds = 0.84f,
        },
        ["Room3ToRoom2"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Left.png",
            Lines =
            [
                "Right at the runner, this time. The sitting room keeps its smell of ash and paper even from out here.",
            ],
            StartFocus = new Vector2(690f, 330f),
            Focus = new Vector2(398f, 240f),
            FinalScale = 1.75f,
            TargetRect = new Rect2(450f, 80f, 250f, 490f),
            Prompt = "Click the door to open it",
            CameraSeconds = 6.2f,
            FootstepSfx = [Sfx.FootstepCarpetA, Sfx.FootstepCarpetB],
            FootstepVolumeDb = -13f,
            FootstepIntervalSeconds = 0.84f,
        },
        ["Room4ToRoom3"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Hallway_Right.png",
            Lines =
            [
                "Left at the photographs. Back to the room of jars and brave little names.",
            ],
            StartFocus = new Vector2(460f, 330f),
            Focus = new Vector2(773f, 255f),
            FinalScale = 1.75f,
            TargetRect = new Rect2(430f, 80f, 290f, 490f),
            Prompt = "Click the door to open it",
            CameraSeconds = 6.2f,
            FootstepSfx = [Sfx.FootstepCarpetA, Sfx.FootstepCarpetB],
            FootstepVolumeDb = -13f,
            FootstepIntervalSeconds = 0.84f,
        },
        ["PorchToRoom6"] = new JourneySpec
        {
            TexturePath = "res://assets/images/bgs/Journey_Empty_Road.png",
            Lines =
            [
                "Back up the road. Shorter every time I let myself know it.",
            ],
            Focus = new Vector2(635f, 292f),
            FinalScale = 2.45f,
            TargetRect = new Rect2(490f, 215f, 220f, 155f),
            Prompt = "Click the house to go back in",
            CameraSeconds = 7.5f,
            FootstepSfx = [Sfx.FootstepDirtA, Sfx.FootstepDirtB],
            FootstepVolumeDb = -11f,
            FootstepIntervalSeconds = 0.92f,
            OpenSfx = Sfx.DoorCreak,
            OpenPitch = 0.82f,
        },
    };

    private JourneySpec _spec;
    private Texture2D _texture;
    private Font _font;
    private Label _promptLabel;
    private TextureRect _heldHandRect;
    private TextureRect _heldFragmentRect;
    private Texture2D _memoryTexture;
    private Tween _heldTween;
    private Vector2 _handRestPosition;
    private Vector2 _fragmentRestPosition;

    private bool _readyToOpen;
    private bool _opening;
    private bool _hovering;
    private bool _hoverCursorOwned;
    private bool _reduceMotion;
    private bool _seenBefore;
    private readonly RandomNumberGenerator _rng = new();
    private bool _migrainePlanned;
    private bool _walkingMonologuePlanned;
    private float _cameraSeconds = 3.4f;
    private float _elapsed;
    private float _progress;
    private float _nextFootstepElapsed = 0.5f;
    private int _footstepIndex;
    private bool _heldSettled;
    private bool _migraineActive;
    private float _migraineTime;
    private float _recoverShakeTime;
    private float _recoverShakeElapsed;

    private string SeenFlag => $"journey_seen_{JourneyId}";

    /// <summary>Legs travelled toward rooms Mara has already left
    /// behind. Walking back is always wordless.</summary>
    private bool IsReturnJourney => JourneyId is
        "Room2ToRoom1" or "Room3ToRoom2" or "Room4ToRoom3" or "PorchToRoom6";

    /// <summary>Roll a first-journey beat once and keep the result in the
    /// save, so quitting and resuming midway cannot reroll whether this
    /// threshold gets a walking thought or a migraine.</summary>
    private bool SavedRandomDecision(string kind, float chance)
    {
        var gs = GameState.Instance;
        if (gs == null)
            return _rng.Randf() < chance;

        string rolledFlag = $"journey_random_{kind}_rolled_{JourneyId}";
        string yesFlag = $"journey_random_{kind}_{JourneyId}";
        if (!gs.HasFlag(rolledFlag))
        {
            gs.SetFlag(rolledFlag, save: false);
            if (_rng.Randf() < chance)
                gs.SetFlag(yesFlag, save: false);
            gs.SaveGame($"journey random roll: {JourneyId} {kind}");
        }
        return gs.HasFlag(yesFlag);
    }

    /// <summary>Some first crossings get a full little monologue —
    /// the authored route lines plus one thought chosen from this
    /// stretch's pool — and some keep only their first threshold
    /// sentence. Every later crossing is silent, and every walk
    /// back toward an older room is silent from its first step:
    /// spoken journey lines belong only to going somewhere new.</summary>
    private List<string> BuildWalkingLines()
    {
        if (_seenBefore || IsReturnJourney)
            return new List<string>();

        var lines = new List<string>(_spec.Lines ?? []);
        if (!_walkingMonologuePlanned)
            return lines.Count > 0 ? [lines[0]] : lines;

        if (_spec.RandomWalkingLines is { Length: > 0 })
        {
            string extra = _spec.RandomWalkingLines[
                _rng.RandiRange(0, _spec.RandomWalkingLines.Length - 1)];
            int insertAt = lines.Count > 1 ? lines.Count - 1 : lines.Count;
            lines.Insert(insertAt, extra);
        }
        return lines;
    }

    public override void _Ready()
    {
        SetProcess(true);
        SetProcessInput(true);

        // A backwards walk arrives wearing a forward journey's
        // scene; the pending request is what makes it backwards.
        if (!string.IsNullOrWhiteSpace(PendingJourneyId))
        {
            JourneyId = PendingJourneyId;
            if (!string.IsNullOrWhiteSpace(PendingDestination))
                DestinationScenePath = PendingDestination;
            PendingJourneyId = null;
            PendingDestination = null;
        }

        if (!Specs.TryGetValue(JourneyId ?? string.Empty, out _spec))
        {
            Logger.Warning($"[RoomJourney] Unknown journey id '{JourneyId}'; using Room1ToRoom2 staging.");
            _spec = Specs["Room1ToRoom2"];
        }

        _font = GD.Load<FontFile>(FontPath);
        _texture = GD.Load<Texture2D>(_spec.TexturePath);
        if (_texture == null)
            Logger.Error($"[RoomJourney] Could not load journey background: {_spec.TexturePath}");
        if (!string.IsNullOrWhiteSpace(_spec.MemoryTexturePath))
        {
            _memoryTexture = GD.Load<Texture2D>(_spec.MemoryTexturePath);
            if (_memoryTexture == null)
                Logger.Warning($"[RoomJourney] Could not load memory flicker: {_spec.MemoryTexturePath}");
        }

        _rng.Randomize();
        _seenBefore = GameState.Instance?.HasFlag(SeenFlag) ?? false;
        _reduceMotion = Settings.ReduceMotionOrDefault();
        _cameraSeconds = _seenBefore ? Math.Min(4.2f, _spec.CameraSeconds) : _spec.CameraSeconds;
        _progress = _reduceMotion ? 1f : 0f;
        _migrainePlanned = !_seenBefore && !IsReturnJourney &&
            SavedRandomDecision("migraine", MigraineChance);
        _walkingMonologuePlanned = !_seenBefore && !IsReturnJourney &&
            SavedRandomDecision("walk_monologue", FirstWalkingMonologueChance);

        BuildHeldFragment();
        BuildPrompt();
        QueueRedraw();
        _ = RunJourneyAsync();
    }

    public override void _ExitTree()
    {
        _heldTween?.Kill();
        ClearHover();
    }

    public override void _Process(double delta)
    {
        if (!GetTree().Paused)
        {
            if (_migraineActive)
            {
                _migraineTime += (float)delta;
                UpdateHeldTremor();
                QueueRedraw();
            }
            if (_recoverShakeTime > 0f)
            {
                _recoverShakeElapsed += (float)delta;
                _recoverShakeTime = Math.Max(0f, _recoverShakeTime - (float)delta);
                QueueRedraw();
            }
        }

        bool walking = false;
        if (!_reduceMotion && !GetTree().Paused && _progress < 1f)
        {
            // Her feet are hers now: the walk only moves while W is
            // held — camera, footsteps, all of it. Let go and she
            // stops where she stands, mid-hall, mid-thought, her
            // lines still said over the quiet.
            walking = Input.IsPhysicalKeyPressed(Key.W) &&
                !Dialogue.IsInputBlocked &&
                !(GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());
            if (walking)
            {
                _elapsed += (float)delta;
                _progress = Mathf.Clamp(_elapsed / Math.Max(_cameraSeconds, 0.01f), 0f, 1f);
                UpdateFootsteps();
            }
            QueueRedraw();
        }

        if (_promptLabel != null && !_readyToOpen && !_opening)
        {
            const string walkText = "Hold W to walk";
            if (_promptLabel.Text != walkText)
                _promptLabel.Text = walkText;
            _promptLabel.Visible = _progress < 1f && !_reduceMotion;
            float target = walking ? 0.4f : 0.95f;
            var m = _promptLabel.Modulate;
            m.A = Mathf.MoveToward(m.A, target, (float)delta * 2.2f);
            _promptLabel.Modulate = m;
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
        Vector2 focus = _spec.StartFocus.Lerp(_spec.Focus, eased);

        // The migraine is a camera event as much as a visual overlay:
        // small, involuntary source-space jolts while the memory has her,
        // then one deliberate, decaying left-right head shake as she
        // comes back to herself. Reduce Motion keeps only a trace.
        if (_migraineActive)
        {
            float amp = _reduceMotion ? 1.4f : 6f;
            focus += new Vector2(
                (float)Math.Sin(_migraineTime * 71f) * amp,
                (float)Math.Cos(_migraineTime * 59f) * amp * 0.65f);
        }
        else if (_recoverShakeTime > 0f)
        {
            float strength = Mathf.Clamp(_recoverShakeTime / RecoverShakeSeconds, 0f, 1f);
            float amp = (_reduceMotion ? 2f : 11f) * strength;
            focus += new Vector2(
                (float)Math.Sin(_recoverShakeElapsed * 34f) * amp,
                (float)Math.Cos(_recoverShakeElapsed * 27f) * amp * 0.3f);
        }

        // A barely-there walking bob, gone once Mara arrives (or for
        // Reduce Motion). It is in source pixels, so it stays subtle.
        if (!_reduceMotion && _progress < 1f)
            focus.Y += (float)Math.Sin(_elapsed * 3.3f) * 1.6f * (1f - _progress);

        Vector2 sourcePos = new(
            Mathf.Clamp(focus.X - sourceSize.X * 0.5f, 0f, 1152f - sourceSize.X),
            Mathf.Clamp(focus.Y - sourceSize.Y * 0.5f, 0f, 648f - sourceSize.Y));
        var sourceRect = new Rect2(sourcePos, sourceSize);
        DrawTextureRectRegion(_texture, screen, sourceRect, Colors.White);
        DrawMemoryEffects(screen, sourceRect);

        if (_readyToOpen && !_opening)
        {
            float pulse = _reduceMotion
                ? 0.72f
                : 0.62f + 0.22f * (float)Math.Sin(Time.GetTicksMsec() / 210.0);
            DrawRect(_spec.TargetRect, new Color(1f, 0.89f, 0.58f, _hovering ? 0.10f : 0.05f));
            DrawRect(_spec.TargetRect, new Color(1f, 0.89f, 0.58f, _hovering ? 0.95f : pulse), false, 2f);
        }
    }

    /// <summary>Footfalls are the journey's environmental soundscape. Their cadence
    /// follows the camera's smoothstep speed, and alternating samples keep
    /// the carpet runner (or dirt road) from sounding like a metronome.</summary>
    private void UpdateFootsteps()
    {
        if (_progress >= 0.995f || _spec?.FootstepSfx is not { Length: > 0 } ||
            _elapsed < _nextFootstepElapsed)
            return;

        string sfx = _spec.FootstepSfx[_footstepIndex++ % _spec.FootstepSfx.Length];
        float volume = _spec.FootstepVolumeDb
            + Mathf.Lerp(-1.1f, 0.35f, _progress)
            + _rng.RandfRange(-0.55f, 0.35f);
        AudioManager.PlayVaried(sfx, volume, _rng.RandfRange(0.94f, 1.05f), 0.025f);

        // Smoothstep's derivative peaks mid-journey: steps gather a little
        // there and spread out again as Mara reaches the threshold.
        float speed01 = Mathf.Clamp(6f * _progress * (1f - _progress) / 1.5f, 0f, 1f);
        float interval = _spec.FootstepIntervalSeconds
            * Mathf.Lerp(1.1f, 0.88f, speed01)
            * _rng.RandfRange(0.94f, 1.07f);
        _nextFootstepElapsed = _elapsed + interval;
    }

    private async Task RunJourneyAsync()
    {
        try
        {
            await WaitSeconds(0.45f);
            if (!StillHere())
                return;

            List<string> lines = BuildWalkingLines();
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

            await PlayFragmentPauseAsync();
            if (!StillHere())
                return;

            _readyToOpen = true;
            if (_promptLabel != null)
            {
                _promptLabel.Text = _spec.Prompt;
                _promptLabel.Modulate = new Color(1, 1, 1, 1);
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

    /// <summary>Draw the brief, involuntary return of the room Mara has
    /// just left: red/cyan separation, hard flickers of that room over the
    /// hallway, a closing red edge, and jagged migraine aura at the top
    /// and bottom of sight. Everything is derived from _migraineTime, so
    /// the effect can run underneath click-to-advance dialogue without a
    /// second timed sequence.</summary>
    private void DrawMemoryEffects(Rect2 screen, Rect2 sourceRect)
    {
        if (_migraineActive)
        {
            float t = _migraineTime;
            float pulse = 0.5f + 0.5f * (float)Math.Sin(t * 8.5f);
            float effectScale = _reduceMotion ? 0.45f : 1f;

            if (!_reduceMotion)
            {
                var redGhost = screen;
                redGhost.Position += new Vector2(5f, 0f);
                DrawTextureRectRegion(_texture, redGhost, sourceRect,
                    new Color(1f, 0.25f, 0.25f, 0.16f));
                var cyanGhost = screen;
                cyanGhost.Position += new Vector2(-5f, 0f);
                DrawTextureRectRegion(_texture, cyanGhost, sourceRect,
                    new Color(0.35f, 0.85f, 1f, 0.13f));
            }

            if (_memoryTexture != null)
            {
                float gate = Math.Abs((float)Math.Sin(t * 10.7f)
                    + 0.45f * (float)Math.Sin(t * 23.1f));
                float alpha = gate > 1.18f ? 0.68f : gate > 0.95f ? 0.32f : 0.05f;
                DrawTextureRect(_memoryTexture, screen, false,
                    new Color(1f, 1f, 1f, alpha * effectScale));
            }

            DrawRect(screen, new Color(0.32f, 0f, 0.04f, (0.07f + 0.04f * pulse) * effectScale));
            float edge = (30f + 16f * pulse) * effectScale;
            var edgeColor = new Color(0.18f, 0f, 0.025f, (0.34f + 0.16f * pulse) * effectScale);
            DrawRect(new Rect2(0f, 0f, 1152f, edge), edgeColor);
            DrawRect(new Rect2(0f, 648f - edge, 1152f, edge), edgeColor);
            DrawRect(new Rect2(0f, 0f, edge, 648f), edgeColor);
            DrawRect(new Rect2(1152f - edge, 0f, edge, 648f), edgeColor);

            var aura = new Color(1f, 0.72f, 0.62f, (0.28f + 0.18f * pulse) * effectScale);
            for (int i = 0; i < 5; i++)
            {
                float x = 130f + i * 220f;
                float topBend = (float)Math.Sin(t * 17f + i * 1.7f) * 22f;
                float topEnd = (float)Math.Cos(t * 13f + i) * 30f;
                DrawLine(new Vector2(x, 0f), new Vector2(x + topBend, 38f), aura, 2f, true);
                DrawLine(new Vector2(x + topBend, 38f), new Vector2(x + topEnd, 74f), aura, 2f, true);

                float bottomBend = (float)Math.Cos(t * 15f + i * 2.1f) * 22f;
                float bottomEnd = (float)Math.Sin(t * 12f + i) * 30f;
                DrawLine(new Vector2(x, 648f), new Vector2(x + bottomBend, 610f), aura, 2f, true);
                DrawLine(new Vector2(x + bottomBend, 610f), new Vector2(x + bottomEnd, 574f), aura, 2f, true);
            }
            return;
        }

        if (_recoverShakeTime > 0f)
        {
            float strength = Mathf.Clamp(_recoverShakeTime / RecoverShakeSeconds, 0f, 1f);
            DrawRect(screen, new Color(0.25f, 0f, 0.03f, 0.10f * strength));
        }
    }

    /// <summary>The guaranteed pre-open pause. Mara always takes the
    /// just-recovered fragment in her palm and wonders what carrying it can
    /// possibly be for. The random part is only whether that inspection
    /// turns into a migraine — the room it came from cutting through in
    /// flashes, followed by a hard little head shake. On a calm threshold
    /// she simply steadies the fragment and lowers her hand. First arrivals
    /// only; shortened repeat journeys stay shortened.</summary>
    private async Task PlayFragmentPauseAsync()
    {
        if (_seenBefore || IsReturnJourney || _spec.FragmentIndex < 0 ||
            _spec.FragmentIndex >= FragmentIconPaths.Length ||
            _spec.FragmentLines == null || _spec.FragmentLines.Length == 0)
        {
            return;
        }

        var gs = GameState.Instance;
        if (gs != null && !gs.IsShardRecovered(_spec.FragmentIndex))
            return;

        await ShowHeldFragmentAsync();
        if (!StillHere())
            return;

        await Say(_spec.FragmentLines[0], 5.2f, 23);
        if (!StillHere())
            return;

        if (_migrainePlanned && _spec.FragmentLines.Length > 1)
        {
            try
            {
                _migraineTime = 0f;
                _migraineActive = true;
                AudioManager.Play(Sfx.CorruptionGlitch, -8f, 0.82f);
                AudioManager.Play(Sfx.CorruptionWhisper, -13f, 0.75f);
                QueueRedraw();

                for (int i = 1; i < _spec.FragmentLines.Length; i++)
                {
                    await Say(_spec.FragmentLines[i], 5.2f, 23);
                    if (!StillHere())
                        return;
                }
            }
            finally
            {
                _migraineActive = false;
                ResetHeldPositions();
            }

            _recoverShakeElapsed = 0f;
            _recoverShakeTime = RecoverShakeSeconds;
            AudioManager.Play(Sfx.ClarifyReveal, -10f, 0.85f);
            QueueRedraw();
        }

        string[] closingLines = _migrainePlanned
            ? _spec.RecoveryLines
            : _spec.CalmFragmentLines;
        if (closingLines != null)
        {
            foreach (string line in closingLines)
            {
                await Say(line, 5.2f, 23);
                if (!StillHere())
                    return;
            }
        }

        await HideHeldFragmentAsync();
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

        string destination = DestinationScenePath;
        // The empty road crosses water, and the memory left that
        // crossing broken. Until her father's tools have mended it,
        // the road to the porch ends at the bridge.
        if (JourneyId == "Room6ToPorch" &&
            !(GameState.Instance?.HasFlag("bridge_fixed") ?? false))
            destination = "res://scenes/world/bridge_repair.tscn";
        SceneManager.ChangeScene(destination);
    }

    private async Task ShowHeldFragmentAsync()
    {
        if (_heldHandRect == null || _heldFragmentRect == null)
            return;

        _heldSettled = false;
        _heldHandRect.Position = _handRestPosition + new Vector2(0f, 190f);
        _heldFragmentRect.Position = _fragmentRestPosition + new Vector2(0f, 190f);
        _heldHandRect.Modulate = new Color(1, 1, 1, 0);
        _heldFragmentRect.Modulate = new Color(1, 1, 1, 0);
        _heldHandRect.Visible = true;
        _heldFragmentRect.Visible = true;
        AudioManager.Play(Sfx.PhotoPickup, -13f, 0.9f);

        if (_reduceMotion)
        {
            ResetHeldPositions();
            _heldHandRect.Modulate = Colors.White;
            _heldFragmentRect.Modulate = Colors.White;
            _heldSettled = true;
            return;
        }

        _heldTween?.Kill();
        _heldTween = CreateTween();
        _heldTween.SetParallel(true);
        _heldTween.TweenProperty(_heldHandRect, "position", _handRestPosition, 0.65f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _heldTween.TweenProperty(_heldFragmentRect, "position", _fragmentRestPosition, 0.65f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _heldTween.TweenProperty(_heldHandRect, "modulate:a", 1f, 0.4f);
        _heldTween.TweenProperty(_heldFragmentRect, "modulate:a", 1f, 0.4f);
        await WaitSeconds(0.7f);
        _heldSettled = StillHere();
    }

    private async Task HideHeldFragmentAsync()
    {
        if (_heldHandRect == null || _heldFragmentRect == null)
            return;

        _heldSettled = false;
        AudioManager.Play(Sfx.ShardPlace, -13f, 0.9f);
        if (_reduceMotion)
        {
            _heldHandRect.Visible = false;
            _heldFragmentRect.Visible = false;
            return;
        }

        _heldTween?.Kill();
        _heldTween = CreateTween();
        _heldTween.SetParallel(true);
        _heldTween.TweenProperty(_heldHandRect, "position",
                _handRestPosition + new Vector2(0f, 190f), 0.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _heldTween.TweenProperty(_heldFragmentRect, "position",
                _fragmentRestPosition + new Vector2(0f, 190f), 0.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _heldTween.TweenProperty(_heldHandRect, "modulate:a", 0f, 0.45f);
        _heldTween.TweenProperty(_heldFragmentRect, "modulate:a", 0f, 0.45f);
        await WaitSeconds(0.5f);
        if (!StillHere())
            return;
        _heldHandRect.Visible = false;
        _heldFragmentRect.Visible = false;
    }

    private void ResetHeldPositions()
    {
        if (_heldHandRect == null || _heldFragmentRect == null)
            return;
        _heldHandRect.Position = _handRestPosition;
        _heldFragmentRect.Position = _fragmentRestPosition;
    }

    private void UpdateHeldTremor()
    {
        if (!_heldSettled || _heldHandRect == null || _heldFragmentRect == null)
            return;
        Vector2 tremor = _reduceMotion
            ? Vector2.Zero
            : new Vector2(
                (float)Math.Sin(_migraineTime * 43f) * 2.2f,
                (float)Math.Cos(_migraineTime * 37f) * 1.6f);
        _heldHandRect.Position = _handRestPosition + tremor;
        _heldFragmentRect.Position = _fragmentRestPosition + tremor;
    }

    private void BuildHeldFragment()
    {
        if (_spec.FragmentIndex < 0 || _spec.FragmentIndex >= FragmentIconPaths.Length)
            return;

        var handTexture = GD.Load<Texture2D>(HandTexturePath);
        var fragmentTexture = GD.Load<Texture2D>(FragmentIconPaths[_spec.FragmentIndex]);
        if (handTexture == null || fragmentTexture == null)
        {
            Logger.Warning("[RoomJourney] Held-fragment art could not be loaded; monologue only.");
            return;
        }

        float handW = 370f;
        float handH = handW * HeldHandAspect;
        _handRestPosition = new Vector2(576f - handW / 2f, 648f - handH * 0.92f);
        Vector2 palm = _handRestPosition + new Vector2(handW * HeldPalmX, handH * HeldPalmY);
        _fragmentRestPosition = palm - new Vector2(150f, 150f);

        _heldHandRect = new TextureRect
        {
            Name = "JourneyHeldHand",
            Texture = handTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(handW, handH),
            Position = _handRestPosition + new Vector2(0f, 190f),
            Visible = false,
            Modulate = new Color(1, 1, 1, 0),
            ZIndex = 2450,
        };
        AddChild(_heldHandRect);

        _heldFragmentRect = new TextureRect
        {
            Name = "JourneyHeldFragment",
            Texture = fragmentTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(300f, 300f),
            PivotOffset = new Vector2(150f, 150f),
            Scale = new Vector2(0.55f, 0.55f),
            Position = _fragmentRestPosition + new Vector2(0f, 190f),
            Visible = false,
            Modulate = new Color(1, 1, 1, 0),
            ZIndex = 2460,
        };
        AddChild(_heldFragmentRect);
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
        // The prompt is navigation, not scenery: it rides its own
        // canvas layer just above the blackout's (layer 1), so the
        // dark hallway journeys of the flashlight trek keep their
        // "Hold W to walk" readable — HUD and dialogue already
        // stay lit in the black by the same rule.
        var promptLayer = new CanvasLayer { Name = "JourneyPrompt", Layer = 2 };
        AddChild(promptLayer);
        promptLayer.AddChild(_promptLabel);
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

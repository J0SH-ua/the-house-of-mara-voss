using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The whole overlay layer of the game (autoload, CanvasLayer 90, Always):
///  * HUD — current objective, clarity pips, memory-integrity meter, shards.
///  * Corruption screen overlay (shader) driven by GameState corruption.
///  * Pause menu, Settings panel, Memory Journal (fragments + echoes), toasts.
///  * Global input: pause/journal/hint/clarity keys, held-Tab hotspot reveal,
///    and dialogue advance forwarding (Dialogue lives in a SubViewport whose
///    _Input is not guaranteed to receive root input, so we forward clicks).
/// Everything is built in code so no .tscn surgery was needed. Context-aware:
/// HUD only inside the GameManager scene, a lone Settings button on the main
/// menu, nothing over the intro cinematic.
/// </summary>
public partial class GameUI : CanvasLayer
{
    public static GameUI Instance { get; private set; }

    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string CorruptionShaderPath = "res://assets/shaders/corruption.gdshader";
    private const string MainMenuScenePath = "res://scenes/ui/main_menu.tscn";

    private Font _font;

    // HUD
    private Control _hud;
    private Label _objectiveLabel;
    private Label _integrityLabel;
    private ProgressBar _integrityBar;
    private HBoxContainer _clarityPips;
    private Label _shardLabel;

    // Bag / inventory (global replacement for the old per-room inventory sprites)
    private Button _bagButton;
    private Control _bagPanel;
    private GridContainer _bagGrid;
    private Label _bagSummary;
    private Label _bagDetailTitle;
    private Label _bagDetailStatus;
    private RichTextLabel _bagDetailBody;
    private Label _bagVerbsLabel;
    private int _bagSelectedIndex = -1;
    private bool _openedBagFromPause;

    private static readonly string[] BagShortNames =
    [
        "Brass Key",
        "Photograph",
        "Preserved Names",
        "Hallway Sentence",
        "Unsent Apology",
        "Last Afternoon",
    ];
    private static readonly string[] BagIconPaths =
    [
        "res://assets/images/core/shards/brass_key.png",
        "res://assets/images/core/shards/photo.png",
        "res://assets/images/core/shards/fragment_jar.png",
        "res://assets/images/core/shards/fragment_frame.png",
        "res://assets/images/core/shards/fragment_letter.png",
        "res://assets/images/core/shards/fragment_apple.png",
    ];
    private Label _toastLabel;
    private PanelContainer _toastPanel;
    private Tween _toastTween;
    private double _toastUntilMsec;

    // Back navigation (puzzle -> room, room -> previous room)
    private Button _backButton;

    // Forward navigation ("go back to the new room") + Recollection offer
    private Button _forwardButton;
    private string _forwardTargetPath = string.Empty;
    private string _forwardCacheKey = string.Empty;
    private Button _rememberButton;
    private bool _recollectionOfferedThisArming;

    // Memory Verb bar (Echo / Anchor / Trace / Release)
    private PanelContainer _verbPanel;
    private readonly System.Collections.Generic.Dictionary<MemoryVerb, Button> _verbButtons = new();

    // Authored endings (Waking / Kept House)
    private Control _endingRoot;
    private TextureRect _endingImage;
    private ColorRect _endingFade;
    private Label _endingTitle;
    private Label _endingCaption;
    private Label _endingStats;
    private Button _endingReturnButton;
    private Tween _endingTween;
    private bool _endingPlaying;
    private bool _endingAdvanceRequested;
    private bool _endingSkipRequested;
    private string _endingId = string.Empty;
    private string _backTargetPath = string.Empty;
    private string _navSceneCachePath = string.Empty;

    // Post-Play memory cutscene
    private Control _cutsceneRoot;
    private TextureRect _cutsceneImage;

    // Bad ending (memory shatter) overlay
    private Control _shatterRoot;
    private ColorRect _shatterBlack;
    private Label _shatterCaption;
    private Label _shatterTitle;
    private HBoxContainer _shatterChoices;
    private bool _shatterPlaying;
    private bool _shatterAdvanceRequested;
    private ShatterCrackOverlay _shatterCracks;

    // Recollection choice (custom panel in the game's own voice)
    private Control _recollectionPanel;
    private Label _recollectionBody;
    private Label _recollectionCost;
    private Label _recollectionHeldLabel;
    private Label _recollectionNextLabel;
    private HBoxContainer _recollectionBeforePips;
    private HBoxContainer _recollectionAfterPips;
    private Button _recollectionConfirmButton;
    private Button _recollectionCancelButton;
    private Action _recollectionConfirmAction;
    private bool _recollectionVisualPlaying;
    private Label _cutsceneCaption;
    private Label _cutsceneHint;
    private Label _cutsceneContinueLabel;
    private Button _cutsceneSkipButton;
    private Tween _cutsceneTween;
    private bool _cutscenePlaying;
    private bool _cutsceneAdvanceRequested;
    private bool _cutsceneSkipRequested;
    private bool _cutsceneClickOnly;
    private bool _cutsceneWaitingForContinue;
    private TaskCompletionSource<bool> _cutsceneContinueSource;
    private (string ImagePath, string Caption, float HoldSeconds)[] _activeShots;
    private Action _cutsceneOnComplete;
    private ColorRect _cutsceneBackdrop;
    private ColorRect _cutsceneBand;
    private ColorRect _cutsceneWash;
    private bool _cutsceneFadeFromWhite;

    // Mara's own voice in the HUD: brief asides she mutters to herself while
    // looking at things. Non-blocking by design — she talks while the
    // player plays.
    private Label _maraAsideLabel;
    private Tween _maraAsideTween;
    private int _lastExaminesPerformed = -1;
    private float _asideCooldown;
    private readonly List<string> _asideBag = new();
    private string _asideBagRoom = string.Empty;
    private readonly Random _asideRandom = new();
    private bool _roomArrivalPlaying;
    private bool _roomFlashPlaying;

    // One short first-person threshold line the first time Mara enters a
    // room. The kitchen keeps its bespoke bedroom-door reaction and the
    // bedroom keeps its waking sequence; puzzle close-ups for Rooms 1-2
    // are included because entering a lock is its own little held breath.
    private static readonly Dictionary<string, (string RoomKey, string[] Lines)> RoomEntryMonologuesByScene = new()
    {
        ["room_1_puzzle.tscn"] = ("Room1Puzzle",
        [
            "A lock. Of course. The house won't give me a morning until I say it back in numbers.",
        ]),
        ["room_2.tscn"] = ("Room2",
        [
            "The sitting room. Dad's paper-and-ash smell. Two chairs, both empty. I used to fall asleep here just because someone else was breathing in the room.",
        ]),
        ["room_2_puzzle.tscn"] = ("Room2Puzzle",
        [
            "Letters this time. Fine. The house has never liked saying a thing only once.",
        ]),
        ["room_3.tscn"] = ("Room3",
        [
            "Her room. Don't touch the doorframe. Don't fix anything. Just… go in properly this time.",
        ]),
        ["room_4.tscn"] = ("Room4",
        [
            "The hall. Every photo turned a little away, like they're listening to something behind me. Mum's voice used to carry down here.",
        ]),
        ["flashback_landing.tscn"] = ("Room4Flashback",
        [
            "This light. I know this light. This is the evening nobody in my family says out loud.",
        ]),
        ["room_5.tscn"] = ("Room5",
        [
            "My room. My rooms. One still has kid sheets and one has twelve years of letters. Great. That's normal.",
        ]),
        ["apartment_now.tscn"] = ("ApartmentNow",
        [
            "My door. My building. My actual life, waiting where I left it. It looks less forgiving in daylight.",
        ]),
        ["room_6.tscn"] = ("Room6",
        [
            "The garden. Right. I can stand here. I can stand here and not count.",
        ]),
        ["porch_447.tscn"] = ("Porch447",
        [
            "The porch. I have come through that gate calling her name so many times. Not this time. Just walk.",
        ]),
        ["room_7.tscn"] = ("Room7",
        [
            "The last room. No more doors after this one. Whatever I decide here, I have to be the one who decides it.",
        ]),
    };

    // The kitchen is never eligible: exactly one of the later main rooms is
    // chosen per run for Mara black-and-white room flash.
    private static readonly string[] RandomRoomFlashRooms = ["Room2", "Room3", "Room4", "Room5", "Room6"];
    private const string RandomRoomFlashSeenFlag = "random_room_flash_seen";

    // ------------------------------------------------------- ambient life
    //
    // The rooms are paintings; these are the small motions that keep a
    // painting from being a photograph — steam curling, a fire breathing,
    // rain crossing two windows, leaves down and a branch swaying in the
    // garden, dust turning in the light. Three frames each (ping-ponged),
    // placed per scene where that room would actually have them, always
    // under the interface, and held perfectly still by Reduce Motion.

    private sealed class AmbientSpec
    {
        public string Effect;   // steam / flame / leaves / rain / dust / branch / glow
        public float Cx, Cy;    // centre, as fractions of the room
        public float W, H;      // px at 1152x648; 0 = follow the other by aspect
        public float Fps = 3f;
        public float Alpha = 0.85f;
        public float Phase;
        public Color Tint = Colors.White;
        public float SwayDegrees, SwaySpeed = 0.5f, PivotX = 0.5f;
        public float Pulse;     // glows breathe by this much alpha
        public float PulseSpeed = 1.35f;
        public float OrbitRx, OrbitRy;  // moths: flutter ellipse, px at 1152x648
        public string CupNodePath;      // steam only while this cup layer is the one showing
    }

    private static readonly Dictionary<string, AmbientSpec[]> AmbientByScene = new()
    {
        ["bedroom_prologue.tscn"] =
        [
            // morning light in the childhood window, and the dust in it
            new() { Effect = "glow", Cx = 0.395f, Cy = 0.36f, W = 240f, H = 300f, Alpha = 0.10f, Tint = new Color(1f, 0.85f, 0.55f), Pulse = 0.03f, PulseSpeed = 0.5f },
            new() { Effect = "dust", Cx = 0.42f, Cy = 0.42f, W = 380f, Fps = 2f, Alpha = 0.5f },
        ],
        ["room_1.tscn"] =
        [
            // steam off the two cups — only while each cup's first layer is
            // the one showing: turn it over, empty it, or clarify it, and
            // the steam is gone, exactly as the tile art itself insists
            new() { Effect = "steam", Cx = 0.534f, Cy = 0.636f, H = 86f, Fps = 5f, Alpha = 0.8f, CupNodePath = "Objects/CupExclude/Examine1" },
            new() { Effect = "steam", Cx = 0.777f, Cy = 0.636f, H = 86f, Fps = 5f, Alpha = 0.8f, Phase = 1.3f, CupNodePath = "Objects/Cup2/Examine1" },
        ],
        ["room_2.tscn"] =
        [
            // the sitting-room fire, lit and breathing
            new() { Effect = "glow", Cx = 0.148f, Cy = 0.67f, W = 260f, H = 260f, Alpha = 0.17f, Tint = new Color(1f, 0.69f, 0.36f), Pulse = 0.05f, PulseSpeed = 2.3f },
            new() { Effect = "flame", Cx = 0.148f, Cy = 0.71f, W = 58f, H = 140f, Fps = 7f, Alpha = 0.9f },
        ],
        ["room_3.tscn"] =
        [
            new() { Effect = "dust", Cx = 0.70f, Cy = 0.42f, W = 420f, Fps = 2f, Alpha = 0.6f },
        ],
        ["room_4.tscn"] =
        [
            new() { Effect = "dust", Cx = 0.5f, Cy = 0.45f, W = 480f, Fps = 2f, Alpha = 0.38f },
        ],
        ["flashback_landing.tscn"] =
        [
            new() { Effect = "dust", Cx = 0.5f, Cy = 0.45f, W = 480f, Fps = 2f, Alpha = 0.32f, Tint = new Color(0.78f, 0.84f, 1f) },
        ],
        ["room_5.tscn"] =
        [
            // rain crossing the window; the lamp's light, breathing
            new() { Effect = "rain", Cx = 0.145f, Cy = 0.33f, W = 250f, H = 380f, Fps = 6f, Alpha = 0.55f, Tint = new Color(0.75f, 0.83f, 0.93f) },
            new() { Effect = "glow", Cx = 0.485f, Cy = 0.47f, W = 300f, H = 300f, Alpha = 0.15f, Tint = new Color(1f, 0.77f, 0.43f), Pulse = 0.05f, PulseSpeed = 0.5f },
            // three small moths, worrying at the only still light
            new() { Effect = "moth", Cx = 0.485f, Cy = 0.465f, H = 17f, Fps = 10f, Alpha = 0.9f, Tint = new Color(1f, 0.95f, 0.84f), OrbitRx = 48f, OrbitRy = 34f },
            new() { Effect = "moth", Cx = 0.485f, Cy = 0.465f, H = 14f, Fps = 10f, Alpha = 0.8f, Tint = new Color(1f, 0.95f, 0.84f), Phase = 2.2f, OrbitRx = 60f, OrbitRy = 40f },
            new() { Effect = "moth", Cx = 0.485f, Cy = 0.465f, H = 12f, Fps = 10f, Alpha = 0.7f, Tint = new Color(1f, 0.95f, 0.84f), Phase = 4.1f, OrbitRx = 36f, OrbitRy = 26f },
        ],
        ["apartment_now.tscn"] =
        [
            new() { Effect = "rain", Cx = 0.145f, Cy = 0.30f, W = 260f, H = 330f, Fps = 6f, Alpha = 0.5f, Tint = new Color(0.75f, 0.83f, 0.93f) },
        ],
        ["room_6.tscn"] =
        [
            // leaves coming down past both trees; a near branch swaying
            new() { Effect = "leaves", Cx = 0.24f, Cy = 0.42f, W = 110f, Fps = 3f, Alpha = 0.95f, SwayDegrees = 0.7f, SwaySpeed = 0.6f },
            new() { Effect = "leaves", Cx = 0.50f, Cy = 0.36f, W = 90f, Fps = 3f, Alpha = 0.9f, Phase = 1.7f, SwayDegrees = 0.7f, SwaySpeed = 0.5f },
            new() { Effect = "branch", Cx = 0.835f, Cy = 0.155f, W = 400f, Alpha = 1f, SwayDegrees = 1.1f, SwaySpeed = 0.45f, PivotX = 0.85f },
        ],
        ["porch_447.tscn"] =
        [
            // chimney smoke (the kettle's wisp, greyer); the lit panes;
            // and Room 5's moths, come down to bother the porch lantern
            new() { Effect = "steam", Cx = 0.10f, Cy = 0.02f, H = 120f, Fps = 4f, Alpha = 0.5f, Tint = new Color(0.8f, 0.82f, 0.85f) },
            new() { Effect = "glow", Cx = 0.326f, Cy = 0.42f, W = 170f, H = 170f, Alpha = 0.12f, Tint = new Color(1f, 0.77f, 0.43f), Pulse = 0.035f, PulseSpeed = 0.6f },
            new() { Effect = "glow", Cx = 0.402f, Cy = 0.39f, W = 150f, H = 150f, Alpha = 0.16f, Tint = new Color(1f, 0.77f, 0.43f), Pulse = 0.05f, PulseSpeed = 0.8f },
            new() { Effect = "moth", Cx = 0.402f, Cy = 0.39f, H = 15f, Fps = 10f, Alpha = 0.9f, Tint = new Color(1f, 0.95f, 0.84f), OrbitRx = 40f, OrbitRy = 28f },
            new() { Effect = "moth", Cx = 0.402f, Cy = 0.39f, H = 13f, Fps = 10f, Alpha = 0.8f, Tint = new Color(1f, 0.95f, 0.84f), Phase = 2.4f, OrbitRx = 52f, OrbitRy = 34f },
            new() { Effect = "moth", Cx = 0.402f, Cy = 0.39f, H = 11f, Fps = 10f, Alpha = 0.7f, Tint = new Color(1f, 0.95f, 0.84f), Phase = 4.4f, OrbitRx = 30f, OrbitRy = 22f },
        ],
        ["room_7.tscn"] =
        [
            new() { Effect = "dust", Cx = 0.42f, Cy = 0.40f, W = 420f, Fps = 2f, Alpha = 0.25f },
        ],
    };

    private const int AmbientPoolSize = 6;
    private CanvasLayer _ambientLayer;
    private TextureRect[] _ambientRects;
    private AmbientSpec[] _ambientActive = System.Array.Empty<AmbientSpec>();
    private string _ambientSceneFile = string.Empty;
    private float _ambientTime;
    private readonly Dictionary<string, Texture2D[]> _ambientFrameCache = new();
    private static readonly int[] AmbientPingPong = [0, 1, 2, 1];

    // ------------------------------------------------- held fragment beat
    //
    // Every fragment, once it's hers, is held up in first person for a
    // breath — the thing itself, big; one line of Mara; then click, and
    // it flies to the bag. Getting a fragment should feel like getting it.

    private static readonly string[] HeldFragmentLines =
    [
        "Every morning started with her already talking. ... I'd give anything to be woken up like that one more time.",
        "She could sleep anywhere, as long as someone was in the room. I used to turn the pages quietly. I remember turning them quietly.",
        "She named the spiders so she wouldn't be afraid of them. Me, I kept the jars. That's what I did instead.",
        "\"She would have loved to see how far you've come.\" Mum said it in this hall, years too late. ... I finally heard her.",
        "Every October I write it. Every October I don't send it. It's still mine to carry. Just carried different, now.",
        "She waited by the trees until waiting became walking. The swing was still moving when I remembered. ... I know the rest now.",
    ];

    private Control _heldRoot;
    private TextureRect _heldGlow;
    private TextureRect _heldIcon;
    private Label _heldName;
    private Label _heldLine;
    private Label _heldHint;
    private Tween _heldTween;
    private readonly bool[] _heldSeen = new bool[6];
    private bool _heldBaselineSynced;
    private int _heldPending = -1;
    private bool _heldShowing;
    private float _heldAge;
    private TextureRect _heldHand;
    private int _heldPhase;    // 0 = resting in her hand, 1 = held up to the light
    private bool _heldBusy;    // showing, or still flying to the bag

    // Where a fragment rests in the palm art, and the art's shape.
    private const float HeldPalmX = 0.51f;
    private const float HeldPalmY = 0.36f;
    private const float HeldHandAspect = 1.332f;

    private static readonly string[] GenericAsideLines =
    [
        "Okay. Just look at everything. Properly this time.",
        "I know this. I know I know this.",
        "Stop shaking. It's only a house.",
        "Don't think. Look. Looking's safe. Probably.",
        "Everything here's mine. That's the worst part.",
        "Breathe. In. Out. Again.",
    ];

    private static readonly string[] Room1AsideLines =
    [
        "Morning. It's always morning in here.",
        "Toast. Coffee. Like nothing happened. Like she just—",
        "That clock. I keep not looking at that clock.",
    ];

    private static readonly string[] Room2AsideLines =
    [
        "Dad's study. Still smells like paper.",
        "She read to me in here. I half-listened. I always half-listened.",
        "Don't touch the phone. ...Why did I say that out loud?",
    ];

    private static readonly string[] Room3AsideLines =
    [
        "Her room. Okay. Okay, I can do her room.",
        "The jars. God, the names. She named every single one.",
        "Don't straighten anything. Just look.",
    ];

    private static readonly string[] Room4AsideLines =
    [
        "Mum's voice is in this wall somewhere. In pieces.",
        "Eleven seconds. I can still hear socks on the boards.",
        "Later. She wrote 'later'. Imagine that.",
    ];

    private static readonly string[] Room5AsideLines =
    [
        "My room. Both my rooms.",
        "Twelve letters. Say it plain: twelve years of sorry, never sent.",
        "That's my writing. That's definitely my writing.",
    ];

    private static readonly string[] Room6AsideLines =
    [
        "The garden. Right. The garden.",
        "She waited right here. Right here, and I—",
        "Don't count. Whatever you do, don't count the swings.",
    ];

    private static readonly string[] Room7AsideLines =
    [
        "Six pieces. My whole life in six pieces.",
        "They lived in an order. I was there. I should know it.",
    ];

    // Overlay
    private ColorRect _corruptionRect;
    private ShaderMaterial _corruptionMaterial;

    // Panels
    private Control _pausePanel;
    private Control _settingsPanel;
    private Control _journalPanel;
    private Control _menuSettingsButton;
    private Control _confirmPanel;
    private Label _confirmTextLabel;
    private Button _confirmOkButton;
    private Button _confirmCancelButton;
    private Action _confirmAction;
    private Action _confirmCancelAction;

    // Clarity-collapse countdown overlay (house taking the room back)
    private Control _collapseRoot;
    private ColorRect _collapseTint;
    private ShatterCrackOverlay _collapseCracks;
    private Label _collapseTimerLabel;
    private bool _collapseUiActive;

    // Journal widgets
    private VBoxContainer _journalList;
    private RichTextLabel _journalBody;
    private Label _journalStats;
    private int _journalTab; // 0 fragments · 1 echoes · 2 notes & clues
    private VBoxContainer _notesBox;
    private TextEdit _notesEdit;
    private bool _notesDirty;

    // Settings widgets (refreshed on open)
    private readonly List<(HSlider slider, Action<float> apply)> _settingsRefreshers = new();

    private bool _hotspotsHeld;
    private double _contextCheckTimer;
    private enum UiContext { Unknown, Intro, Menu, Game }
    private UiContext _context = UiContext.Unknown;

    // =============================================================== lifecycle

    public override void _Ready()
    {
        Instance = this;
        Layer = 90;
        ProcessMode = ProcessModeEnum.Always;
        _font = GD.Load<FontFile>(FontPath);

        BuildAmbientLife();
        BuildCorruptionOverlay();
        BuildCollapseOverlay();
        BuildHud();
        BuildBackButton();
        BuildForwardButton();
        BuildBagButton();
        BuildRememberButton();
        BuildMemoryVerbBar();
        BuildFragmentHeld();
        BuildPausePanel();
        BuildSettingsPanel();
        BuildJournalPanel();
        BuildBagPanel();
        BuildMenuButton();
        BuildTutorialPanel();
        BuildCutscene();
        BuildEndingOverlay();
        BuildRecollectionPanel();
        BuildShatterOverlay();
        BuildConfirmPanel();
        BuildMaraAside();

        if (GameState.Instance != null)
        {
            var gs = GameState.Instance;
            gs.CorruptionChanged += (_, _) => RefreshHud();
            gs.ClarityChanged += _ => RefreshHud();
            gs.ShardsChanged += RefreshHud;
            gs.ShardsChanged += () => { if (_bagPanel?.Visible ?? false) RefreshBag(); };
            gs.ObjectiveChanged += RefreshHud;
            gs.VerbsChanged += RefreshMemoryVerbBar;
            gs.VerbsChanged += () => { if (_bagPanel?.Visible ?? false) RefreshBag(); };
            gs.StoryFlagsChanged += RefreshHud;
            gs.ToastRequested += ShowToast;
            gs.MemoryShattered += OnMemoryShattered;
            gs.CollapseStarted += OnCollapseStarted;
            gs.CollapseTicked += OnCollapseTicked;
            gs.CollapseEnded += OnCollapseEnded;
            gs.Saved += () => ShowToast("Memory preserved.", quiet: true);
        }
        RoomExamineTracker.ExamineRecorded += (_, _) =>
        {
            RefreshHud();
            Hotspots.RefreshPersistent(); // a faced recollection ring goes dark
        };
        SceneManager.SceneChanged += scenePath =>
        {
            MemoryVerbSystem.Cancel(silent: true);
            Hotspots.HideAll();
            if (_bagPanel != null)
                _bagPanel.Visible = false;
            _openedBagFromPause = false;
            ClaritySystem.RefreshToggleTiles();
            RefreshHud();
            MaybePlayCutsceneThenTutorial(scenePath);
            CloseRecollectionPanel();
            Hotspots.ApplyRecollectionVisuals();
            Hotspots.RefreshPersistent();
            _ = PlayRoomArrivalAsync(scenePath);
        };
        if (Settings.Instance != null)
            Settings.Instance.Changed += RefreshCorruptionShader;

        RefreshHud();
        RefreshCorruptionShader();
        UpdateContext(force: true);
        Logger.Info("GameUI ready.");
    }

    public override void _Process(double delta)
    {
        _contextCheckTimer -= delta;
        if (_contextCheckTimer <= 0)
        {
            _contextCheckTimer = 0.3;
            UpdateContext();
        }

        RefreshBackButton();
        RefreshForwardButton();
        RefreshRememberButton();
        RefreshMemoryVerbBar();
        UpdateCollapsePulse();
        UpdateMaraAsides(delta);
        UpdateAmbientLife(delta);
        UpdateFragmentHeld(delta);
        UpdateCutsceneContinuePrompt(delta);

        // "Remember it all": offered once per Clarity arming — but retried
        // every frame until the moment is actually free (no dialogue, no
        // panel), so arming Clarity a breath too early can no longer lose
        // the offer for the whole room. The HUD button offers it any time.
        bool armedNow = ClaritySystem.IsArmed;
        if (!armedNow)
            _recollectionOfferedThisArming = false;
        else if (!_recollectionOfferedThisArming)
            MaybeOfferRecollection();

        // Corruption overlay follows the live value, easing toward it.
        if (_corruptionMaterial != null && _corruptionRect.Visible)
        {
            float current = (float)_corruptionMaterial.GetShaderParameter("intensity").AsDouble();
            float target = _shatterPlaying ? 1f
                : _collapseUiActive ? CollapseShaderTarget()
                : TargetCorruptionIntensity();
            float rate = _shatterPlaying ? 1.2f : _collapseUiActive ? 0.55f : 0.25f;
            float next = Mathf.MoveToward(current, target, (float)delta * rate);
            if (Mathf.Abs(next - current) > 0.0005f)
                _corruptionMaterial.SetShaderParameter("intensity", next);
        }

        // Held-Tab hotspot reveal.
        bool wantHotspots = _context == UiContext.Game
                            && !GetTree().Paused
                            && !Dialogue.IsShowing
                            && Settings.HotspotHintsOrDefault()
                            && Input.IsActionPressed(InputSetup.RevealHotspots)
                            && !FocusIsTextEditing();
        if (!_roomFlashPlaying && wantHotspots && !_hotspotsHeld)
            Hotspots.RevealAll();
        else if (!_roomFlashPlaying && !wantHotspots && _hotspotsHeld)
            Hotspots.HideAll();
        if (!_roomFlashPlaying)
            _hotspotsHeld = wantHotspots;
        Hotspots.ValidateScene();

        if (_toastLabel.Visible && Time.GetTicksMsec() > (ulong)_toastUntilMsec)
            FadeToastOut();
    }

    private float TargetCorruptionIntensity()
    {
        if (_context != UiContext.Game || GameState.Instance == null)
            return 0f;

        float v = GameState.Instance.Corruption01 * Settings.CorruptionIntensityOrDefault();
        if (Settings.ReduceMotionOrDefault())
            v *= 0.45f;
        return Mathf.Clamp(v, 0f, 1f);
    }

    /// <summary>During the collapse the shader itself is the takeover: it
    /// starts half-drowned and closes to full corruption as the count ends,
    /// whatever the underlying corruption value says.</summary>
    private float CollapseShaderTarget()
    {
        float left = GameState.Instance?.CollapseSecondsLeft ?? GameState.CollapseGraceSeconds;
        float progress = 1f - Mathf.Clamp(left / GameState.CollapseGraceSeconds, 0f, 1f);
        float v = 0.5f + progress * 0.5f;
        if (Settings.ReduceMotionOrDefault())
            v *= 0.6f;
        return Mathf.Clamp(v * Settings.CorruptionIntensityOrDefault(), 0f, 1f);
    }

    /// <summary>True while any modal surface owns the screen. GameState asks
    /// this so the collapse count never drains behind a menu.</summary>
    public bool IsBlockingOverlayOpen() => AnyPanelOpen();

    private void UpdateCollapsePulse()
    {
        if (!_collapseUiActive || GameState.Instance == null)
            return;

        float left = GameState.Instance.CollapseSecondsLeft;
        float progress = 1f - Mathf.Clamp(left / GameState.CollapseGraceSeconds, 0f, 1f);

        // Cracks spread with the count; the red drown pulses faster late.
        _collapseCracks?.SetFracture(0.08f + progress * 0.84f);
        double period = progress > 0.66f ? 130.0 : 230.0;
        float pulse = Settings.ReduceMotionOrDefault()
            ? 0.5f
            : 0.5f + 0.5f * (float)Math.Sin(Time.GetTicksMsec() / period);
        if (_collapseTint != null)
            _collapseTint.Color = new Color(0.38f, 0.02f, 0.05f, 0.10f + progress * 0.17f + pulse * 0.05f);
    }

    // =================================================================== input

    public override void _Input(InputEvent @event)
    {
        // The held-fragment moment takes the click for itself: admire,
        // then click, and it goes in the bag.
        if (_heldShowing && !GetTree().Paused)
        {
            bool advance = (@event is InputEventMouseButton heldClick
                    && heldClick.Pressed && heldClick.ButtonIndex == MouseButton.Left)
                || @event.IsActionPressed(InputSetup.AdvanceDialogue);
            if (advance)
            {
                if (_heldAge > 0.45f)
                {
                    if (_heldPhase == 0)
                        AdvanceHeldToShowcase();
                    else
                        DismissFragmentHeld();
                }
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // Forward dialogue advance/skip (guaranteed path; see Dialogue docs).
        if (Dialogue.IsShowing)
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left
                && !AnyPanelOpen() && !GetTree().Paused)
                Dialogue.NotifyAdvancePressed();
            else if (@event.IsActionPressed(InputSetup.AdvanceDialogue) && !GetTree().Paused)
                Dialogue.NotifyAdvancePressed();
            else if (@event.IsActionPressed(InputSetup.SkipDialogue) && !GetTree().Paused)
                Dialogue.NotifySkipPressed();
        }

        // Right-click on the Recollection choice means "not all of it" — it
        // closes the choice while leaving Clarity armed for one object.
        if ((_recollectionPanel?.Visible ?? false)
            && @event is InputEventMouseButton recRight && recRight.Pressed
            && recRight.ButtonIndex == MouseButton.Right)
        {
            CloseRecollectionPanel();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_context == UiContext.Game && MemoryVerbSystem.Active != MemoryVerb.None
            && @event is InputEventMouseButton verbRight && verbRight.Pressed
            && verbRight.ButtonIndex == MouseButton.Right)
        {
            MemoryVerbSystem.Cancel();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Right-click releases Clarity from anywhere (ExamineHandler also does
        // this over tiles; this covers clicks over empty space/UI).
        if (_context == UiContext.Game && ClaritySystem.IsArmed
            && @event is InputEventMouseButton rmb && rmb.Pressed && rmb.ButtonIndex == MouseButton.Right)
        {
            ClaritySystem.Disarm();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_context != UiContext.Game)
            return;

        if (_recollectionPanel?.Visible ?? false)
        {
            if (@event.IsActionPressed(InputSetup.PauseGame))
            {
                CloseRecollectionPanel();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_endingPlaying)
        {
            if (@event.IsActionPressed(InputSetup.AdvanceDialogue)
                || (@event is InputEventMouseButton emb && emb.Pressed && emb.ButtonIndex == MouseButton.Left))
            {
                _endingAdvanceRequested = true;
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionPressed(InputSetup.PauseGame) || @event.IsActionPressed(InputSetup.SkipDialogue))
            {
                _endingSkipRequested = true;
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_cutscenePlaying)
        {
            if (@event.IsActionPressed(InputSetup.AdvanceDialogue))
            {
                RequestCutsceneAdvance();
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionPressed(InputSetup.PauseGame) || @event.IsActionPressed(InputSetup.SkipDialogue))
            {
                RequestCutsceneSkip();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_shatterPlaying)
        {
            if (@event.IsActionPressed(InputSetup.AdvanceDialogue)
                || (@event is InputEventMouseButton smb && smb.Pressed && smb.ButtonIndex == MouseButton.Left))
            {
                _shatterAdvanceRequested = true;
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (@event.IsActionPressed(InputSetup.ToggleBag) && !FocusIsTextEditing())
        {
            ToggleBag();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(InputSetup.PauseGame))
        {
            HandleEscape();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (GetTree().Paused)
            return;

        // A click nothing claimed: the softest acknowledgement, so the mouse
        // never feels dead. Claimed clicks (examines, doors, triggers) call
        // SetInputAsHandled and never reach _UnhandledInput.
        if (@event is InputEventMouseButton emptyClick && emptyClick.Pressed &&
            emptyClick.ButtonIndex == MouseButton.Left && !FocusIsTextEditing() && !AnyPanelOpen())
        {
            AudioManager.Play(Sfx.UiClick, -17f, 0.9f);
            return;
        }

        if (@event.IsActionPressed(InputSetup.OpenJournal) && !FocusIsTextEditing())
        {
            OpenJournal();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.ToggleClarity) && !FocusIsTextEditing())
        {
            if (!Dialogue.IsShowing)
                ClaritySystem.Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.MemoryEcho) && !FocusIsTextEditing())
        {
            if (!Dialogue.IsShowing)
                MemoryVerbSystem.Toggle(MemoryVerb.Echo);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.MemoryAnchor) && !FocusIsTextEditing())
        {
            if (!Dialogue.IsShowing)
                MemoryVerbSystem.Toggle(MemoryVerb.Anchor);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.MemoryTrace) && !FocusIsTextEditing())
        {
            if (!Dialogue.IsShowing)
                MemoryVerbSystem.Toggle(MemoryVerb.Trace);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.MemoryRelease) && !FocusIsTextEditing())
        {
            if (!Dialogue.IsShowing)
                MemoryVerbSystem.Toggle(MemoryVerb.Release);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.RequestHint) && !FocusIsTextEditing())
        {
            RequestHint();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.NavBack) && !FocusIsTextEditing())
        {
            GoBack();
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleEscape()
    {
        if (_confirmPanel != null && _confirmPanel.Visible)
        {
            var cancel = _confirmCancelAction;
            CloseConfirm();
            cancel?.Invoke();
            return;
        }
        if (_tutorialPanel.Visible)
        {
            CloseTutorial();
            return;
        }
        if (_bagPanel.Visible)
        {
            CloseBag(backToPause: _openedBagFromPause);
            return;
        }
        if (_settingsPanel.Visible)
        {
            CloseSettings(backToPause: _pausePanel.Visible);
            return;
        }
        if (_journalPanel.Visible)
        {
            CloseJournal(backToPause: false);
            if (_openedJournalFromPause)
            {
                _pausePanel.Visible = true;
                _openedJournalFromPause = false;
            }
            return;
        }
        if (MemoryVerbSystem.Active != MemoryVerb.None && !GetTree().Paused)
        {
            MemoryVerbSystem.Cancel();
            return;
        }
        if (ClaritySystem.IsArmed && !GetTree().Paused)
        {
            // First Esc releases Clarity; it takes a second Esc to pause.
            ClaritySystem.Disarm();
            return;
        }

        if (GetTree().Paused)
            ResumeGame();
        else
            PauseGame();
    }

    private bool _openedJournalFromPause;
    private bool _openedSettingsFromPause;

    private bool FocusIsTextEditing()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        return focus is LineEdit or TextEdit or SpinBox;
    }

    private bool AnyPanelOpen()
    {
        return (_pausePanel?.Visible ?? false) || (_settingsPanel?.Visible ?? false)
               || (_journalPanel?.Visible ?? false) || (_bagPanel?.Visible ?? false) || (_tutorialPanel?.Visible ?? false)
               || (_cutsceneRoot?.Visible ?? false) || (_endingRoot?.Visible ?? false)
               || (_shatterRoot?.Visible ?? false)
               || (_recollectionPanel?.Visible ?? false)
               || (_confirmPanel?.Visible ?? false);
    }

    private void RequestHint()
    {
        if (GameState.Instance == null || Dialogue.IsShowing)
            return;

        var (text, objectToPulse) = GameState.Instance.RequestHint();
        AudioManager.Play(Sfx.PageTurn, -9f);
        ShowToast(text, seconds: 5.5f);
        if (!string.IsNullOrEmpty(objectToPulse))
            Hotspots.RevealObject(objectToPulse);
    }

    // ========================================================== back navigation

    private void BuildBackButton()
    {
        _backButton = new Button { Text = "\u2190 Step Back", Visible = false };
        if (_font != null)
            _backButton.AddThemeFontOverride("font", _font);
        _backButton.AddThemeFontSizeOverride("font_size", 22);
        _backButton.AddThemeColorOverride("font_color", new Color(0.93f, 0.89f, 0.82f));
        _backButton.AddThemeColorOverride("font_hover_color", new Color(1f, 0.96f, 0.85f));

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.5f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.35f),
        };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(4);
        normal.SetContentMarginAll(8);
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.75f, 0.55f, 0.16f),
            BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.5f),
        };
        hover.SetBorderWidthAll(1);
        hover.SetCornerRadiusAll(4);
        hover.SetContentMarginAll(8);
        _backButton.AddThemeStyleboxOverride("normal", normal);
        _backButton.AddThemeStyleboxOverride("hover", hover);
        _backButton.AddThemeStyleboxOverride("pressed", hover);
        _backButton.AddThemeStyleboxOverride("focus", hover);

        // Bottom-left, just above the controls reminder. Focus off so Space /
        // Enter (dialogue advance) can never re-press it after a click.
        _backButton.AnchorLeft = 0;
        _backButton.AnchorRight = 0;
        _backButton.AnchorTop = 1;
        _backButton.AnchorBottom = 1;
        _backButton.OffsetLeft = 16;
        _backButton.OffsetRight = 268;
        _backButton.OffsetTop = -88;
        _backButton.OffsetBottom = -44;
        _backButton.TooltipText = "Return to the previous view (Q)";
        _backButton.FocusMode = Control.FocusModeEnum.None;
        _backButton.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -9f);
        _backButton.Pressed += GoBack;
        _hud.AddChild(_backButton);
    }

    private void BuildMemoryVerbBar()
    {
        _verbPanel = new PanelContainer { Visible = false, Name = "MemoryVerbBar" };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.55f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.45f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = 10;
        style.ContentMarginRight = 10;
        style.ContentMarginTop = 5;
        style.ContentMarginBottom = 5;
        _verbPanel.AddThemeStyleboxOverride("panel", style);
        _verbPanel.AnchorLeft = 0.5f;
        _verbPanel.AnchorRight = 0.5f;
        _verbPanel.AnchorTop = 1f;
        _verbPanel.AnchorBottom = 1f;
        _verbPanel.OffsetLeft = -350;
        _verbPanel.OffsetRight = 350;
        _verbPanel.OffsetTop = -104;
        _verbPanel.OffsetBottom = -48;

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 6);
        _verbPanel.AddChild(row);

        var title = MakeLabel("MEMORY", 15, new Color(0.82f, 0.72f, 0.54f, 0.9f));
        title.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(title);

        MemoryVerb[] verbs = [MemoryVerb.Echo, MemoryVerb.Anchor, MemoryVerb.Trace, MemoryVerb.Release];
        foreach (var verb in verbs)
        {
            var captured = verb;
            var button = MakeButton(string.Empty, 19);
            button.Name = $"{verb}VerbButton";
            button.CustomMinimumSize = new Vector2(128, 36);
            button.FocusMode = Control.FocusModeEnum.None;
            button.Visible = false;
            button.Pressed += () => MemoryVerbSystem.Toggle(captured);
            _verbButtons[verb] = button;
            row.AddChild(button);
        }

        _hud.AddChild(_verbPanel);
    }

    private void RefreshMemoryVerbBar()
    {
        if (_verbPanel == null)
            return;

        var gs = GameState.Instance;
        bool anyUnlocked = false;
        foreach (var (verb, button) in _verbButtons)
        {
            bool unlocked = gs != null && gs.HasVerb(verb);
            anyUnlocked |= unlocked;
            bool active = unlocked && gs.ActiveVerb == verb;
            button.Visible = unlocked;
            button.Disabled = false;
            button.Text = $"{(active ? "◆ " : string.Empty)}{MemoryVerbSystem.HotkeyLabel(verb)} — {MemoryVerbSystem.DisplayName(verb)}";
            button.TooltipText = MemoryVerbSystem.Description(verb) + " Right-click or Esc releases it.";
            button.AddThemeColorOverride("font_color", active
                ? new Color(1f, 0.9f, 0.62f)
                : new Color(0.93f, 0.89f, 0.82f));
        }

        _verbPanel.Visible = _context == UiContext.Game
                             && anyUnlocked
                             && !AnyPanelOpen()
                             && !Dialogue.IsShowing
                             && !GetTree().Paused
                             && !SceneManager.IsChanging;
    }

    /// <summary>Keeps the Step Back button in sync with the live scene: shown
    /// in-game whenever the current view has somewhere to step back to
    /// (puzzle -&gt; its room, room -&gt; previous room), never over dialogue,
    /// panels or a scene change. Label/target refresh only on scene change.</summary>
    private void RefreshBackButton()
    {
        if (_backButton == null)
            return;

        string current = SceneManager.CurrentScenePath ?? string.Empty;
        if (current != _navSceneCachePath)
        {
            _navSceneCachePath = current;
            if (SceneManager.TryGetBackTarget(current, out string target, out string label))
            {
                _backTargetPath = target;
                _backButton.Text = label;
            }
            else
            {
                _backTargetPath = string.Empty;
            }
        }

        _backButton.Visible = _context == UiContext.Game
                              && !string.IsNullOrEmpty(_backTargetPath)
                              && !AnyPanelOpen()
                              && !Dialogue.IsShowing
                              && !SceneManager.IsChanging
                              && !GetTree().Paused;
    }

    private void GoBack()
    {
        if (string.IsNullOrEmpty(_backTargetPath) || SceneManager.IsChanging ||
            Dialogue.IsShowing || AnyPanelOpen() || GetTree().Paused)
            return;

        MemoryVerbSystem.Cancel(silent: true);
        if (ClaritySystem.IsArmed)
            ClaritySystem.Disarm(silent: true);
        Hotspots.HideAll();
        AudioManager.Play(Sfx.UiBack, -6f);
        SceneManager.ChangeScene(_backTargetPath);
    }

    /// <summary>The Step Back button's mirror, bottom-right: after stepping
    /// back through finished rooms, this returns to the furthest ("new")
    /// room reached — finished rooms in between no longer answer clicks,
    /// so the way forward lives here instead of in their doors.</summary>
    private void BuildForwardButton()
    {
        _forwardButton = new Button { Text = "Return \u2192", Visible = false };
        if (_font != null)
            _forwardButton.AddThemeFontOverride("font", _font);
        _forwardButton.AddThemeFontSizeOverride("font_size", 20);
        _forwardButton.AddThemeColorOverride("font_color", new Color(0.93f, 0.89f, 0.82f));
        _forwardButton.AddThemeColorOverride("font_hover_color", new Color(1f, 0.96f, 0.85f));

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.5f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.35f),
        };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(4);
        normal.SetContentMarginAll(8);
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.75f, 0.55f, 0.16f),
            BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.5f),
        };
        hover.SetBorderWidthAll(1);
        hover.SetCornerRadiusAll(4);
        hover.SetContentMarginAll(8);
        _forwardButton.AddThemeStyleboxOverride("normal", normal);
        _forwardButton.AddThemeStyleboxOverride("hover", hover);
        _forwardButton.AddThemeStyleboxOverride("pressed", hover);
        _forwardButton.AddThemeStyleboxOverride("focus", hover);

        _forwardButton.AnchorLeft = 1;
        _forwardButton.AnchorRight = 1;
        _forwardButton.AnchorTop = 1;
        _forwardButton.AnchorBottom = 1;
        _forwardButton.OffsetLeft = -268;
        _forwardButton.OffsetRight = -16;
        _forwardButton.OffsetTop = -88;
        _forwardButton.OffsetBottom = -44;
        _forwardButton.TooltipText = "Return to the furthest room you have reached";
        _forwardButton.FocusMode = Control.FocusModeEnum.None;
        _forwardButton.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -9f);
        _forwardButton.Pressed += GoForward;
        _hud.AddChild(_forwardButton);
    }

    private void RefreshForwardButton()
    {
        if (_forwardButton == null)
            return;

        var gs = GameState.Instance;
        string cacheKey = $"{SceneManager.CurrentScenePath}|{gs?.FurthestScenePath}";
        if (cacheKey != _forwardCacheKey)
        {
            _forwardCacheKey = cacheKey;
            if (gs != null && gs.TryGetForwardScene(out string target))
            {
                _forwardTargetPath = target;
                _forwardButton.Text = $"Return to {ForwardSceneName(target)} \u2192";
            }
            else
            {
                _forwardTargetPath = string.Empty;
            }
        }

        _forwardButton.Visible = _context == UiContext.Game
                                 && !string.IsNullOrEmpty(_forwardTargetPath)
                                 && !AnyPanelOpen()
                                 && !Dialogue.IsShowing
                                 && !SceneManager.IsChanging
                                 && !GetTree().Paused;
    }

    private static string ForwardSceneName(string scenePath)
    {
        string file = scenePath ?? string.Empty;
        int slash = file.LastIndexOf('/');
        if (slash >= 0)
            file = file.Substring(slash + 1);
        return file switch
        {
            "bedroom_prologue.tscn" => "the bedroom",
            "journey_to_room_2.tscn" => "the way to the sitting room",
            "journey_to_room_3.tscn" => "the way to the kept room",
            "journey_to_room_4.tscn" => "the way to the hallway",
            "journey_to_porch.tscn" => "the empty road",
            "room_1.tscn" => "the kitchen",
            "room_1_puzzle.tscn" => "the kitchen drawer",
            "room_2.tscn" => "the sitting room",
            "room_2_puzzle.tscn" => "the table drawer",
            "room_3.tscn" => "the kept room",
            "room_4.tscn" => "the hallway",
            "Room_4_puzzle.tscn" => "the brass door",
            "room_5.tscn" => "the sliding room",
            "room_6.tscn" => "the last afternoon",
            "porch_447.tscn" => "the porch",
            "room_7.tscn" => "the last room",
            _ => "the new room",
        };
    }

    private void GoForward()
    {
        if (string.IsNullOrEmpty(_forwardTargetPath) || SceneManager.IsChanging ||
            Dialogue.IsShowing || AnyPanelOpen() || GetTree().Paused)
            return;

        MemoryVerbSystem.Cancel(silent: true);
        if (ClaritySystem.IsArmed)
            ClaritySystem.Disarm(silent: true);
        Hotspots.HideAll();
        AudioManager.Play(Sfx.UiClick, -6f);
        SceneManager.ChangeScene(_forwardTargetPath);
    }

    /// <summary>"Remember it all" as a standing HUD affordance: while a
    /// Recollection is available it sits beside the bag, in gold, instead of
    /// depending on the player arming Clarity at the lucky moment.</summary>
    private void BuildRememberButton()
    {
        _rememberButton = new Button { Name = "RememberItAllButton", Text = "\u2726 Remember it all", Visible = false };
        if (_font != null)
            _rememberButton.AddThemeFontOverride("font", _font);
        _rememberButton.AddThemeFontSizeOverride("font_size", 20);
        _rememberButton.AddThemeColorOverride("font_color", new Color(1f, 0.88f, 0.58f));
        _rememberButton.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.75f));

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.16f, 0.11f, 0.04f, 0.62f),
            BorderColor = new Color(1f, 0.82f, 0.45f, 0.55f),
        };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(4);
        normal.SetContentMarginAll(8);
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.65f, 0.3f, 0.3f),
            BorderColor = new Color(1f, 0.9f, 0.6f, 0.85f),
        };
        hover.SetBorderWidthAll(1);
        hover.SetCornerRadiusAll(4);
        hover.SetContentMarginAll(8);
        _rememberButton.AddThemeStyleboxOverride("normal", normal);
        _rememberButton.AddThemeStyleboxOverride("hover", hover);
        _rememberButton.AddThemeStyleboxOverride("pressed", hover);
        _rememberButton.AddThemeStyleboxOverride("focus", hover);

        // Under the top-right status chip, just left of the bag icon.
        _rememberButton.AnchorLeft = 1;
        _rememberButton.AnchorRight = 1;
        _rememberButton.AnchorTop = 0;
        _rememberButton.AnchorBottom = 0;
        _rememberButton.OffsetLeft = -316;
        _rememberButton.OffsetRight = -88;
        _rememberButton.OffsetTop = 140;
        _rememberButton.OffsetBottom = 184;
        _rememberButton.TooltipText = "Spend ALL Clarity: everything left in this room shows itself truly";
        _rememberButton.FocusMode = Control.FocusModeEnum.None;
        _rememberButton.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -9f);
        _rememberButton.Pressed += () =>
        {
            _recollectionOfferedThisArming = true;
            MaybeOfferRecollection();
        };
        _hud.AddChild(_rememberButton);
    }

    private void RefreshRememberButton()
    {
        if (_rememberButton == null)
            return;

        var gs = GameState.Instance;
        _rememberButton.Visible = _context == UiContext.Game
                                  && gs != null
                                  && gs.CanRecollect(out _, out _)
                                  && !AnyPanelOpen()
                                  && !Dialogue.IsShowing
                                  && !SceneManager.IsChanging
                                  && !GetTree().Paused;
    }

    // ================================================================= context

    private void UpdateContext(bool force = false)
    {
        string sceneFile = GetTree().CurrentScene?.SceneFilePath ?? string.Empty;
        UiContext next = sceneFile switch
        {
            "res://scenes/ui/main_menu.tscn" => UiContext.Menu,
            "res://scenes/managers/game_manager.tscn" => UiContext.Game,
            _ => UiContext.Intro,
        };

        if (!force && next == _context)
            return;
        _context = next;

        _hud.Visible = next == UiContext.Game;
        _corruptionRect.Visible = next == UiContext.Game;
        _menuSettingsButton.Visible = next == UiContext.Menu;
        _menuHowToButton.Visible = next == UiContext.Menu;
        if (next == UiContext.Game && _tutorialPanel.Visible)
            GetTree().Paused = true;

        if (next != UiContext.Game)
        {
            Hotspots.HideAll();
            if (GetTree().Paused)
                ResumeGame();
            _pausePanel.Visible = false;
            _journalPanel.Visible = false;
            _bagPanel.Visible = false;
            _openedBagFromPause = false;
            if (_cutscenePlaying)
                RequestCutsceneSkip();
            if (next == UiContext.Intro)
                _tutorialPanel.Visible = false;
            if (next != UiContext.Menu)
                _settingsPanel.Visible = false;
        }

        RefreshHud();
    }

    // ================================================================ pause UI

    public void PauseGame()
    {
        if (_context != UiContext.Game)
            return;

        MemoryVerbSystem.Cancel(silent: true);
        GameState.Instance?.SaveGame("pause");
        GetTree().Paused = true;
        _pausePanel.Visible = true;
        AudioManager.Play(Sfx.UiClick, -6f);
        AudioManager.Instance?.SetPausedDuck(true);
        (_pausePanel.GetNodeOrNull<BaseButton>("Center/Panel/VBox/ResumeButton"))?.GrabFocus();
    }

    public void ResumeGame()
    {
        GetTree().Paused = false;
        _pausePanel.Visible = false;
        _journalPanel.Visible = false;
        _bagPanel.Visible = false;
        _openedBagFromPause = false;
        _settingsPanel.Visible = false;
        AudioManager.Instance?.SetPausedDuck(false);
    }

    private void QuitToMenu()
    {
        GameState.Instance?.SaveGame("quit to menu");
        MemoryVerbSystem.Cancel(silent: true);
        ClaritySystem.Disarm(silent: true);
        ResumeGame();
        AudioManager.Instance?.StopAmbience();
        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    // ============================================================== settings UI

    public void OpenSettings()
    {
        _openedSettingsFromPause = _pausePanel.Visible;
        if (_pausePanel.Visible)
            _pausePanel.Visible = false;

        foreach (var (slider, _) in _settingsRefreshers)
            slider.SetValueNoSignal(slider.GetMeta("refresh").AsDouble());

        _settingsPanel.Visible = true;
        AudioManager.Play(Sfx.UiClick, -6f);
    }

    private void CloseSettings(bool backToPause)
    {
        _settingsPanel.Visible = false;
        Settings.Instance?.Save();
        AudioManager.Play(Sfx.UiBack, -6f);
        if (backToPause)
            _pausePanel.Visible = true;
        _openedSettingsFromPause = false;
    }

    // =============================================================== journal UI

    public void OpenJournal(bool fromPause = false)
    {
        MemoryVerbSystem.Cancel(silent: true);
        _openedJournalFromPause = fromPause;
        if (fromPause)
            _pausePanel.Visible = false;

        if (!fromPause && _context == UiContext.Game)
            GetTree().Paused = true;

        _journalPanel.Visible = true;
        AudioManager.Play(Sfx.PageTurn, -7f);
        RefreshJournal();
    }

    private void CloseJournal(bool backToPause)
    {
        if (_notesDirty && GameState.Instance != null && _notesEdit != null)
        {
            GameState.Instance.SetPlayerNotes(_notesEdit.Text);
            GameState.Instance.SaveGame("player notes");
            _notesDirty = false;
        }

        _journalPanel.Visible = false;
        AudioManager.Play(Sfx.UiBack, -6f);
        if (!backToPause && !_pausePanel.Visible && _context == UiContext.Game)
            GetTree().Paused = false;
    }

    private void RefreshJournal()
    {
        foreach (var child in _journalList.GetChildren())
            child.QueueFree();

        var gs = GameState.Instance;
        _journalStats.Text = gs?.StatsSummary() ?? string.Empty;

        if (_journalTab == 0)
        {
            var journalOrder = gs?.FragmentDisplayOrder;
            for (int i = 0; i < GameState.MaxShards; i++)
            {
                int index = journalOrder != null ? journalOrder[i] : i;
                bool recovered = gs != null && (Globals.SHARDS_COLLECTED[index]
                                                || System.Array.IndexOf(gs.PlacedShardBySlot, index) >= 0);
                string title = GameState.ShardTitles[index];
                var btn = MakeButton(recovered ? title : "· · ·");
                btn.Disabled = !recovered;
                btn.Pressed += () =>
                {
                    AudioManager.Play(Sfx.PageTurn, -10f);
                    string body = gs != null && !string.IsNullOrWhiteSpace(gs.ShardFragments[index])
                        ? gs.ShardFragments[index]
                        : "Recovered, but its words are still surfacing.";
                    string placed = gs != null && System.Array.IndexOf(gs.PlacedShardBySlot, index) >= 0
                        ? "\n\n[ Returned to the house. ]"
                        : "\n\n[ Carried. ]";
                    _journalBody.Text = $"[b]{title}[/b]\n\n{body}{placed}";
                };
                _journalList.AddChild(btn);
            }
            _journalBody.Text = "[i]Choose a fragment. What was recovered can be re-read; " +
                                "what is still missing keeps its silence.[/i]";
        }
        else if (_journalTab == 1)
        {
            var btnAll = MakeButton("All echoes");
            btnAll.Pressed += () => ShowEchoes(gs, null);
            _journalList.AddChild(btnAll);
            ShowEchoes(gs, null);
        }
        else
        {
            RefreshNotesTab(gs);
        }

        _notesBox.Visible = _journalTab == 2;
    }

    private void RefreshNotesTab(GameState gs)
    {
        if (gs == null || gs.UnlockedClues.Count == 0)
        {
            var none = MakeLabel("No clues noted yet.\nExamine things closely —\nwhat matters will be kept here.", 19,
                new Color(0.85f, 0.8f, 0.72f, 0.75f));
            none.AutowrapMode = TextServer.AutowrapMode.Word;
            _journalList.AddChild(none);
        }
        else
        {
            foreach (var clueId in gs.UnlockedClues)
            {
                if (!gs.TryGetClue(clueId, out string title, out string text))
                    continue;
                var btn = MakeButton(title, 20);
                btn.Pressed += () =>
                {
                    AudioManager.Play(Sfx.PageTurn, -10f);
                    _journalBody.Text = $"[b]{title}[/b]\n\n{text}";
                };
                _journalList.AddChild(btn);
            }
        }

        _journalBody.Text = "[i]What the house has shown you, kept in one place. " +
                            "Choose a clue — and use the page below for your own reckoning.[/i]";

        // Sync the free-notes page; clear dirty AFTER setting Text in case
        // the assignment itself raises TextChanged synchronously.
        _notesEdit.Text = gs?.PlayerNotes ?? string.Empty;
        _notesDirty = false;
    }

    private void ShowEchoes(GameState gs, int? _)
    {
        if (gs == null || gs.EchoLog.Count == 0)
        {
            _journalBody.Text = "[i]Nothing has been said in this house yet.[/i]";
            return;
        }

        var lines = new List<string>();
        for (int i = gs.EchoLog.Count - 1; i >= 0; i--)
            lines.Add("— " + gs.EchoLog[i]);
        _journalBody.Text = string.Join("\n\n", lines);
        _journalBody.ScrollToLine(0);
    }

    // ================================================================== toasts

    public void ShowToast(string text) => ShowToast(text, 3.4f, false);

    public void ShowToast(string text, float seconds = 3.4f, bool quiet = false)
    {
        if (_toastLabel == null || string.IsNullOrWhiteSpace(text) || _context != UiContext.Game)
            return;

        _toastLabel.Text = text;

        // The toast chip grows down to hold the whole message instead of
        // letting the second line spill off it.
        float toastH = Mathf.Max(MeasureHudTextHeight(text, 804f, 23), 28f);
        _toastLabel.OffsetBottom = _toastLabel.OffsetTop + toastH;
        if (_toastPanel != null)
            _toastPanel.OffsetBottom = _toastPanel.OffsetTop + toastH + 20f;

        _toastLabel.Visible = true;
        var m = _toastLabel.Modulate;
        m.A = quiet ? 0.55f : 0.95f;
        _toastLabel.Modulate = m;
        if (_toastPanel != null)
        {
            _toastPanel.Visible = true;
            _toastPanel.Modulate = m;
        }
        _toastUntilMsec = Time.GetTicksMsec() + (double)(seconds * 1000);
        _toastTween?.Kill();
    }

    private void FadeToastOut()
    {
        _toastTween?.Kill();
        _toastTween = CreateTween();
        _toastTween.TweenProperty(_toastLabel, "modulate:a", 0f, 0.5f);
        if (_toastPanel != null)
            _toastTween.Parallel().TweenProperty(_toastPanel, "modulate:a", 0f, 0.5f);
        _toastTween.TweenCallback(Callable.From(() =>
        {
            _toastLabel.Visible = false;
            if (_toastPanel != null)
                _toastPanel.Visible = false;
        }));
    }

    /// <summary>Wrapped height of a HUD line in the real font, so chips and
    /// captions can be sized to their words instead of overflowing.</summary>
    private float MeasureHudTextHeight(string text, float width, int fontSize)
    {
        if (_font == null || string.IsNullOrEmpty(text))
            return 0f;
        return _font.GetMultilineStringSize(
            text, HorizontalAlignment.Center, width, fontSize).Y;
    }

    /// <summary>Cutscene captions run long; the caption area grows upward
    /// over the letterbox (stepping the type down if it must) so no line
    /// of the truth ever falls off the bottom of the screen.</summary>
    private void SetCutsceneCaption(string text)
    {
        if (_cutsceneCaption == null)
            return;
        _cutsceneCaption.Text = text ?? string.Empty;

        int size = 30;
        float h = MeasureHudTextHeight(_cutsceneCaption.Text, 942f, size);
        while (h > 250f && size > 22)
        {
            size -= 2;
            h = MeasureHudTextHeight(_cutsceneCaption.Text, 942f, size);
        }
        _cutsceneCaption.AddThemeFontSizeOverride("font_size", size);
        float need = Mathf.Clamp(h + 18f, 138f, 268f);
        _cutsceneCaption.OffsetTop = _cutsceneCaption.OffsetBottom - need;
    }

    // ================================================================= confirm

    /// <summary>The game's own reminder card — parchment-dark panel and
    /// Cormorant buttons like every other surface, never the engine's
    /// default dialog. Used by New Game ("begin again?"), the first-run
    /// tutorial question, and anywhere else a choice must be deliberate.</summary>
    private void BuildConfirmPanel()
    {
        _confirmPanel = new Control { Visible = false, Name = "ConfirmPanel" };
        _confirmPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _confirmPanel.AddChild(DimBackdrop());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _confirmPanel.AddChild(center);

        var panel = MakePanel();
        panel.CustomMinimumSize = new Vector2(580, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 16);
        panel.AddChild(vbox);

        var eyebrow = MakeLabel("THE HOUSE ASKS", 16, new Color(0.82f, 0.72f, 0.54f, 0.9f));
        eyebrow.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(eyebrow);

        _confirmTextLabel = MakeLabel(string.Empty, 24);
        _confirmTextLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _confirmTextLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _confirmTextLabel.CustomMinimumSize = new Vector2(500, 0);
        vbox.AddChild(_confirmTextLabel);

        vbox.AddChild(new HSeparator());

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 14);
        vbox.AddChild(row);

        _confirmOkButton = MakeButton("Yes", 23);
        _confirmOkButton.CustomMinimumSize = new Vector2(210, 48);
        _confirmOkButton.Pressed += () =>
        {
            var action = _confirmAction;
            CloseConfirm();
            action?.Invoke();
        };
        row.AddChild(_confirmOkButton);

        _confirmCancelButton = MakeButton("Cancel", 23);
        _confirmCancelButton.CustomMinimumSize = new Vector2(210, 48);
        _confirmCancelButton.Pressed += () =>
        {
            var action = _confirmCancelAction;
            CloseConfirm();
            action?.Invoke();
        };
        row.AddChild(_confirmCancelButton);

        AddChild(_confirmPanel);
    }

    public void ShowConfirm(string text, Action onConfirm) => ShowConfirm(text, onConfirm, "Yes", "Cancel", null);

    public void ShowConfirm(string text, Action onConfirm, string okText, string cancelText)
        => ShowConfirm(text, onConfirm, okText, cancelText, null);

    public void ShowConfirm(string text, Action onConfirm, string okText, string cancelText, Action onCancel)
    {
        if (_confirmPanel == null)
        {
            onConfirm?.Invoke();
            return;
        }

        _confirmTextLabel.Text = text;
        _confirmOkButton.Text = okText;
        _confirmCancelButton.Text = cancelText;
        _confirmCancelButton.Visible = !string.IsNullOrEmpty(cancelText);
        _confirmAction = onConfirm;
        _confirmCancelAction = onCancel;
        _confirmPanel.Visible = true;
        AudioManager.Play(Sfx.PageTurn, -11f, 0.95f);
        _confirmOkButton.GrabFocus();
    }

    private void CloseConfirm()
    {
        if (_confirmPanel != null)
            _confirmPanel.Visible = false;
        _confirmAction = null;
        _confirmCancelAction = null;
    }

    // ================================================================= bag UI

    private void BuildBagButton()
    {
        // Icon only — a worn satchel, no words. It lives under the top-right
        // status panel (MEMORY / Clarity pips), clear of the room's own
        // clarity eye at the bottom and of every world hotspot strip.
        _bagButton = new Button { Name = "BagButton", Text = string.Empty };
        _bagButton.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        var icon = GD.Load<Texture2D>("res://assets/images/core/ui/bag_icon.png");
        if (icon != null)
        {
            _bagButton.Icon = icon;
            _bagButton.ExpandIcon = true;
        }
        else
        {
            _bagButton.Text = "Bag"; // texture missing: never leave the bag unreachable
            if (_font != null)
                _bagButton.AddThemeFontOverride("font", _font);
            _bagButton.AddThemeFontSizeOverride("font_size", 20);
            _bagButton.AddThemeColorOverride("font_color", new Color(0.93f, 0.89f, 0.82f));
        }

        _bagButton.AnchorLeft = 1;
        _bagButton.AnchorRight = 1;
        _bagButton.AnchorTop = 0;
        _bagButton.AnchorBottom = 0;
        _bagButton.OffsetLeft = -76;
        _bagButton.OffsetRight = -16;
        _bagButton.OffsetTop = 142;
        _bagButton.OffsetBottom = 202;
        _bagButton.FocusMode = Control.FocusModeEnum.None;

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.5f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.42f),
        };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(6);
        normal.SetContentMarginAll(7);
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.75f, 0.55f, 0.18f),
            BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.6f),
        };
        hover.SetBorderWidthAll(1);
        hover.SetCornerRadiusAll(6);
        hover.SetContentMarginAll(7);
        _bagButton.AddThemeStyleboxOverride("normal", normal);
        _bagButton.AddThemeStyleboxOverride("hover", hover);
        _bagButton.AddThemeStyleboxOverride("pressed", hover);
        _bagButton.AddThemeStyleboxOverride("focus", hover);
        _bagButton.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -9f);
        _bagButton.Pressed += ToggleBag;
        _hud.AddChild(_bagButton);
        RefreshBagButton();
    }

    private void BuildBagPanel()
    {
        _bagPanel = new Control { Visible = false, Name = "BagPanel" };
        _bagPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bagPanel.AddChild(DimBackdrop());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bagPanel.AddChild(center);

        var panel = MakePanel();
        panel.CustomMinimumSize = new Vector2(940, 560);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        var title = MakeLabel("Mara's Bag", 34);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        _bagSummary = MakeLabel("", 19, new Color(0.85f, 0.79f, 0.67f, 0.9f));
        _bagSummary.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_bagSummary);
        vbox.AddChild(new HSeparator());

        var split = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(split);

        _bagGrid = new GridContainer
        {
            Columns = 3,
            CustomMinimumSize = new Vector2(520, 290),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _bagGrid.AddThemeConstantOverride("h_separation", 8);
        _bagGrid.AddThemeConstantOverride("v_separation", 8);
        split.AddChild(_bagGrid);

        var detail = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(340, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        detail.AddThemeConstantOverride("separation", 8);
        split.AddChild(detail);

        _bagDetailTitle = MakeLabel("", 27);
        _bagDetailTitle.AutowrapMode = TextServer.AutowrapMode.Word;
        detail.AddChild(_bagDetailTitle);

        _bagDetailStatus = MakeLabel("", 19, new Color(0.95f, 0.82f, 0.55f, 0.95f));
        _bagDetailStatus.AutowrapMode = TextServer.AutowrapMode.Word;
        detail.AddChild(_bagDetailStatus);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        detail.AddChild(scroll);

        _bagDetailBody = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (_font != null)
        {
            _bagDetailBody.AddThemeFontOverride("normal_font", _font);
            _bagDetailBody.AddThemeFontOverride("bold_font", _font);
            _bagDetailBody.AddThemeFontOverride("italics_font", _font);
        }
        _bagDetailBody.AddThemeFontSizeOverride("normal_font_size", 21);
        _bagDetailBody.AddThemeColorOverride("default_color", new Color(0.93f, 0.89f, 0.82f));
        scroll.AddChild(_bagDetailBody);

        _bagVerbsLabel = MakeLabel("", 18, new Color(0.82f, 0.78f, 0.68f, 0.9f));
        _bagVerbsLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _bagVerbsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_bagVerbsLabel);

        vbox.AddChild(new HSeparator());
        var close = MakeButton("Close the bag");
        close.Pressed += () => CloseBag(backToPause: _openedBagFromPause);
        vbox.AddChild(close);

        AddChild(_bagPanel);
    }

    public void OpenBag(bool fromPause = false)
    {
        if (_context != UiContext.Game || _bagPanel == null || _endingPlaying ||
            _cutscenePlaying || _shatterPlaying)
            return;
        if (!fromPause && Dialogue.IsShowing)
            return;

        MemoryVerbSystem.Cancel(silent: true);
        ClaritySystem.Disarm(silent: true);
        Hotspots.HideAll();

        _openedBagFromPause = fromPause || _pausePanel.Visible;
        _pausePanel.Visible = false;
        GetTree().Paused = true;
        _bagPanel.Visible = true;
        AudioManager.Play(Sfx.PageTurn, -8f, 0.92f);
        RefreshBag(selectFirstUsable: true);
    }

    private void CloseBag(bool backToPause = false)
    {
        if (_bagPanel == null)
            return;

        if (_bagPanel.Visible)
            AudioManager.Play(Sfx.UiBack, -7f);
        _bagPanel.Visible = false;
        _openedBagFromPause = false;

        if (backToPause && _context == UiContext.Game)
        {
            _pausePanel.Visible = true;
            GetTree().Paused = true;
        }
        else if (_context == UiContext.Game)
        {
            GetTree().Paused = false;
        }
    }

    private void ToggleBag()
    {
        if (_bagPanel?.Visible ?? false)
            CloseBag(backToPause: _openedBagFromPause);
        else if (_pausePanel?.Visible ?? false)
            OpenBag(fromPause: true);
        else if (!Dialogue.IsShowing && !AnyPanelOpen())
            OpenBag();
    }

    private void RefreshBagButton()
    {
        if (_bagButton == null)
            return;

        var gs = GameState.Instance;
        int carried = gs?.CountCarried() ?? 0;
        int recovered = gs?.CountRecovered() ?? 0;
        // The button itself stays wordless (satchel icon); the count lives
        // in the tooltip and in the bag panel.
        _bagButton.TooltipText = $"Mara's bag — {carried} carried, {recovered}/6 recovered (B or I)";
    }

    private void RefreshBag(bool selectFirstUsable = false)
    {
        if (_bagGrid == null || _bagDetailTitle == null)
            return;

        var gs = GameState.Instance;
        RefreshBagButton();

        if (gs == null)
        {
            _bagSummary.Text = "Nothing can be carried yet.";
            _bagDetailTitle.Text = "An empty bag";
            _bagDetailStatus.Text = "";
            _bagDetailBody.Text = "[i]Recover a memory fragment and it will be kept here.[/i]";
            _bagVerbsLabel.Text = "";
            return;
        }

        if (selectFirstUsable || _bagSelectedIndex < 0 || _bagSelectedIndex >= GameState.MaxShards ||
            !gs.IsShardRecovered(_bagSelectedIndex))
        {
            _bagSelectedIndex = -1;
            for (int i = 0; i < GameState.MaxShards; i++)
            {
                if (Globals.SHARDS_COLLECTED[i])
                {
                    _bagSelectedIndex = i;
                    break;
                }
            }
            if (_bagSelectedIndex < 0)
            {
                for (int i = 0; i < GameState.MaxShards; i++)
                {
                    if (gs.IsShardRecovered(i))
                    {
                        _bagSelectedIndex = i;
                        break;
                    }
                }
            }
        }

        int placed = gs.CountPlaced();
        _bagSummary.Text = $"Carried {gs.CountCarried()} · Recovered {gs.CountRecovered()}/{GameState.MaxShards}" +
                           (placed > 0 ? $" · Returned {placed}" : "");

        foreach (var child in _bagGrid.GetChildren())
            child.QueueFree();

        // The bag is a bag: the fragments lie in it jumbled, in no order
        // that means anything. Only Mara knows the order they lived.
        for (int pos = 0; pos < GameState.MaxShards; pos++)
        {
            int index = gs.FragmentDisplayOrder[pos];
            bool carried = Globals.SHARDS_COLLECTED[index];
            int placedSlot = System.Array.IndexOf(gs.PlacedShardBySlot, index);
            bool recovered = carried || placedSlot >= 0;
            bool selected = index == _bagSelectedIndex;

            var slot = MakeButton(string.Empty, 17);
            slot.CustomMinimumSize = new Vector2(164, 138);
            slot.FocusMode = Control.FocusModeEnum.None;
            slot.Disabled = !recovered;
            slot.TooltipText = recovered ? GameState.ShardTitles[index] : "Not recovered yet";
            slot.Pressed += () =>
            {
                _bagSelectedIndex = index;
                RefreshBag();
            };
            ApplyBagSlotStyle(slot, selected, recovered, carried);

            var slotBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            slotBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            slotBox.OffsetLeft = 8;
            slotBox.OffsetTop = 7;
            slotBox.OffsetRight = -8;
            slotBox.OffsetBottom = -7;
            slotBox.AddThemeConstantOverride("separation", 3);
            slot.AddChild(slotBox);

            Texture2D icon = recovered && BagIconPaths[index] != null
                ? GD.Load<Texture2D>(BagIconPaths[index])
                : null;
            if (icon != null)
            {
                var image = new TextureRect
                {
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    Texture = icon,
                    CustomMinimumSize = new Vector2(0, 58),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Modulate = carried ? Colors.White : new Color(0.72f, 0.82f, 1f, 0.72f),
                };
                slotBox.AddChild(image);
            }
            else
            {
                var sigil = MakeLabel(recovered ? "\u25C6" : "?", 31,
                    recovered ? new Color(1f, 0.86f, 0.52f, 0.96f)
                              : new Color(0.55f, 0.5f, 0.44f, 0.55f));
                sigil.CustomMinimumSize = new Vector2(0, 58);
                sigil.HorizontalAlignment = HorizontalAlignment.Center;
                sigil.VerticalAlignment = VerticalAlignment.Center;
                sigil.MouseFilter = Control.MouseFilterEnum.Ignore;
                slotBox.AddChild(sigil);
            }

            var name = MakeLabel(recovered ? BagShortNames[index] : "Unremembered", 17,
                recovered ? new Color(0.94f, 0.9f, 0.82f) : new Color(0.58f, 0.54f, 0.48f, 0.65f));
            name.CustomMinimumSize = new Vector2(0, 40);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.AutowrapMode = TextServer.AutowrapMode.Word;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            slotBox.AddChild(name);

            var status = MakeLabel(carried ? "IN BAG" : placedSlot >= 0 ? "RETURNED" : "NOT FOUND", 14,
                carried ? new Color(1f, 0.84f, 0.5f)
                        : placedSlot >= 0 ? new Color(0.66f, 0.8f, 1f)
                                          : new Color(0.55f, 0.51f, 0.46f, 0.55f));
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.MouseFilter = Control.MouseFilterEnum.Ignore;
            slotBox.AddChild(status);

            _bagGrid.AddChild(slot);
        }

        if (_bagSelectedIndex >= 0 && gs.IsShardRecovered(_bagSelectedIndex))
        {
            int index = _bagSelectedIndex;
            _bagDetailTitle.Text = GameState.ShardTitles[index];
            _bagDetailStatus.Text = Globals.SHARDS_COLLECTED[index]
                ? "In Mara's bag"
                : "Returned to the last room";
            string body = !string.IsNullOrWhiteSpace(gs.ShardFragments[index])
                ? gs.ShardFragments[index]
                : GameState.DefaultShardFragments[index];
            _bagDetailBody.Text = body;
        }
        else
        {
            _bagDetailTitle.Text = "Nothing carried yet";
            _bagDetailStatus.Text = "The bag is light.";
            _bagDetailBody.Text = "[i]Fragments Mara recovers will be kept here until the last room asks for them back. The Memory Journal (J) still keeps everything she has heard.[/i]";
        }

        var learned = new List<string>();
        foreach (var verb in new[] { MemoryVerb.Echo, MemoryVerb.Anchor, MemoryVerb.Trace, MemoryVerb.Release })
        {
            if (gs.HasVerb(verb))
                learned.Add($"{GameState.VerbDisplayName(verb)} ({MemoryVerbSystem.HotkeyLabel(verb)})");
        }
        _bagVerbsLabel.Text = learned.Count > 0
            ? "Ways of remembering: " + string.Join(" · ", learned) + " — Mara's notes are in the Journal (J)."
            : "Ways of remembering: none learned yet — the house will teach them as the story gives them to her.";
    }

    private static void ApplyBagSlotStyle(Button button, bool selected, bool recovered, bool carried)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = selected
                ? new Color(0.5f, 0.35f, 0.13f, 0.95f)
                : carried
                    ? new Color(0.11f, 0.085f, 0.055f, 0.94f)
                    : recovered
                        ? new Color(0.08f, 0.12f, 0.18f, 0.92f)
                        : new Color(0.05f, 0.045f, 0.04f, 0.62f),
            BorderColor = selected
                ? new Color(1f, 0.87f, 0.5f, 0.98f)
                : new Color(0.72f, 0.62f, 0.45f, recovered ? 0.52f : 0.25f),
        };
        normal.SetBorderWidthAll(selected ? 2 : 1);
        normal.SetCornerRadiusAll(5);
        normal.SetContentMarginAll(6);

        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.62f, 0.44f, 0.17f, 0.94f),
            BorderColor = new Color(1f, 0.9f, 0.58f, 0.95f),
        };
        hover.SetBorderWidthAll(1);
        hover.SetCornerRadiusAll(5);
        hover.SetContentMarginAll(6);

        var disabled = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.5f),
            BorderColor = new Color(0.45f, 0.4f, 0.32f, 0.22f),
        };
        disabled.SetBorderWidthAll(1);
        disabled.SetCornerRadiusAll(5);
        disabled.SetContentMarginAll(6);

        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", hover);
        button.AddThemeStyleboxOverride("disabled", disabled);
    }

    // ===================================================================== HUD

    private void RefreshHud()
    {
        if (_objectiveLabel == null)
            return;

        var gs = GameState.Instance;
        _objectiveLabel.Text = (gs?.GetCurrentObjective() ?? string.Empty);
        var objectivePanel = _objectiveLabel.GetParent<Control>();
        objectivePanel.Visible = !string.IsNullOrWhiteSpace(_objectiveLabel.Text);
        if (objectivePanel.Visible)
        {
            // The objective chip fits its text rather than clipping line two.
            float objectiveH = MeasureHudTextHeight(_objectiveLabel.Text, 516f, 21);
            objectivePanel.OffsetBottom = objectivePanel.OffsetTop
                + Mathf.Max(objectiveH, 26f) + 24f;
        }

        // Clarity pips (ColorRects — no font-glyph roulette).
        int clarity = gs?.ClarityCount ?? 0;
        for (int i = 0; i < _clarityPips.GetChildCount(); i++)
        {
            if (_clarityPips.GetChild(i) is ColorRect pip)
                pip.Color = i < clarity
                    ? new Color(0.95f, 0.87f, 0.65f, 0.95f)
                    : new Color(0.4f, 0.36f, 0.3f, 0.5f);
        }

        var tier = gs?.CurrentTier ?? CorruptionTier.Calm;
        _integrityLabel.Text = "MEMORY — " + GameState.TierDisplayName(tier).ToUpperInvariant();
        _integrityBar.Value = 1f - (gs?.Corruption01 ?? 0f);

        _shardLabel.Text = gs != null
            ? $"Fragments  {gs.CountRecovered()}/{GameState.MaxShards}" +
              (gs.CountPlaced() > 0 ? $"   ·   Returned {gs.CountPlaced()}" : "")
            : string.Empty;

        RefreshBagButton();
        RefreshMemoryVerbBar();
    }

    private void RefreshCorruptionShader()
    {
        if (_corruptionMaterial != null)
            _corruptionMaterial.SetShaderParameter("frozen", Settings.ReduceMotionOrDefault() ? 1f : 0f);
    }

    // ============================================================ construction

    private Label MakeLabel(string text, int size = 20, Color? color = null)
    {
        var label = new Label { Text = text };
        if (_font != null)
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? new Color(0.93f, 0.89f, 0.82f, 0.95f));
        // A hair of shadow keeps HUD text readable now that the chips under
        // it are translucent over bright rooms.
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.7f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private Button MakeButton(string text, int size = 24)
    {
        var b = new Button { Text = text };
        if (_font != null)
            b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", new Color(0.93f, 0.89f, 0.82f));
        b.AddThemeColorOverride("font_hover_color", new Color(1f, 0.96f, 0.85f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.42f, 0.38f));

        var flat = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.07f, 0.06f, 0.0f),
        };
        flat.SetContentMarginAll(6);
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.75f, 0.55f, 0.14f),
            BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.35f),
        };
        hover.SetContentMarginAll(6);
        hover.SetBorderWidthAll(1);
        b.AddThemeStyleboxOverride("normal", flat);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", hover);

        b.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -9f);
        b.Pressed += () => AudioManager.Play(Sfx.UiClick, -5f);
        return b;
    }

    /// <summary>Translucent sibling of MakePanel for surfaces that float
    /// over the room itself (objective, MEMORY/Clarity status): the memory
    /// stays visible underneath them. Modal panels keep the solid panel.</summary>
    private static PanelContainer MakeHudPanel()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.46f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.38f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.ContentMarginLeft = 14;
        sb.ContentMarginRight = 14;
        sb.ContentMarginTop = 9;
        sb.ContentMarginBottom = 9;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", sb);
        return panel;
    }

    private static PanelContainer MakePanel()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.92f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.55f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.ContentMarginLeft = 28;
        sb.ContentMarginRight = 28;
        sb.ContentMarginTop = 22;
        sb.ContentMarginBottom = 22;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", sb);
        return panel;
    }

    private static Control DimBackdrop()
    {
        var rect = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = Control.MouseFilterEnum.Stop };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    /// <summary>The house taking a room back: a drowning red tint, spreading
    /// cracks, an explanation, and a huge corrupt count. Sits above the
    /// world and the corruption shader, below the HUD chips and menus;
    /// every part ignores the mouse so play never stops.</summary>
    private void BuildCollapseOverlay()
    {
        _collapseRoot = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Name = "CollapseOverlay" };
        _collapseRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_collapseRoot);

        _collapseTint = new ColorRect
        {
            Color = new Color(0.38f, 0.02f, 0.05f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _collapseTint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _collapseRoot.AddChild(_collapseTint);

        _collapseCracks = new ShatterCrackOverlay { MouseFilter = Control.MouseFilterEnum.Ignore };
        _collapseCracks.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _collapseRoot.AddChild(_collapseCracks);

        var warningPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var warningStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.015f, 0.025f, 0.84f),
            BorderColor = new Color(0.72f, 0.14f, 0.16f, 0.85f),
        };
        warningStyle.SetBorderWidthAll(1);
        warningStyle.SetCornerRadiusAll(4);
        warningStyle.ContentMarginLeft = 18;
        warningStyle.ContentMarginRight = 18;
        warningStyle.ContentMarginTop = 10;
        warningStyle.ContentMarginBottom = 10;
        warningPanel.AddThemeStyleboxOverride("panel", warningStyle);
        warningPanel.AnchorLeft = 0.5f;
        warningPanel.AnchorRight = 0.5f;
        warningPanel.OffsetLeft = -452;
        warningPanel.OffsetRight = 452;
        warningPanel.OffsetTop = 76;
        warningPanel.OffsetBottom = 146;
        var warning = MakeLabel(
            "CLARITY IS GONE — the house is taking this room back. Find what remains and open its lock before the count ends, or everything here corrupts.",
            21, new Color(0.98f, 0.88f, 0.8f, 0.98f));
        warning.AutowrapMode = TextServer.AutowrapMode.Word;
        warning.HorizontalAlignment = HorizontalAlignment.Center;
        warning.MouseFilter = Control.MouseFilterEnum.Ignore;
        warningPanel.AddChild(warning);
        _collapseRoot.AddChild(warningPanel);

        var timerPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var timerStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.008f, 0.014f, 0.88f),
            BorderColor = new Color(0.8f, 0.18f, 0.2f, 0.9f),
            ShadowColor = new Color(0.4f, 0.02f, 0.03f, 0.55f),
            ShadowSize = 14,
        };
        timerStyle.SetBorderWidthAll(2);
        timerStyle.SetCornerRadiusAll(6);
        timerStyle.ContentMarginLeft = 22;
        timerStyle.ContentMarginRight = 22;
        timerStyle.ContentMarginTop = 2;
        timerStyle.ContentMarginBottom = 6;
        timerPanel.AddThemeStyleboxOverride("panel", timerStyle);
        timerPanel.AnchorLeft = 0.5f;
        timerPanel.AnchorRight = 0.5f;
        timerPanel.OffsetLeft = -78;
        timerPanel.OffsetRight = 78;
        timerPanel.OffsetTop = 156;
        timerPanel.OffsetBottom = 240;
        _collapseTimerLabel = MakeLabel("15", 64, new Color(1f, 0.92f, 0.82f));
        _collapseTimerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _collapseTimerLabel.VerticalAlignment = VerticalAlignment.Center;
        _collapseTimerLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        timerPanel.AddChild(_collapseTimerLabel);
        _collapseRoot.AddChild(timerPanel);
    }

    private void OnCollapseStarted()
    {
        _collapseUiActive = true;
        if (_collapseRoot != null)
            _collapseRoot.Visible = true;
        _collapseCracks?.ResetCracks();
        _collapseCracks?.SetFracture(0.08f);

        AudioManager.Play(Sfx.CorruptionGlitch, -4f, 0.8f);
        AudioManager.Play(Sfx.ScreenCrack1, -6f);
        AudioManager.Play(Sfx.CorruptionWhisper, -11f, 0.75f);
        AudioManager.Instance?.PlayCollapseMusic();
        UpdateCollapseTimer(GameState.CollapseGraceSeconds);
    }

    private void OnCollapseTicked(float secondsLeft)
    {
        UpdateCollapseTimer(secondsLeft);
        int whole = Mathf.CeilToInt(Mathf.Max(secondsLeft, 0f));

        // The count, audible — rising and louder as the room goes.
        AudioManager.PlayVaried(Sfx.ClockTick, whole <= 5 ? -3.5f : -8f,
            1f + (GameState.CollapseGraceSeconds - whole) * 0.02f, 0.01f);
        if (whole == 10)
            AudioManager.Play(Sfx.ScreenCrack2, -7f, 0.95f);
        else if (whole == 5)
        {
            AudioManager.Play(Sfx.ScreenCrack2, -5f, 1.08f);
            AudioManager.Play(Sfx.CorruptionWhisper, -10f, 0.85f);
        }
    }

    private void UpdateCollapseTimer(float secondsLeft)
    {
        if (_collapseTimerLabel == null)
            return;
        int whole = Mathf.CeilToInt(Mathf.Max(secondsLeft, 0f));
        _collapseTimerLabel.Text = whole.ToString();
        float danger = 1f - Mathf.Clamp(secondsLeft / GameState.CollapseGraceSeconds, 0f, 1f);
        _collapseTimerLabel.AddThemeColorOverride("font_color",
            new Color(1f, Mathf.Lerp(0.92f, 0.3f, danger), Mathf.Lerp(0.82f, 0.2f, danger)));
    }

    private void OnCollapseEnded(bool rescued)
    {
        _collapseUiActive = false;
        if (_collapseRoot != null)
            _collapseRoot.Visible = false;
        _collapseCracks?.ResetCracks();
        if (_collapseTint != null)
            _collapseTint.Color = new Color(0.38f, 0.02f, 0.05f, 0f);

        if (rescued)
        {
            AudioManager.Instance?.RestoreSceneMusic();
            AudioManager.Play(Sfx.ClarifyReveal, -6f, 0.9f);
        }
        // Not rescued: either the shatter sequence owns the audio now, or a
        // scene change already picked the next room's score.
    }

    private void BuildCorruptionOverlay()
    {
        _corruptionRect = new ColorRect
        {
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _corruptionRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var shader = GD.Load<Shader>(CorruptionShaderPath);
        if (shader != null)
        {
            _corruptionMaterial = new ShaderMaterial { Shader = shader };
            _corruptionMaterial.SetShaderParameter("intensity", 0f);
            _corruptionMaterial.SetShaderParameter("frozen", 0f);
            _corruptionMaterial.SetShaderParameter("flashback", 0f);
            _corruptionRect.Material = _corruptionMaterial;
        }
        else
        {
            Logger.Warning($"GameUI: corruption shader missing at {CorruptionShaderPath}");
        }

        AddChild(_corruptionRect);
    }

    private void BuildHud()
    {
        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_hud);

        // Objective (top-left, parchment-dark chip — translucent over the room).
        var objectivePanel = MakeHudPanel();
        objectivePanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        objectivePanel.OffsetLeft = 16;
        objectivePanel.OffsetTop = 14;
        objectivePanel.OffsetRight = 560;
        objectivePanel.OffsetBottom = 74;
        objectivePanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _objectiveLabel = MakeLabel("", 21);
        _objectiveLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        objectivePanel.AddChild(_objectiveLabel);
        _hud.AddChild(objectivePanel);

        // Integrity + clarity (top-right) — translucent over the room.
        var statusPanel = MakeHudPanel();
        statusPanel.AnchorLeft = 1;
        statusPanel.AnchorRight = 1;
        statusPanel.OffsetLeft = -316;
        statusPanel.OffsetRight = -16;
        statusPanel.OffsetTop = 14;
        statusPanel.OffsetBottom = 132;
        statusPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        var statusBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        statusBox.AddThemeConstantOverride("separation", 5);
        _integrityLabel = MakeLabel("MEMORY — INTACT", 19);
        statusBox.AddChild(_integrityLabel);

        _integrityBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 1,
            CustomMinimumSize = new Vector2(236, 7),
            ShowPercentage = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var barBg = new StyleBoxFlat { BgColor = new Color(0.25f, 0.22f, 0.18f, 0.8f) };
        var barFill = new StyleBoxFlat { BgColor = new Color(0.82f, 0.71f, 0.5f, 0.95f) };
        _integrityBar.AddThemeStyleboxOverride("background", barBg);
        _integrityBar.AddThemeStyleboxOverride("fill", barFill);
        statusBox.AddChild(_integrityBar);

        var clarityRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        clarityRow.AddThemeConstantOverride("separation", 8);
        clarityRow.AddChild(MakeLabel("Clarity", 19));
        _clarityPips = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _clarityPips.AddThemeConstantOverride("separation", 5);
        for (int i = 0; i < Globals.MaxClarity; i++)
        {
            _clarityPips.AddChild(new ColorRect
            {
                CustomMinimumSize = new Vector2(13, 13),
                Color = new Color(0.95f, 0.87f, 0.65f, 0.95f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        clarityRow.AddChild(_clarityPips);
        statusBox.AddChild(clarityRow);

        _shardLabel = MakeLabel("", 19);
        statusBox.AddChild(_shardLabel);
        statusPanel.AddChild(statusBox);
        _hud.AddChild(statusPanel);

        // Toast (top-centre, under nothing else — dialogue owns the bottom).
        // Toast text floats over bright rooms; sit it on a dark chip so hints
        // stay readable against morning light.
        _toastPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var toastChip = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.52f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.3f),
        };
        toastChip.SetBorderWidthAll(1);
        toastChip.SetCornerRadiusAll(4);
        _toastPanel.AddThemeStyleboxOverride("panel", toastChip);
        _toastPanel.AnchorLeft = 0.5f;
        _toastPanel.AnchorRight = 0.5f;
        _toastPanel.OffsetLeft = -440;
        _toastPanel.OffsetRight = 440;
        _toastPanel.OffsetTop = 138;
        _toastPanel.OffsetBottom = 206;
        _hud.AddChild(_toastPanel);

        _toastLabel = MakeLabel("", 23, new Color(0.96f, 0.9f, 0.78f, 0.95f));
        _toastLabel.AnchorLeft = 0.5f;
        _toastLabel.AnchorRight = 0.5f;
        _toastLabel.OffsetLeft = -420;
        _toastLabel.OffsetRight = 420;
        _toastLabel.OffsetTop = 148;
        _toastLabel.OffsetBottom = 196;
        _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _toastLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _toastLabel.Visible = false;
        _toastLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _hud.AddChild(_toastLabel);

        // Controls reminder (bottom-left). Bare pale text washed out against
        // the bright rooms, so it sits on a slim dark chip like the toasts.
        var controlsChip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var controlsStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.52f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.3f),
        };
        controlsStyle.SetBorderWidthAll(1);
        controlsStyle.SetCornerRadiusAll(4);
        controlsStyle.ContentMarginLeft = 12;
        controlsStyle.ContentMarginRight = 12;
        controlsStyle.ContentMarginTop = 5;
        controlsStyle.ContentMarginBottom = 5;
        controlsChip.AddThemeStyleboxOverride("panel", controlsStyle);
        controlsChip.AnchorTop = 1;
        controlsChip.AnchorBottom = 1;
        controlsChip.OffsetLeft = 12;
        controlsChip.OffsetRight = 924;
        controlsChip.OffsetTop = -40;
        controlsChip.OffsetBottom = -8;
        var controls = MakeLabel("Tab — look      C — clarity      E/A/T/R — memory verbs      H — hint      B — bag      J — journal      Q — back      Esc — pause",
            16, new Color(0.93f, 0.89f, 0.82f, 0.92f));
        controls.MouseFilter = Control.MouseFilterEnum.Ignore;
        controlsChip.AddChild(controls);
        _hud.AddChild(controlsChip);
    }

    private void BuildPausePanel()
    {
        _pausePanel = new Control { Visible = false };
        _pausePanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pausePanel.AddChild(DimBackdrop());

        var center = new CenterContainer { Name = "Center" };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pausePanel.AddChild(center);

        var panel = MakePanel();
        panel.Name = "Panel";
        panel.CustomMinimumSize = new Vector2(380, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer { Name = "VBox" };
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        var title = MakeLabel("The house holds its breath.", 30);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);
        vbox.AddChild(new HSeparator());

        var resume = MakeButton("Resume");
        resume.Name = "ResumeButton";
        resume.Pressed += ResumeGame;
        vbox.AddChild(resume);

        var journal = MakeButton("Memory Journal");
        journal.Pressed += () => OpenJournal(fromPause: true);
        vbox.AddChild(journal);

        var bag = MakeButton("Bag / Inventory");
        bag.Pressed += () => OpenBag(fromPause: true);
        vbox.AddChild(bag);

        var howTo = MakeButton("How to Play");
        howTo.Pressed += () => OpenTutorial(fromPause: true);
        vbox.AddChild(howTo);

        var settings = MakeButton("Settings");
        settings.Pressed += OpenSettings;
        vbox.AddChild(settings);

        var save = MakeButton("Preserve Memory (Save)");
        save.Pressed += () =>
        {
            GameState.Instance?.SaveGame("manual");
            ShowToast("Memory preserved.");
        };
        vbox.AddChild(save);

        var quit = MakeButton("Leave to Main Menu");
        quit.Pressed += QuitToMenu;
        vbox.AddChild(quit);

        AddChild(_pausePanel);
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel = new Control { Visible = false };
        _settingsPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _settingsPanel.AddChild(DimBackdrop());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _settingsPanel.AddChild(center);

        var panel = MakePanel();
        panel.CustomMinimumSize = new Vector2(540, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 9);
        panel.AddChild(vbox);

        var title = MakeLabel("Settings", 32);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);
        vbox.AddChild(new HSeparator());

        AddVolumeSlider(vbox, "Master", () => Settings.Instance.MasterVolume, v => Settings.Instance.MasterVolume = v);
        AddVolumeSlider(vbox, "Music", () => Settings.Instance.MusicVolume, v => Settings.Instance.MusicVolume = v);
        AddVolumeSlider(vbox, "Sounds", () => Settings.Instance.SfxVolume, v => Settings.Instance.SfxVolume = v);
        AddVolumeSlider(vbox, "Ambience", () => Settings.Instance.AmbienceVolume, v => Settings.Instance.AmbienceVolume = v);

        vbox.AddChild(new HSeparator());

        AddSliderRow(vbox, "Text speed", 0, 160, 5,
            () => Settings.Instance.TextSpeedCps,
            v => Settings.Instance.TextSpeedCps = v,
            v => v <= 0 ? "instant" : $"{(int)v} chars/s");
        AddToggle(vbox, "Auto-advance dialogue",
            () => Settings.Instance.AutoAdvanceDialogue,
            v => Settings.Instance.AutoAdvanceDialogue = v);

        vbox.AddChild(new HSeparator());

        AddToggle(vbox, "Fullscreen",
            () => Settings.Instance.Fullscreen,
            v => Settings.Instance.Fullscreen = v);
        AddToggle(vbox, "Reduce motion / flashing",
            () => Settings.Instance.ReduceMotion,
            v => Settings.Instance.ReduceMotion = v);
        AddSliderRow(vbox, "Corruption effects", 0, 1, 0.05,
            () => Settings.Instance.CorruptionIntensity,
            v => Settings.Instance.CorruptionIntensity = v,
            v => $"{(int)(v * 100)}%");
        AddToggle(vbox, "Hotspot reveal (hold Tab)",
            () => Settings.Instance.HotspotHints,
            v => Settings.Instance.HotspotHints = v);

        vbox.AddChild(new HSeparator());
        var close = MakeButton("Done");
        close.Pressed += () => CloseSettings(backToPause: _openedSettingsFromPause);
        vbox.AddChild(close);

        AddChild(_settingsPanel);
    }

    private void AddVolumeSlider(Container parent, string label, Func<float> get, Action<float> set)
    {
        AddSliderRow(parent, label, 0, 1, 0.01, get, set, v => $"{(int)(v * 100)}%");
    }

    private void AddSliderRow(Container parent, string label, double min, double max, double step,
        Func<float> get, Action<float> set, Func<double, string> format)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var l = MakeLabel(label, 21);
        l.CustomMinimumSize = new Vector2(190, 0);
        row.AddChild(l);

        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        slider.CustomMinimumSize = new Vector2(200, 0);
        var valueLabel = MakeLabel("", 19);
        valueLabel.CustomMinimumSize = new Vector2(90, 0);

        slider.ValueChanged += v =>
        {
            if (Settings.Instance == null)
                return;
            set((float)v);
            slider.SetMeta("refresh", v);
            valueLabel.Text = format(v);
            Settings.Instance.NotifyChanged();
        };

        if (Settings.Instance != null)
        {
            slider.Value = get();
            slider.SetMeta("refresh", (double)get());
            valueLabel.Text = format(slider.Value);
        }

        row.AddChild(slider);
        row.AddChild(valueLabel);
        _settingsRefreshers.Add((slider, null));
    }

    private void AddToggle(Container parent, string label, Func<bool> get, Action<bool> set)
    {
        var check = new CheckBox { Text = label };
        if (_font != null)
            check.AddThemeFontOverride("font", _font);
        check.AddThemeFontSizeOverride("font_size", 21);
        check.AddThemeColorOverride("font_color", new Color(0.93f, 0.89f, 0.82f));

        if (Settings.Instance != null)
            check.ButtonPressed = get();

        check.Toggled += v =>
        {
            if (Settings.Instance == null)
                return;
            set(v);
            Settings.Instance.NotifyChanged();
            AudioManager.Play(Sfx.UiClick, -8f);
        };
        parent.AddChild(check);
    }

    private void BuildJournalPanel()
    {
        _journalPanel = new Control { Visible = false };
        _journalPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _journalPanel.AddChild(DimBackdrop());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _journalPanel.AddChild(center);

        var panel = MakePanel();
        panel.CustomMinimumSize = new Vector2(880, 520);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        var title = MakeLabel("Memory Journal", 34);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        _journalStats = MakeLabel("", 18, new Color(0.8f, 0.75f, 0.68f, 0.85f));
        _journalStats.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_journalStats);
        vbox.AddChild(new HSeparator());

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        var fragTab = MakeButton("Fragments", 22);
        fragTab.Pressed += () => { _journalTab = 0; RefreshJournal(); };
        var echoTab = MakeButton("Echoes (recent words)", 22);
        echoTab.Pressed += () => { _journalTab = 1; RefreshJournal(); };
        var notesTab = MakeButton("Notes & Clues", 22);
        notesTab.Pressed += () => { _journalTab = 2; RefreshJournal(); };
        tabs.AddChild(fragTab);
        tabs.AddChild(echoTab);
        tabs.AddChild(notesTab);
        vbox.AddChild(tabs);

        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 16);
        split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(split);

        _journalList = new VBoxContainer { CustomMinimumSize = new Vector2(290, 0) };
        _journalList.AddThemeConstantOverride("separation", 6);
        split.AddChild(_journalList);

        var rightCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rightCol.AddThemeConstantOverride("separation", 8);
        split.AddChild(rightCol);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        rightCol.AddChild(scroll);

        _journalBody = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        if (_font != null)
        {
            _journalBody.AddThemeFontOverride("normal_font", _font);
            _journalBody.AddThemeFontOverride("bold_font", _font);
            _journalBody.AddThemeFontOverride("italics_font", _font);
        }
        _journalBody.AddThemeFontSizeOverride("normal_font_size", 21);
        _journalBody.AddThemeColorOverride("default_color", new Color(0.93f, 0.89f, 0.82f));
        scroll.AddChild(_journalBody);

        // The player's own notes page (Notes & Clues tab only).
        _notesBox = new VBoxContainer { Visible = false };
        _notesBox.AddThemeConstantOverride("separation", 6);
        _notesBox.AddChild(new HSeparator());
        _notesBox.AddChild(MakeLabel("Your notes — kept with this memory", 18, new Color(0.85f, 0.8f, 0.7f, 0.85f)));
        _notesEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, 110),
            PlaceholderText = "Write what you want to remember — numbers, names, suspicions…",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        if (_font != null)
            _notesEdit.AddThemeFontOverride("font", _font);
        _notesEdit.AddThemeFontSizeOverride("font_size", 20);
        _notesEdit.AddThemeColorOverride("font_color", new Color(0.95f, 0.91f, 0.83f));
        _notesEdit.AddThemeColorOverride("font_placeholder_color", new Color(0.75f, 0.7f, 0.62f, 0.6f));
        var notesBg = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.09f, 0.075f, 0.85f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.35f),
        };
        notesBg.SetBorderWidthAll(1);
        notesBg.SetCornerRadiusAll(4);
        notesBg.SetContentMarginAll(10);
        _notesEdit.AddThemeStyleboxOverride("normal", notesBg);
        _notesEdit.AddThemeStyleboxOverride("focus", notesBg);
        _notesEdit.TextChanged += () => _notesDirty = true;
        _notesBox.AddChild(_notesEdit);
        rightCol.AddChild(_notesBox);

        vbox.AddChild(new HSeparator());
        var close = MakeButton("Close the journal");
        close.Pressed += () =>
        {
            bool back = _openedJournalFromPause;
            CloseJournal(backToPause: back);
            if (back)
            {
                _pausePanel.Visible = true;
                _openedJournalFromPause = false;
            }
        };
        vbox.AddChild(close);

        AddChild(_journalPanel);
    }

    private void BuildMenuButton()
    {
        var b = MakeButton("Settings", 22);
        b.AnchorLeft = 1;
        b.AnchorTop = 1;
        b.AnchorRight = 1;
        b.AnchorBottom = 1;
        b.OffsetLeft = -170;
        b.OffsetTop = -62;
        b.OffsetRight = -26;
        b.OffsetBottom = -22;
        b.Visible = false;
        b.Pressed += OpenSettings;
        _menuSettingsButton = b;
        AddChild(b);

        var howTo = MakeButton("How to Play", 22);
        howTo.AnchorLeft = 1;
        howTo.AnchorTop = 1;
        howTo.AnchorRight = 1;
        howTo.AnchorBottom = 1;
        howTo.OffsetLeft = -170;
        howTo.OffsetTop = -108;
        howTo.OffsetRight = -26;
        howTo.OffsetBottom = -68;
        howTo.Visible = false;
        howTo.Pressed += () => OpenTutorial();
        _menuHowToButton = howTo;
        AddChild(howTo);
    }

    // ================================================================ tutorial

    private static readonly (string Title, string Body, string Visual)[] TutorialPages =
    [
        ("The House",
         "Mara's childhood home, as she remembers it — which is not the same as how it was.\n\nFace everything a room is holding, and its locked place opens.",
         "house"),
        ("Look Deeper — Gently",
         "Looking again looks deeper, and deeper looks fray the memory. If the MEMORY meter ever empties, the memory shatters — that is an ending, the hollow one.",
         "meter"),
        ("Clarity",
         "Clarity shows a thing as it truly is, and steadies the memory a little. Spend the LAST charge in an unfinished room and the house starts taking it back: fifteen seconds, in red.\n\nIn a room you nearly know, you may instead Remember it all: spend everything, and everything left shows itself.",
         "clarity"),
        ("Memory Verbs",
         "Four ways of remembering, learned one fragment at a time and written down in Mara's Journal when they wake. Their grammar never changes: choose the verb, then touch the memory.",
         "verbs"),
        ("Finding Things",
         "The house notices being noticed. If you are stuck, it would rather nudge than tell — and finished rooms behind you go quiet, so the way forward is the Return button.",
         "finding"),
        ("What You Are Doing Here",
         "Recover the six fragments of what actually happened, and return them in the last room. Nothing there is labelled.\n\nSome of what the house shows you is a kindness. Your work is to tell the difference.",
         "fragments"),
    ];

    private Control _tutorialPanel;
    private Label _tutorialTitle;
    private HBoxContainer _tutorialVisual;
    private Label _tutorialBody;
    private Label _tutorialPageLabel;
    private Button _tutorialNextButton;
    private Button _tutorialPrevButton;
    private Control _menuHowToButton;
    private int _tutorialIndex;
    private bool _tutorialFromPause;
    private bool _tutorialAutoShown;

    private void BuildTutorialPanel()
    {
        _tutorialPanel = new Control { Visible = false };
        _tutorialPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _tutorialPanel.AddChild(DimBackdrop());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _tutorialPanel.AddChild(center);

        var panel = MakePanel();
        panel.CustomMinimumSize = new Vector2(760, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        _tutorialTitle = MakeLabel("", 32);
        _tutorialTitle.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_tutorialTitle);
        vbox.AddChild(new HSeparator());

        // The picture teaches first; the words underneath only caption it.
        _tutorialVisual = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            CustomMinimumSize = new Vector2(680, 208),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _tutorialVisual.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(_tutorialVisual);

        _tutorialBody = MakeLabel("", 20);
        _tutorialBody.AutowrapMode = TextServer.AutowrapMode.Word;
        _tutorialBody.CustomMinimumSize = new Vector2(680, 96);
        vbox.AddChild(_tutorialBody);

        _tutorialPageLabel = MakeLabel("", 17, new Color(0.75f, 0.7f, 0.62f, 0.8f));
        _tutorialPageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_tutorialPageLabel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(row);

        _tutorialPrevButton = MakeButton("Back", 22);
        _tutorialPrevButton.Pressed += () => ShowTutorialPage(_tutorialIndex - 1);
        row.AddChild(_tutorialPrevButton);

        _tutorialNextButton = MakeButton("Next", 22);
        _tutorialNextButton.Pressed += () =>
        {
            if (_tutorialIndex >= TutorialPages.Length - 1)
                CloseTutorial();
            else
                ShowTutorialPage(_tutorialIndex + 1);
        };
        row.AddChild(_tutorialNextButton);

        var skip = MakeButton("Skip", 22);
        skip.Pressed += CloseTutorial;
        row.AddChild(skip);

        AddChild(_tutorialPanel);
    }

    public void OpenTutorial(bool fromPause = false)
    {
        _tutorialFromPause = fromPause;
        if (fromPause)
            _pausePanel.Visible = false;
        else if (_context == UiContext.Game)
            GetTree().Paused = true;

        _tutorialPanel.Visible = true;
        AudioManager.Play(Sfx.PageTurn, -7f);
        ShowTutorialPage(0);
        _tutorialNextButton.GrabFocus();
    }

    private void CloseTutorial()
    {
        _tutorialPanel.Visible = false;
        AudioManager.Play(Sfx.UiBack, -6f);

        if (Settings.Instance != null && !Settings.Instance.TutorialSeen)
        {
            Settings.Instance.TutorialSeen = true;
            Settings.Instance.Save();
        }

        if (_tutorialFromPause)
        {
            _pausePanel.Visible = true;
            _tutorialFromPause = false;
        }
        else if (_context == UiContext.Game)
        {
            GetTree().Paused = false;
        }
    }

    private void ShowTutorialPage(int index)
    {
        _tutorialIndex = Mathf.Clamp(index, 0, TutorialPages.Length - 1);
        var (title, body, visual) = TutorialPages[_tutorialIndex];
        _tutorialTitle.Text = title;
        _tutorialBody.Text = body;
        BuildTutorialVisual(visual);
        _tutorialPageLabel.Text = $"{_tutorialIndex + 1} / {TutorialPages.Length}";
        _tutorialPrevButton.Disabled = _tutorialIndex == 0;
        _tutorialNextButton.Text = _tutorialIndex == TutorialPages.Length - 1 ? "Begin" : "Next";
    }

    // ------------------------------------------------------- tutorial visuals

    private Control TutorialImage(string path, Vector2 size, bool cover)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
            return new ColorRect { CustomMinimumSize = size, Color = new Color(0.1f, 0.09f, 0.07f), MouseFilter = Control.MouseFilterEnum.Ignore };
        return new TextureRect
        {
            Texture = tex,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            CustomMinimumSize = size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = cover
                ? TextureRect.StretchModeEnum.KeepAspectCovered
                : TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    private Control TutorialChip(string text, Color border, Color? bg = null, int fontSize = 19)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = bg ?? new Color(0.07f, 0.06f, 0.05f, 0.9f),
            BorderColor = border,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = 12;
        style.ContentMarginRight = 12;
        style.ContentMarginTop = 7;
        style.ContentMarginBottom = 7;
        chip.AddThemeStyleboxOverride("panel", style);
        var label = MakeLabel(text, fontSize);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(label);
        return chip;
    }

    private static VBoxContainer TutorialColumn(int separation = 10)
    {
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", separation);
        return col;
    }

    private void BuildTutorialVisual(string kind)
    {
        if (_tutorialVisual == null)
            return;
        foreach (var child in _tutorialVisual.GetChildren())
        {
            _tutorialVisual.RemoveChild(child);
            child.QueueFree();
        }

        var gold = new Color(0.85f, 0.7f, 0.42f, 0.85f);
        switch (kind)
        {
            case "house":
            {
                _tutorialVisual.AddChild(TutorialImage("res://assets/images/bgs/Room_1_BG.png", new Vector2(400, 205), cover: true));
                var col = TutorialColumn();
                col.AddChild(TutorialChip("CLICK — look closer", gold));
                col.AddChild(TutorialChip("look again — deeper, at a cost", gold));
                col.AddChild(TutorialChip("Tab — what still waits", gold));
                _tutorialVisual.AddChild(col);
                break;
            }

            case "meter":
            {
                var cracks = new ShatterCrackOverlay
                {
                    CustomMinimumSize = new Vector2(300, 200),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                _tutorialVisual.AddChild(cracks);
                cracks.SetFracture(0.55f);
                var col = TutorialColumn();
                col.AddChild(MakeLabel("MEMORY", 20, new Color(0.85f, 0.79f, 0.67f)));
                col.AddChild(TutorialChip("Intact", new Color(0.82f, 0.71f, 0.5f, 0.9f), new Color(0.3f, 0.25f, 0.14f, 0.9f)));
                col.AddChild(TutorialChip("Strained", new Color(0.8f, 0.62f, 0.38f, 0.9f), new Color(0.32f, 0.2f, 0.1f, 0.9f)));
                col.AddChild(TutorialChip("Fractured", new Color(0.78f, 0.45f, 0.3f, 0.9f), new Color(0.3f, 0.14f, 0.08f, 0.9f)));
                col.AddChild(TutorialChip("Shattered", new Color(0.75f, 0.25f, 0.22f, 0.95f), new Color(0.28f, 0.06f, 0.05f, 0.95f)));
                _tutorialVisual.AddChild(col);
                break;
            }

            case "clarity":
            {
                _tutorialVisual.AddChild(TutorialImage(ClaritySystem.EyeCursorPath, new Vector2(150, 150), cover: false));
                var col = TutorialColumn();
                var pips = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                pips.AddThemeConstantOverride("separation", 6);
                for (int i = 0; i < Globals.MaxClarity; i++)
                {
                    pips.AddChild(new ColorRect
                    {
                        CustomMinimumSize = new Vector2(18, 18),
                        Color = i < Globals.MaxClarity - 1
                            ? new Color(0.95f, 0.87f, 0.65f, 0.95f)
                            : new Color(0.4f, 0.36f, 0.3f, 0.5f),
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    });
                }
                col.AddChild(MakeLabel("C — Clarity, and its charges", 19));
                col.AddChild(pips);
                col.AddChild(TutorialChip("0:15 — spend the last one carelessly",
                    new Color(0.85f, 0.25f, 0.22f, 0.95f), new Color(0.3f, 0.05f, 0.06f, 0.92f)));
                col.AddChild(TutorialChip("\u2726 Remember it all — spend everything, see everything",
                    new Color(1f, 0.82f, 0.45f, 0.9f), new Color(0.2f, 0.14f, 0.05f, 0.92f)));
                _tutorialVisual.AddChild(col);
                break;
            }

            case "verbs":
            {
                var col = TutorialColumn(14);
                var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                row.AddThemeConstantOverride("separation", 10);
                row.AddChild(TutorialChip("E · Echo\nlisten", gold));
                row.AddChild(TutorialChip("A · Anchor\nhold still", gold));
                row.AddChild(TutorialChip("T · Trace\njoin", gold));
                row.AddChild(TutorialChip("R · Release\ndecide", gold));
                col.AddChild(row);
                var arrow = MakeLabel("choose the verb  \u2192  touch the memory      ·      right-click puts it down", 19,
                    new Color(0.88f, 0.83f, 0.72f, 0.9f));
                arrow.HorizontalAlignment = HorizontalAlignment.Center;
                col.AddChild(arrow);
                _tutorialVisual.AddChild(col);
                break;
            }

            case "finding":
            {
                _tutorialVisual.AddChild(TutorialImage("res://assets/images/core/ui/bag_icon.png", new Vector2(150, 150), cover: false));
                var col = TutorialColumn();
                col.AddChild(TutorialChip("Tab — what still waits    ·    H — a nudge", gold));
                col.AddChild(TutorialChip("B — Mara's bag    ·    J — her journal", gold));
                col.AddChild(TutorialChip("Q — step back    ·    Return \u2192 — the new room", gold));
                col.AddChild(TutorialChip("click — dialogue    ·    Esc — pause", gold));
                _tutorialVisual.AddChild(col);
                break;
            }

            default: // "fragments"
            {
                foreach (var path in BagIconPaths)
                    _tutorialVisual.AddChild(TutorialImage(path, new Vector2(96, 120), cover: false));
                break;
            }
        }
    }

    /// <summary>First ever entry into Room 1: teach, once. Replayable any time
    /// from the pause menu / main menu ("How to Play").</summary>
    // ======================================================= memory cutscene

    /// <summary>One still + caption. ImagePath "" = black screen, text only.
    /// The morning Mara wakes into, told with the game's own room art; the
    /// last card states the mission plainly so the objective is never in doubt.
    /// This post-Start introduction is player-paced: each shot remains until
    /// the player clicks, so HoldSeconds only informs the slow zoom here.</summary>
    private static readonly (string ImagePath, string Caption, float HoldSeconds)[] CutsceneShots =
    [
        ("",
         "Mara Voss is thirty-four years old, and she has not truly slept in months.", 4.5f),
        ("res://assets/images/bgs/Room_1_BG.png",
         "So tonight her mind does what it has done since she was twenty-two: it takes her back to Eldfen — to the childhood house, kept exactly as warm as she needs it to be.", 7f),
        ("res://assets/images/bgs/Room2_no_objects.png",
         "Almost exactly. In the true house there was always someone else in the next room. Here, every chair is empty. It has been empty for twelve years.", 6.5f),
        ("res://assets/images/bgs/Room_3_BG.png",
         "Her sister Ida died at fourteen, on an October afternoon when Mara was supposed to come for her at four o'clock — and forgot. That is the sentence this whole house was built to keep Mara from saying.", 8f),
        ("res://assets/images/bgs/BgRoom4.png",
         "Seven rooms. Six fragments of what actually happened. Some of what they show her is memory. Some of it is mercy, arranged nightly by a mind that loves her.", 6.5f),
        ("res://assets/images/bgs/Room_6_Final.png",
         "You are the part of Mara that still knows the difference. Walk the rooms. Face what each one is holding. Do not let the house be kind at the truth's expense.", 7f),
        ("res://assets/images/bgs/DoorCloseup.png",
         "Recover the six fragments. Put that day back in its order. And let Mara Voss wake up.", 6f),
    ];

    // =========================================================== recollection

    private void BuildRecollectionPanel()
    {
        _recollectionPanel = new Control { Visible = false };
        _recollectionPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _recollectionPanel.AddChild(DimBackdrop());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _recollectionPanel.AddChild(center);

        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.045f, 0.036f, 0.97f),
            BorderColor = new Color(0.78f, 0.65f, 0.43f, 0.78f),
            ShadowColor = new Color(0f, 0f, 0f, 0.55f),
            ShadowSize = 18,
        };
        panelStyle.SetBorderWidthAll(1);
        panelStyle.SetCornerRadiusAll(5);
        panelStyle.ContentMarginLeft = 36;
        panelStyle.ContentMarginRight = 36;
        panelStyle.ContentMarginTop = 28;
        panelStyle.ContentMarginBottom = 28;

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(688, 0) };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 13);
        panel.AddChild(box);

        box.AddChild(MakeLabel("CLARITY  ·  RECOLLECTION", 17, new Color(0.78f, 0.65f, 0.43f, 0.95f)));
        var title = MakeLabel("This room is almost known to you.", 36, new Color(0.97f, 0.92f, 0.82f));
        box.AddChild(title);

        var rule = new HSeparator();
        var ruleStyle = new StyleBoxLine { Color = new Color(0.78f, 0.65f, 0.43f, 0.45f), Thickness = 1 };
        rule.AddThemeStyleboxOverride("separator", ruleStyle);
        box.AddChild(rule);

        _recollectionBody = MakeLabel(string.Empty, 23, new Color(0.93f, 0.89f, 0.82f, 0.97f));
        _recollectionBody.AutowrapMode = TextServer.AutowrapMode.Word;
        _recollectionBody.CustomMinimumSize = new Vector2(612, 0);
        box.AddChild(_recollectionBody);

        _recollectionHeldLabel = MakeLabel(string.Empty, 19, new Color(0.84f, 0.79f, 0.70f, 0.95f));
        box.AddChild(_recollectionHeldLabel);
        _recollectionBeforePips = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _recollectionBeforePips.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < Globals.MaxClarity; i++)
        {
            _recollectionBeforePips.AddChild(new ColorRect
            {
                CustomMinimumSize = new Vector2(17, 13),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        box.AddChild(_recollectionBeforePips);

        _recollectionNextLabel = MakeLabel(string.Empty, 19, new Color(0.74f, 0.86f, 1f, 0.95f));
        box.AddChild(_recollectionNextLabel);
        _recollectionAfterPips = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _recollectionAfterPips.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < Globals.MaxClarity; i++)
        {
            _recollectionAfterPips.AddChild(new ColorRect
            {
                CustomMinimumSize = new Vector2(17, 13),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        box.AddChild(_recollectionAfterPips);

        _recollectionCost = MakeLabel(string.Empty, 21, new Color(0.74f, 0.86f, 1f, 0.96f));
        _recollectionCost.AutowrapMode = TextServer.AutowrapMode.Word;
        box.AddChild(_recollectionCost);

        var choices = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        choices.AddThemeConstantOverride("separation", 18);
        _recollectionConfirmButton = MakeButton("Remember it all", 25);
        _recollectionConfirmButton.CustomMinimumSize = new Vector2(226, 52);
        _recollectionConfirmButton.Pressed += OnRecollectionConfirmed;
        _recollectionCancelButton = MakeButton("One at a time", 25);
        _recollectionCancelButton.CustomMinimumSize = new Vector2(226, 52);
        _recollectionCancelButton.Pressed += CloseRecollectionPanel;
        choices.AddChild(_recollectionConfirmButton);
        choices.AddChild(_recollectionCancelButton);
        box.AddChild(choices);

        var hint = MakeLabel("Esc or right-click keeps Clarity armed for one careful look.", 17,
            new Color(0.76f, 0.72f, 0.65f, 0.78f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(hint);

        AddChild(_recollectionPanel);
    }

    private void ShowRecollectionPanel(int charges, int remainingCount, int nextRoomClarity, Action onConfirm)
    {
        _recollectionBody.Text =
            $"Spend all {charges} Clarity, and let the room remember itself. " +
            $"The {remainingCount} thing(s) still hidden will take their true forms now, one by one. " +
            "A blue ring will stay around each until you touch it and hear what it remembers.";
        _recollectionHeldLabel.Text = $"Held now — {charges}";
        _recollectionNextLabel.Text = $"In the next room — {nextRoomClarity}";
        _recollectionCost.Text = "Here, remembering will spend every charge you hold — and emptied light invites the house. " +
                                 "If this room's lock is still shut, the red count starts the moment the revealing ends.";

        for (int i = 0; i < _recollectionBeforePips.GetChildCount(); i++)
        {
            if (_recollectionBeforePips.GetChild(i) is ColorRect pip)
                pip.Color = i < charges
                    ? new Color(0.95f, 0.87f, 0.65f, 0.98f)
                    : new Color(0.38f, 0.34f, 0.29f, 0.55f);
        }
        for (int i = 0; i < _recollectionAfterPips.GetChildCount(); i++)
        {
            if (_recollectionAfterPips.GetChild(i) is ColorRect pip)
                pip.Color = i < nextRoomClarity
                    ? new Color(0.62f, 0.82f, 1f, 0.98f)
                    : new Color(0.30f, 0.36f, 0.45f, 0.55f);
        }

        _recollectionConfirmAction = onConfirm;
        _recollectionPanel.Visible = true;
        _recollectionCancelButton.GrabFocus();
        AudioManager.Play(Sfx.PageTurn, -10f);
    }

    private void OnRecollectionConfirmed()
    {
        var action = _recollectionConfirmAction;
        _recollectionConfirmAction = null;
        _recollectionPanel.Visible = false;
        action?.Invoke();
    }

    private void CloseRecollectionPanel()
    {
        _recollectionConfirmAction = null;
        if (_recollectionPanel != null)
            _recollectionPanel.Visible = false;
    }

    private async void PlayRecollectionVisualAsync(List<string> objectNames)
    {
        if (_recollectionVisualPlaying)
            return;

        _recollectionVisualPlaying = true;
        Dialogue.BeginInputBlock();
        try
        {
            AudioManager.Play(Sfx.ClarifyReveal, -5f);
            foreach (string objectName in objectNames)
            {
                if (_shatterPlaying)
                    break;

                var layer = Hotspots.FindLayerForObject(objectName);
                if (layer == null)
                    continue;

                Vector2 center = Hotspots.GetLayerCenter(layer);
                Hotspots.SpawnRipple(center, new Color(0.62f, 0.84f, 1f, 0.98f));
                layer.PlayRecollectionCue();
                await ShatterTimer(0.24f);

                var clarified = ExamineHelper.ApplyClarifyVisual(layer);
                if (clarified != null)
                {
                    PulseClarifiedLayer(clarified);
                    Hotspots.SpawnRipple(center, new Color(0.95f, 0.87f, 0.65f, 0.95f));
                }

                Hotspots.RefreshPersistent();
                await ShatterTimer(0.42f);
            }
        }
        finally
        {
            Dialogue.EndInputBlock();
            _recollectionVisualPlaying = false;
            ClaritySystem.RefreshToggleTiles();
            RefreshHud();
            Hotspots.RefreshPersistent();
            GameState.Instance?.Toast(objectNames.Count == 1
                ? "One last thing has shown its true form. All Clarity spent."
                : $"The room has remembered its remaining {objectNames.Count} things. All Clarity spent.");
        }
    }

    private void PulseClarifiedLayer(TileMapLayer layer)
    {
        if (layer == null || !GodotObject.IsInstanceValid(layer))
            return;

        var modulate = layer.Modulate;
        modulate.A = 0.15f;
        layer.Modulate = modulate;
        var selfModulate = layer.SelfModulate;
        selfModulate.A = 0.15f;
        layer.SelfModulate = selfModulate;

        var tween = CreateTween();
        if (tween == null)
            return;
        tween.TweenProperty(layer, "modulate:a", 1f, 0.48f);
        tween.Parallel().TweenProperty(layer, "self_modulate:a", 1f, 0.48f);
    }

    private void MaybeOfferRecollection()
    {
        var gs = GameState.Instance;
        if (gs == null || _context != UiContext.Game || _shatterPlaying || _cutscenePlaying
            || _recollectionVisualPlaying || Dialogue.IsShowing || AnyPanelOpen())
            return;
        if (!gs.CanRecollect(out var remaining, out int carry))
            return;

        _recollectionOfferedThisArming = true;
        int charges = gs.ClarityCount;
        var remembered = new List<string>(remaining);
        ShowRecollectionPanel(charges, remembered.Count, carry, () =>
        {
            if (gs.PerformRecollection(announce: false) > 0)
            {
                ClaritySystem.Disarm(silent: true);
                ClaritySystem.RefreshToggleTiles();
                RefreshHud();
                PlayRecollectionVisualAsync(remembered);
            }
        });
    }

    // ============================================================ bad ending

    private static readonly string[] ShatterCards =
    [
        "You looked, and looked, until looking was all that was left.",
        "A memory can be handled only so many times before it handles you.",
        "The rooms fold inward. The light goes out of the kitchen first.",
        "Somewhere, in an apartment whose door never opens, Mara Voss sits down in the dark — and does not get up.",
    ];

    private void BuildShatterOverlay()
    {
        _shatterRoot = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _shatterRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_shatterRoot);

        _shatterBlack = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _shatterBlack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _shatterRoot.AddChild(_shatterBlack);

        _shatterCracks = new ShatterCrackOverlay();
        _shatterCracks.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _shatterRoot.AddChild(_shatterCracks);

        _shatterTitle = MakeLabel(string.Empty, 54, new Color(0.85f, 0.72f, 0.55f));
        _shatterTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _shatterTitle.MouseFilter = Control.MouseFilterEnum.Ignore;
        _shatterTitle.AnchorLeft = 0.5f; _shatterTitle.AnchorRight = 0.5f;
        _shatterTitle.AnchorTop = 0.5f; _shatterTitle.AnchorBottom = 0.5f;
        _shatterTitle.OffsetLeft = -520; _shatterTitle.OffsetRight = 520;
        _shatterTitle.OffsetTop = -250; _shatterTitle.OffsetBottom = -170;
        _shatterRoot.AddChild(_shatterTitle);

        _shatterCaption = MakeLabel(string.Empty, 32, new Color(0.94f, 0.9f, 0.82f));
        _shatterCaption.AutowrapMode = TextServer.AutowrapMode.Word;
        _shatterCaption.HorizontalAlignment = HorizontalAlignment.Center;
        _shatterCaption.VerticalAlignment = VerticalAlignment.Center;
        _shatterCaption.MouseFilter = Control.MouseFilterEnum.Ignore;
        _shatterCaption.AnchorLeft = 0.5f; _shatterCaption.AnchorRight = 0.5f;
        _shatterCaption.AnchorTop = 0.5f; _shatterCaption.AnchorBottom = 0.5f;
        _shatterCaption.OffsetLeft = -520; _shatterCaption.OffsetRight = 520;
        _shatterCaption.OffsetTop = -150; _shatterCaption.OffsetBottom = 80;
        _shatterRoot.AddChild(_shatterCaption);

        _shatterChoices = new HBoxContainer { Visible = false, Alignment = BoxContainer.AlignmentMode.Center };
        _shatterChoices.AddThemeConstantOverride("separation", 28);
        _shatterChoices.AnchorLeft = 0.5f; _shatterChoices.AnchorRight = 0.5f;
        _shatterChoices.AnchorTop = 0.5f; _shatterChoices.AnchorBottom = 0.5f;
        _shatterChoices.OffsetLeft = -340; _shatterChoices.OffsetRight = 340;
        _shatterChoices.OffsetTop = 120; _shatterChoices.OffsetBottom = 180;
        var gather = MakeButton("Gather the pieces", 24);
        gather.Pressed += OnGatherPiecesPressed;
        var letGo = MakeButton("Let it go", 24);
        letGo.Pressed += OnLetGoPressed;
        _shatterChoices.AddChild(gather);
        _shatterChoices.AddChild(letGo);
        _shatterRoot.AddChild(_shatterChoices);

        _shatterRoot.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                _shatterAdvanceRequested = true;
        };
    }

    private async void OnMemoryShattered()
    {
        if (_shatterPlaying)
            return;
        _shatterPlaying = true;

        // The world comes apart first — still live behind the effect.
        Dialogue.NotifySkipPressed();
        MemoryVerbSystem.Cancel(silent: true);
        ClaritySystem.Disarm(silent: true);
        Hotspots.HideAll();
        _shatterRoot.Visible = true;
        _shatterChoices.Visible = false;
        _shatterCaption.Text = string.Empty;
        _shatterTitle.Text = string.Empty;
        SetShatterBlack(0f);
        if (_shatterCracks != null)
        {
            _shatterCracks.Modulate = Colors.White;
            _shatterCracks.ResetCracks();
        }

        AudioManager.Play(Sfx.CorruptionGlitch, -5f, 0.8f);
        AudioManager.Play(Sfx.ScreenCrack1, -6f);
        AudioManager.Instance?.SetCorruption01(1f);

        // The room tears itself apart in visible stages: shader corruption,
        // black flicker, and a screen crack that spreads with each sound.
        float[] fractureStages = [0.16f, 0.34f, 0.52f, 0.70f, 0.88f, 1f];
        for (int i = 0; i < fractureStages.Length; i++)
        {
            _shatterCracks?.SetFracture(fractureStages[i]);
            if (i == 1)
                AudioManager.Play(Sfx.ScreenCrack2, -6f, 0.94f);
            else if (i == 3)
                AudioManager.Play(Sfx.ScreenCrack2, -5f, 1.08f);
            else if (i == fractureStages.Length - 1)
            {
                AudioManager.Play(Sfx.ScreenCrack3, -4f);
                AudioManager.Play(Sfx.MemoryShatter, -3f);
            }

            _corruptionMaterial?.SetShaderParameter("frozen", i % 2 == 0 ? 1f : 0f);
            SetShatterBlack(i % 2 == 0 ? 0.28f : 0.06f);
            await ShatterTimer(0.4f);
        }

        // Then the dark takes it.
        AudioManager.Instance?.StopMusic(0.8f);
        AudioManager.Play(Sfx.CorruptionWhisper, -12f, 0.7f);
        var fade = CreateTween();
        fade.TweenMethod(Callable.From<float>(SetShatterBlack), 0.1f, 1f, 1.4f);
        await ToSignal(fade, Tween.SignalName.Finished);
        if (_shatterCracks != null)
            _shatterCracks.Modulate = new Color(1f, 1f, 1f, 0.48f);
        GetTree().Paused = true;
        AudioManager.Instance?.PlayMusic(AudioManager.MusicFinale, -12f, 2.5f);

        foreach (var card in ShatterCards)
        {
            _shatterCaption.Text = card;
            _shatterCaption.Modulate = new Color(1, 1, 1, 0);
            var t = CreateTween(); t.SetPauseMode(Tween.TweenPauseMode.Process);
            t.TweenProperty(_shatterCaption, "modulate:a", 1f, 1.1f);
            await ShatterWaitAdvance(5.5f);
            var t2 = CreateTween(); t2.SetPauseMode(Tween.TweenPauseMode.Process);
            t2.TweenProperty(_shatterCaption, "modulate:a", 0f, 0.7f);
            await ToSignal(t2, Tween.SignalName.Finished);
        }

        _shatterTitle.Text = "THE HOLLOW ENDING";
        _shatterCaption.Text = "The memory, shattered.";
        _shatterTitle.Modulate = new Color(1, 1, 1, 0);
        _shatterCaption.Modulate = new Color(1, 1, 1, 0);
        var t3 = CreateTween(); t3.SetPauseMode(Tween.TweenPauseMode.Process);
        t3.TweenProperty(_shatterTitle, "modulate:a", 1f, 1.6f);
        t3.Parallel().TweenProperty(_shatterCaption, "modulate:a", 0.9f, 1.6f);
        await ToSignal(t3, Tween.SignalName.Finished);
        await ShatterWaitAdvance(2.5f);

        Settings.Instance?.RecordEnding("hollow");
        _shatterChoices.Visible = true;
    }

    private void SetShatterBlack(float alpha)
    {
        if (_shatterBlack != null)
            _shatterBlack.Color = new Color(0, 0, 0, Mathf.Clamp(alpha, 0f, 1f));
    }

    private async System.Threading.Tasks.Task ShatterTimer(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
    }

    private async System.Threading.Tasks.Task ShatterWaitAdvance(float seconds)
    {
        _shatterAdvanceRequested = false;
        ulong deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        while (Time.GetTicksMsec() < deadline && !_shatterAdvanceRequested)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _shatterAdvanceRequested = false;
    }

    private void OnGatherPiecesPressed()
    {
        AudioManager.Play(Sfx.ClarifyReveal, -6f);
        GameState.Instance?.RecoverFromShatter();
        EndShatter(toMenu: false);
    }

    private void OnLetGoPressed()
    {
        // Recover first so a later Continue never loads into the ending.
        GameState.Instance?.RecoverFromShatter();
        EndShatter(toMenu: true);
    }

    private async void EndShatter(bool toMenu)
    {
        _shatterChoices.Visible = false;
        _shatterTitle.Text = string.Empty;
        _shatterCaption.Text = toMenu ? string.Empty : "Steady. Breathe. The house holds — barely.";
        GetTree().Paused = false;
        RefreshCorruptionShader();

        if (toMenu)
        {
            _shatterPlaying = false;
            _shatterRoot.Visible = false;
            _shatterCracks?.ResetCracks();
            QuitToMenu();
            return;
        }

        AudioManager.Instance?.PlayMusicForScene(SceneManager.CurrentScenePath);
        if (_shatterCracks != null)
            _shatterCracks.Modulate = Colors.White;
        var fade = CreateTween();
        fade.TweenMethod(Callable.From<float>(SetShatterBlack), 1f, 0f, 1.6f);
        fade.Parallel().TweenProperty(_shatterCaption, "modulate:a", 0f, 1.6f);
        if (_shatterCracks != null)
        {
            float fractureFrom = _shatterCracks.Fracture;
            fade.Parallel().TweenMethod(Callable.From<float>(_shatterCracks.SetFracture), fractureFrom, 0f, 1.6f);
        }
        await ToSignal(fade, Tween.SignalName.Finished);
        _shatterRoot.Visible = false;
        _shatterPlaying = false;
        RefreshHud();
        Hotspots.RefreshPersistent();
    }

    private void BuildEndingOverlay()
    {
        _endingRoot = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _endingRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_endingRoot);

        var black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _endingRoot.AddChild(black);

        _endingImage = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _endingImage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _endingRoot.AddChild(_endingImage);

        // The branch wash: light for waking, black for the kept house.
        _endingFade = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _endingFade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _endingRoot.AddChild(_endingFade);

        _endingTitle = MakeLabel(string.Empty, 54, new Color(0.9f, 0.78f, 0.56f));
        _endingTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _endingTitle.MouseFilter = Control.MouseFilterEnum.Ignore;
        _endingTitle.AnchorLeft = 0; _endingTitle.AnchorRight = 1;
        _endingTitle.OffsetLeft = 70; _endingTitle.OffsetRight = -70;
        _endingTitle.OffsetTop = 52; _endingTitle.OffsetBottom = 128;
        _endingRoot.AddChild(_endingTitle);

        var band = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.58f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorTop = 1,
            AnchorBottom = 1,
            AnchorRight = 1,
            OffsetTop = -218,
            OffsetBottom = 0,
        };
        _endingRoot.AddChild(band);

        _endingCaption = MakeLabel(string.Empty, 30, new Color(0.96f, 0.92f, 0.84f));
        _endingCaption.AutowrapMode = TextServer.AutowrapMode.Word;
        _endingCaption.HorizontalAlignment = HorizontalAlignment.Center;
        _endingCaption.VerticalAlignment = VerticalAlignment.Center;
        _endingCaption.MouseFilter = Control.MouseFilterEnum.Ignore;
        _endingCaption.AnchorLeft = 0; _endingCaption.AnchorRight = 1;
        _endingCaption.AnchorTop = 1; _endingCaption.AnchorBottom = 1;
        _endingCaption.OffsetLeft = 90; _endingCaption.OffsetRight = -90;
        _endingCaption.OffsetTop = -204; _endingCaption.OffsetBottom = -96;
        _endingRoot.AddChild(_endingCaption);

        _endingStats = MakeLabel(string.Empty, 17, new Color(0.9f, 0.86f, 0.78f, 0.72f));
        _endingStats.HorizontalAlignment = HorizontalAlignment.Center;
        _endingStats.AutowrapMode = TextServer.AutowrapMode.Word;
        _endingStats.MouseFilter = Control.MouseFilterEnum.Ignore;
        _endingStats.Visible = false;
        _endingStats.AnchorLeft = 0; _endingStats.AnchorRight = 1;
        _endingStats.AnchorTop = 1; _endingStats.AnchorBottom = 1;
        _endingStats.OffsetLeft = 70; _endingStats.OffsetRight = -70;
        _endingStats.OffsetTop = -94; _endingStats.OffsetBottom = -62;
        _endingRoot.AddChild(_endingStats);

        _endingReturnButton = MakeButton("Return to title", 21);
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.8f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.5f),
        };
        normal.SetBorderWidthAll(1); normal.SetCornerRadiusAll(4); normal.SetContentMarginAll(8);
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.75f, 0.55f, 0.18f),
            BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.6f),
        };
        hover.SetBorderWidthAll(1); hover.SetCornerRadiusAll(4); hover.SetContentMarginAll(8);
        _endingReturnButton.AddThemeStyleboxOverride("normal", normal);
        _endingReturnButton.AddThemeStyleboxOverride("hover", hover);
        _endingReturnButton.AddThemeStyleboxOverride("pressed", hover);
        _endingReturnButton.AddThemeStyleboxOverride("focus", hover);
        _endingReturnButton.Visible = false;
        _endingReturnButton.AnchorLeft = 0.5f; _endingReturnButton.AnchorRight = 0.5f;
        _endingReturnButton.AnchorTop = 1; _endingReturnButton.AnchorBottom = 1;
        _endingReturnButton.OffsetLeft = -150; _endingReturnButton.OffsetRight = 150;
        _endingReturnButton.OffsetTop = -56; _endingReturnButton.OffsetBottom = -14;
        _endingReturnButton.Pressed += OnEndingReturnPressed;
        _endingRoot.AddChild(_endingReturnButton);

        _endingRoot.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                _endingAdvanceRequested = true;
        };
    }

    private static (string ImagePath, string FinalStillPath, string MusicPath, string Title, string FinalCaption, string[] Cards) EndingData(string endingId)
    {
        return endingId switch
        {
            "kept_house" => (
                "res://assets/images/bgs/Ending_Kept_House.png",
                "res://assets/images/bgs/Ending_Mara_Curled.png",
                AudioManager.MusicEndingKept,
                "THE KEPT HOUSE",
                "Nothing changes. That was the choice.",
                new[]
                {
                    "Mara looks at the whole morning — every room, every name, every empty chair in its proper order.",
                    "Then, very gently, she closes the last door from the inside.",
                    "The kettle will always be warm here. The swing will never quite finish moving. No one will be asked to leave again.",
                    "Outside the memory, an apartment grows dark around a woman sitting perfectly still, smiling at a house only she can see.",
                }),
            _ => (
                "res://assets/images/bgs/Ending_Waking.png",
                "res://assets/images/bgs/Ending_Mara_Smile.png",
                AudioManager.MusicEndingRelease,
                "THE WAKING ENDING",
                "She does not stop grieving. She stops living inside the grief.",
                new[]
                {
                    "Mara opens her eyes on an ordinary ceiling she has not really seen in four months.",
                    "Ida is not in the kitchen. Ida will not be in the kitchen. The sentence lands all at once — and, this time, Mara does not build a house over it.",
                    "She finds the last unsent letter and writes no address on it. Some apologies are not messages. They are weights set down where they were carried.",
                    "Then she puts on her coat. The stairs are only stairs. The street is wet, and loud, and continuing without permission.",
                    "At the door she says her sister's name once — not to call her back. To take her with her, the way the living are supposed to carry the dead.",
                }),
        };
    }

    /// <summary>First-person frames + lines for the last conversation.
    /// Frames: 0 = listening, 1 = speaking, 2 = her last expression
    /// (a smile if Mara lets go; turned away if she does not).</summary>
    private static (string[] Frames, (int Frame, string Speaker, string Text)[] Lines) IdaConversation(string endingId)
    {
        if (endingId == "kept_house")
        {
            return (
                new[]
                {
                    "res://assets/images/bgs/Ending_Ida_Gloom.png",
                    "res://assets/images/bgs/Ending_Ida_Gloom_Speak.png",
                    "res://assets/images/bgs/Ending_Ida_Gloom_Away.png",
                },
                new (int, string, string)[]
                {
                    (0, "Mara", "Ida. Stay. Please — the house is right here. Everything is where you left it."),
                    (1, "Ida", "That's the problem. Nothing here is allowed to change. Not even me."),
                    (0, "Mara", "I kept it all for you. The jars. Your coat. If I let any of it go, then that day—"),
                    (1, "Ida", "That day happened whether you keep the coat or not. I'm not in the coat, Mara."),
                    (0, "Mara", "Then where are you? Tell me where to be sorry. Tell me how to fix it."),
                    (1, "Ida", "You can't fix it. You can only stop living in the fixing. I didn't wait all this time to watch you do this."),
                    (2, "Ida", "I'm tired of being the reason you won't wake up."),
                    (2, "Mara", "Ida—"),
                    (2, "", "She does not turn back. The rain takes the rest."),
                });
        }

        return (
            new[]
            {
                "res://assets/images/bgs/Ending_Ida_Warm_Listen.png",
                "res://assets/images/bgs/Ending_Ida_Warm_Speak.png",
                "res://assets/images/bgs/Ending_Ida_Warm.png",
            },
            new (int, string, string)[]
            {
                (0, "Mara", "Ida. Ida — look at me. I'm so sorry. I was supposed to come at four."),
                (1, "Ida", "I know what time you came. I've had twelve years of you telling me."),
                (0, "Mara", "Then you know. If I'd just— if I hadn't—"),
                (1, "Ida", "Mara. Listen to me properly, for once. It wasn't your fault."),
                (0, "Mara", "Don't. Don't be kind about it. I chose what I was doing. I remember choosing."),
                (1, "Ida", "And I chose too. I chose to stop waiting and walk. No one made me. That's mine."),
                (0, "Mara", "You were fourteen."),
                (1, "Ida", "And you were twenty-two, and late, and human. Late isn't the same as gone. You came."),
                (2, "Ida", "I let go a long time ago, Mara. I've just been waiting for you to notice I'm not the one holding on."),
                (2, "Mara", "...Okay. Okay. I'll carry you differently, then."),
            });
    }

    /// <summary>Play one of the two authored endings chosen in Room 7.
    /// Reaching its title records cross-run completion and deletes the run
    /// save; Return to title deliberately does not call QuitToMenu/SaveGame.</summary>
    public async void PlayAuthoredEnding(string endingId, bool withIdaConversation = false)
    {
        if (_endingPlaying || _endingRoot == null || (endingId != "waking" && endingId != "kept_house"))
            return;

        _endingPlaying = true;
        _endingSkipRequested = false;
        _endingAdvanceRequested = false;
        _endingId = endingId;

        MemoryVerbSystem.Cancel(silent: true);
        ClaritySystem.Disarm(silent: true);
        Hotspots.HideAll();
        _endingRoot.Visible = true;
        _endingReturnButton.Visible = false;
        _endingStats.Visible = false;
        _endingTitle.Text = string.Empty;
        _endingCaption.Text = string.Empty;
        _endingTitle.Modulate = new Color(1, 1, 1, 0);
        _endingCaption.Modulate = new Color(1, 1, 1, 0);
        if (_endingFade != null)
            _endingFade.Color = new Color(1, 1, 1, 0);
        GetTree().Paused = true;

        var (imagePath, finalStillPath, musicPath, title, finalCaption, cards) = EndingData(endingId);
        AudioManager.Instance?.StopAmbience();
        // The conversation carries its own weather: relief, or gloom. The
        // older direct path (no conversation) keeps the finale track.
        AudioManager.Instance?.PlayMusic(withIdaConversation ? musicPath : AudioManager.MusicFinale, -10f, 2.2f);

        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            string startImage = imagePath;
            (string[] Frames, (int Frame, string Speaker, string Text)[] Lines) convo = default;
            if (withIdaConversation)
            {
                convo = IdaConversation(endingId);
                startImage = convo.Frames[convo.Lines[0].Frame];
            }
            _endingImage.Texture = GD.Load<Texture2D>(startImage);
            _endingImage.PivotOffset = _endingImage.Size / 2f;
            _endingImage.Scale = Vector2.One * 1.03f;
            _endingImage.Modulate = new Color(1, 1, 1, 0);

            _endingTween?.Kill();
            _endingTween = CreateTween();
            _endingTween.SetPauseMode(Tween.TweenPauseMode.Process);
            _endingTween.TweenProperty(_endingImage, "modulate:a", 1f, 1.4f);
            _endingTween.Parallel().TweenProperty(_endingImage, "scale", Vector2.One * 1.08f, 18f);
            await WaitForEndingAdvance(1.2f);

            if (withIdaConversation)
            {
                // Talk to Ida — from inside Mara's own eyes. Her expression
                // changes as the conversation turns; a slow drift keeps the
                // frame breathing between the changes.
                _endingTween?.Kill();
                _endingTween = CreateTween();
                _endingTween.SetPauseMode(Tween.TweenPauseMode.Process);
                _endingTween.TweenProperty(_endingImage, "scale", Vector2.One * 1.1f, 45f);

                int currentFrame = convo.Lines[0].Frame;
                foreach (var line in convo.Lines)
                {
                    if (_endingSkipRequested)
                        break;

                    if (line.Frame != currentFrame)
                    {
                        currentFrame = line.Frame;
                        var outTween = CreateTween();
                        outTween.SetPauseMode(Tween.TweenPauseMode.Process);
                        outTween.TweenProperty(_endingImage, "modulate:a", 0.25f, 0.35f);
                        await ToSignal(outTween, Tween.SignalName.Finished);
                        if (_endingSkipRequested)
                            break;
                        _endingImage.Texture = GD.Load<Texture2D>(convo.Frames[currentFrame]);
                        var inTween = CreateTween();
                        inTween.SetPauseMode(Tween.TweenPauseMode.Process);
                        inTween.TweenProperty(_endingImage, "modulate:a", 1f, 0.45f);
                    }

                    _endingCaption.Text = string.IsNullOrEmpty(line.Speaker)
                        ? line.Text
                        : $"{line.Speaker}: {line.Text}";
                    var capIn = CreateTween();
                    capIn.SetPauseMode(Tween.TweenPauseMode.Process);
                    capIn.TweenProperty(_endingCaption, "modulate:a", 1f, 0.5f);
                    AudioManager.Play(Sfx.PageTurn, -16f, 0.7f);
                    await WaitForEndingAdvance(6.4f);
                    if (_endingSkipRequested)
                        break;
                    var capOut = CreateTween();
                    capOut.SetPauseMode(Tween.TweenPauseMode.Process);
                    capOut.TweenProperty(_endingCaption, "modulate:a", 0f, 0.4f);
                    await ToSignal(capOut, Tween.SignalName.Finished);
                }

                // Out of the conversation the way each choice deserves:
                // waking dissolves into light; the kept house cuts to black.
                _endingCaption.Text = string.Empty;
                _endingImage.Modulate = Colors.White;
                _endingTween?.Kill();
                _endingTween = null;
                if (_endingFade != null)
                {
                    _endingFade.Color = endingId == "waking"
                        ? new Color(1f, 0.98f, 0.92f, 0f)
                        : new Color(0f, 0f, 0f, 0f);
                    var washIn = CreateTween();
                    washIn.SetPauseMode(Tween.TweenPauseMode.Process);
                    washIn.TweenProperty(_endingFade, "color:a", 1f, endingId == "waking" ? 1.8f : 1.1f);
                    await ToSignal(washIn, Tween.SignalName.Finished);
                    _endingImage.Texture = GD.Load<Texture2D>(imagePath);
                    _endingImage.Scale = Vector2.One * 1.03f;
                    var washOut = CreateTween();
                    washOut.SetPauseMode(Tween.TweenPauseMode.Process);
                    washOut.TweenProperty(_endingFade, "color:a", 0f, 1.4f);
                    washOut.Parallel().TweenProperty(_endingImage, "scale", Vector2.One * 1.08f, 16f);
                    await WaitForEndingAdvance(1.2f);
                }
                else
                {
                    _endingImage.Texture = GD.Load<Texture2D>(imagePath);
                }
            }

            foreach (var card in cards)
            {
                if (_endingSkipRequested)
                    break;
                _endingAdvanceRequested = false;
                _endingCaption.Text = card;
                var fadeIn = CreateTween();
                fadeIn.SetPauseMode(Tween.TweenPauseMode.Process);
                fadeIn.TweenProperty(_endingCaption, "modulate:a", 1f, 0.8f);
                await WaitForEndingAdvance(5.8f);
                if (_endingSkipRequested)
                    break;
                var fadeOut = CreateTween();
                fadeOut.SetPauseMode(Tween.TweenPauseMode.Process);
                fadeOut.TweenProperty(_endingCaption, "modulate:a", 0f, 0.5f);
                await ToSignal(fadeOut, Tween.SignalName.Finished);
            }

            // The very last thing seen is Mara herself: smiling into the
            // distance she finally gets to have — or still curled in the
            // corner of her room, keeping everything.
            if (withIdaConversation)
            {
                if (!_endingSkipRequested)
                {
                    var stillOut = CreateTween();
                    stillOut.SetPauseMode(Tween.TweenPauseMode.Process);
                    stillOut.TweenProperty(_endingImage, "modulate:a", 0f, 0.9f);
                    await ToSignal(stillOut, Tween.SignalName.Finished);
                    _endingImage.Texture = GD.Load<Texture2D>(finalStillPath);
                    _endingImage.Scale = Vector2.One * 1.02f;
                    var stillIn = CreateTween();
                    stillIn.SetPauseMode(Tween.TweenPauseMode.Process);
                    stillIn.TweenProperty(_endingImage, "modulate:a", 1f, 1.2f);
                    stillIn.Parallel().TweenProperty(_endingImage, "scale", Vector2.One * 1.06f, 14f);
                    await WaitForEndingAdvance(2.4f);
                }
                else
                {
                    _endingImage.Texture = GD.Load<Texture2D>(finalStillPath);
                    _endingImage.Modulate = Colors.White;
                }
            }

            // The decision is complete once its title appears. Record it
            // now; if the player closes here, the ending is still found.
            GameState.Instance?.FinishRun(_endingId);
            int found = 0;
            foreach (var id in new[] { "hollow", "waking", "kept_house" })
            {
                if (Settings.Instance?.HasSeenEnding(id) == true)
                    found++;
            }
            _endingStats.Text = $"{GameState.Instance?.StatsSummary() ?? string.Empty}   ·   Endings found: {found}/3";
            _endingStats.Visible = true;
            _endingTitle.Text = title;
            _endingCaption.Text = finalCaption;
            var finalTween = CreateTween();
            finalTween.SetPauseMode(Tween.TweenPauseMode.Process);
            finalTween.TweenProperty(_endingTitle, "modulate:a", 1f, 1.4f);
            finalTween.Parallel().TweenProperty(_endingCaption, "modulate:a", 1f, 1.4f);
            await ToSignal(finalTween, Tween.SignalName.Finished);
            _endingReturnButton.Visible = true;
            _endingReturnButton.GrabFocus();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Ending interrupted: {ex.Message}");
            GameState.Instance?.FinishRun(_endingId);
            _endingTitle.Text = title;
            _endingCaption.Text = finalCaption;
            _endingTitle.Modulate = Colors.White;
            _endingCaption.Modulate = Colors.White;
            _endingReturnButton.Visible = true;
        }
    }

    private async System.Threading.Tasks.Task WaitForEndingAdvance(float seconds)
    {
        _endingAdvanceRequested = false;
        ulong deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        while (Time.GetTicksMsec() < deadline && !_endingAdvanceRequested && !_endingSkipRequested)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _endingAdvanceRequested = false;
    }

    private void OnEndingReturnPressed()
    {
        if (!_endingPlaying)
            return;
        _endingPlaying = false;
        _endingTween?.Kill();
        _endingTween = null;
        _endingRoot.Visible = false;
        GetTree().Paused = false;
        AudioManager.Instance?.StopAmbience();
        // FinishRun already let go of the save. Do not SaveGame here.
        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    // ---------------------------------------------------------- Mara's voice

    private void BuildAmbientLife()
    {
        // Canvas layer 1: over the room (0), under the speech bubble (2)
        // and under every scrap of GameUI proper (90). By default, no
        // animated sprite ever clips across somebody's words.
        _ambientLayer = new CanvasLayer { Name = "AmbientLife", Layer = 1 };
        AddChild(_ambientLayer);

        _ambientRects = new TextureRect[AmbientPoolSize];
        for (int i = 0; i < AmbientPoolSize; i++)
        {
            var rect = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            _ambientRects[i] = rect;
            _ambientLayer.AddChild(rect);
        }
    }

    private Texture2D[] AmbientFrames(string effect)
    {
        if (_ambientFrameCache.TryGetValue(effect, out var cached))
            return cached;

        string[] paths = effect switch
        {
            "steam" => ["res://assets/images/bgs/amb_steam_0.png", "res://assets/images/bgs/amb_steam_1.png", "res://assets/images/bgs/amb_steam_2.png"],
            "flame" => ["res://assets/images/bgs/amb_flame_0.png", "res://assets/images/bgs/amb_flame_1.png", "res://assets/images/bgs/amb_flame_2.png"],
            "leaves" => ["res://assets/images/bgs/amb_leaves_0.png", "res://assets/images/bgs/amb_leaves_1.png", "res://assets/images/bgs/amb_leaves_2.png"],
            "rain" => ["res://assets/images/bgs/amb_rain_0.png", "res://assets/images/bgs/amb_rain_1.png", "res://assets/images/bgs/amb_rain_2.png"],
            "dust" => ["res://assets/images/bgs/amb_dust_0.png", "res://assets/images/bgs/amb_dust_1.png", "res://assets/images/bgs/amb_dust_2.png"],
            "moth" => ["res://assets/images/bgs/amb_moth_0.png", "res://assets/images/bgs/amb_moth_1.png", "res://assets/images/bgs/amb_moth_2.png"],
            "branch" => ["res://assets/images/bgs/amb_branch.png"],
            _ => ["res://assets/images/bgs/amb_glow.png"],
        };
        var frames = new Texture2D[paths.Length];
        for (int i = 0; i < paths.Length; i++)
            frames[i] = ResourceLoader.Exists(paths[i]) ? GD.Load<Texture2D>(paths[i]) : null;
        _ambientFrameCache[effect] = frames;
        return frames;
    }

    private static string AmbientSceneFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    private readonly Dictionary<string, CanvasItem> _roomNodeCache = new();

    private CanvasItem FindRoomCanvasItem(string path)
    {
        if (_roomNodeCache.TryGetValue(path, out var cached)
            && GodotObject.IsInstanceValid(cached))
            return cached;
        var node = SceneManager.Instance?.CurrentScene?.GetNodeOrNull<CanvasItem>(path);
        if (node != null)
            _roomNodeCache[path] = node;
        return node;
    }

    /// <summary>Is this TileMap examine layer the one currently showing?
    /// ExamineHelper shows/hides by modulate alpha, and layers advanced
    /// past are freed outright — both read as "not showing" here.</summary>
    private bool RoomLayerShowing(string path)
    {
        var item = FindRoomCanvasItem(path);
        return item != null && item.Modulate.A > 0.5f && item.SelfModulate.A > 0.5f;
    }

    /// <summary>Polled scene watch (a UI-context lag on the exact frame of
    /// a scene change can never eat a room's weather).</summary>
    private void WatchAmbientScene()
    {
        string file = AmbientSceneFileName(SceneManager.CurrentScenePath);
        if (string.IsNullOrEmpty(file) || file == _ambientSceneFile)
            return;
        if (_context != UiContext.Game)
            return; // unspent — retried next frame, once we're in the game
        _ambientSceneFile = file;
        _roomNodeCache.Clear(); // last room's layers are freed; drop their wrappers
        _ambientActive = AmbientByScene.TryGetValue(file, out var specs)
            ? specs
            : System.Array.Empty<AmbientSpec>();
    }

    private void HideAmbientRects()
    {
        if (_ambientRects == null)
            return;
        foreach (var rect in _ambientRects)
        {
            if (rect != null)
                rect.Visible = false;
        }
    }

    private void UpdateAmbientLife(double delta)
    {
        WatchAmbientScene();
        if (_ambientRects == null)
            return;

        bool show = _context == UiContext.Game
            && !_cutscenePlaying && !_endingPlaying && !_shatterPlaying
            && _ambientActive.Length > 0
            // While her eyes are still closed, no weather over the black.
            && !(_ambientSceneFile == "bedroom_prologue.tscn"
                && GameState.Instance?.HasFlag("prologue_awake") != true);
        if (!show)
        {
            HideAmbientRects();
            return;
        }

        // Reduce Motion keeps the weather but holds it perfectly still.
        bool frozen = Settings.ReduceMotionOrDefault();
        if (!frozen && !GetTree().Paused)
            _ambientTime += (float)delta;

        Vector2 view = GetViewport().GetVisibleRect().Size;
        float scale = view.Y / 648f; // specs are authored at 1152x648
        for (int i = 0; i < _ambientRects.Length; i++)
        {
            var rect = _ambientRects[i];
            if (i >= _ambientActive.Length)
            {
                rect.Visible = false;
                continue;
            }

            var spec = _ambientActive[i];
            if (spec.CupNodePath != null && !RoomLayerShowing(spec.CupNodePath))
            {
                rect.Visible = false;
                continue;
            }
            string effect = spec.Effect;
            var frames = AmbientFrames(effect);
            int frame = 0;
            if (frames.Length > 1 && !frozen)
            {
                int step = (int)((_ambientTime + spec.Phase) * spec.Fps);
                frame = AmbientPingPong[Mathf.Abs(step) % AmbientPingPong.Length] % frames.Length;
            }
            var tex = frame < frames.Length ? frames[frame] : null;
            if (tex == null)
            {
                rect.Visible = false;
                continue;
            }

            float w = spec.W, h = spec.H;
            float aspect = tex.GetSize().X / Mathf.Max(tex.GetSize().Y, 1f);
            if (w <= 0f && h > 0f) w = h * aspect;
            else if (h <= 0f && w > 0f) h = w / aspect;
            w *= scale;
            h *= scale;

            rect.Texture = tex;
            rect.TextureFilter = effect == "glow"
                ? CanvasItem.TextureFilterEnum.Linear
                : CanvasItem.TextureFilterEnum.Nearest;
            rect.Size = new Vector2(w, h);
            rect.PivotOffset = new Vector2(w * spec.PivotX, 0f);
            var pos = new Vector2(
                spec.Cx * view.X - w / 2f, spec.Cy * view.Y - h / 2f);
            if (spec.OrbitRx > 0f && !frozen)
            {
                // A moth's errand: two sines arguing, never a clean circle.
                float t = _ambientTime;
                pos.X += (spec.OrbitRx * Mathf.Sin(t * 1.9f + spec.Phase)
                    + spec.OrbitRx * 0.35f * Mathf.Sin(t * 4.7f + spec.Phase * 2.1f)) * scale;
                pos.Y += (spec.OrbitRy * Mathf.Sin(t * 2.6f + spec.Phase * 1.3f)
                    + spec.OrbitRy * 0.4f * Mathf.Cos(t * 5.3f + spec.Phase)) * scale;
            }
            rect.Position = pos;

            float alpha = spec.Alpha;
            if (spec.Pulse > 0f && !frozen)
                alpha += spec.Pulse * Mathf.Sin(_ambientTime * spec.PulseSpeed + spec.Phase);
            rect.Modulate = new Color(spec.Tint, Mathf.Clamp(alpha, 0f, 1f));
            rect.Rotation = spec.SwayDegrees > 0f && !frozen
                ? Mathf.DegToRad(spec.SwayDegrees * Mathf.Sin(_ambientTime * spec.SwaySpeed + spec.Phase))
                : 0f;
            rect.Visible = true;
        }
    }

    private void BuildFragmentHeld()
    {
        _heldRoot = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _heldRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var dim = new ColorRect
        {
            Color = new Color(0.02f, 0.015f, 0.01f, 0.62f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _heldRoot.AddChild(dim);

        _heldGlow = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/images/bgs/amb_glow.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 0.82f, 0.5f, 0.4f),
        };
        _heldRoot.AddChild(_heldGlow);

        _heldHand = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/images/core/hand_palm.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _heldRoot.AddChild(_heldHand);

        _heldIcon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _heldRoot.AddChild(_heldIcon);

        _heldName = MakeLabel(string.Empty, 30, new Color(0.97f, 0.9f, 0.76f));
        _heldName.HorizontalAlignment = HorizontalAlignment.Center;
        _heldName.MouseFilter = Control.MouseFilterEnum.Ignore;
        _heldRoot.AddChild(_heldName);

        _heldLine = MakeLabel(string.Empty, 24, new Color(0.93f, 0.88f, 0.79f));
        _heldLine.HorizontalAlignment = HorizontalAlignment.Center;
        _heldLine.AutowrapMode = TextServer.AutowrapMode.Word;
        _heldLine.MouseFilter = Control.MouseFilterEnum.Ignore;
        _heldRoot.AddChild(_heldLine);

        _heldHint = MakeLabel("click — put it in the bag", 17, new Color(0.85f, 0.79f, 0.68f, 0.75f));
        _heldHint.HorizontalAlignment = HorizontalAlignment.Center;
        _heldHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        _heldRoot.AddChild(_heldHint);

        AddChild(_heldRoot);
    }

    private void ShowFragmentHeld(int index)
    {
        if (_heldRoot == null)
            return;

        _heldShowing = true;
        _heldBusy = true;
        _heldPhase = 0;
        _heldSeen[index] = true;
        _heldPending = -1;
        _heldAge = 0f;
        Dialogue.BeginInputBlock();

        Vector2 view = GetViewport().GetVisibleRect().Size;
        float cx = view.X / 2f;

        // First it is simply in her hand; the words come with the second
        // beat, when she lifts it to the light (AdvanceHeldToShowcase).
        _heldName.Text = GameState.ShardTitles[index];
        _heldName.Position = new Vector2(cx - 380f, view.Y * 0.13f);
        _heldName.Size = new Vector2(760f, 44f);
        _heldName.Modulate = new Color(1, 1, 1, 0);

        _heldLine.Text = HeldFragmentLines[index];
        _heldLine.Position = new Vector2(cx - 380f, view.Y * 0.68f);
        _heldLine.Size = new Vector2(760f, 96f);
        _heldLine.Modulate = new Color(1, 1, 1, 0);

        _heldHint.Text = "click";
        _heldHint.Position = new Vector2(cx - 380f, view.Y * 0.86f);
        _heldHint.Size = new Vector2(760f, 30f);
        _heldHint.Modulate = new Color(1, 1, 1, 0);

        // The hand rises into frame from below, the fragment already
        // resting in the palm, close enough to touch.
        float handW = 470f;
        float handH = handW * HeldHandAspect;
        Vector2 handRest = new Vector2(cx - handW / 2f, view.Y - handH * 0.92f);
        _heldHand.Size = new Vector2(handW, handH);
        _heldHand.Position = handRest + new Vector2(0f, 180f);
        _heldHand.Modulate = Colors.White;

        Vector2 palm = handRest + new Vector2(handW * HeldPalmX, handH * HeldPalmY);
        _heldIcon.Texture = GD.Load<Texture2D>(BagIconPaths[index]);
        _heldIcon.Size = new Vector2(300f, 300f);
        _heldIcon.PivotOffset = new Vector2(150f, 150f);
        _heldIcon.Scale = new Vector2(0.55f, 0.55f);
        _heldIcon.Position = palm - _heldIcon.PivotOffset + new Vector2(0f, 180f);

        _heldGlow.Size = new Vector2(330f, 330f);
        _heldGlow.Position = palm - new Vector2(165f, 165f) + new Vector2(0f, 180f);
        _heldGlow.Modulate = new Color(1f, 0.82f, 0.5f, 0.4f);

        _heldRoot.Modulate = new Color(1, 1, 1, 0);
        _heldRoot.Visible = true;
        AudioManager.Play(Sfx.PhotoPickup, -12f);

        _heldTween?.Kill();
        _heldTween = CreateTween();
        _heldTween.SetParallel(true);
        _heldTween.TweenProperty(_heldRoot, "modulate:a", 1f, 0.4f);
        _heldTween.TweenProperty(_heldHand, "position", handRest, 0.65f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _heldTween.TweenProperty(_heldIcon, "position", palm - _heldIcon.PivotOffset, 0.65f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _heldTween.TweenProperty(_heldGlow, "position", palm - new Vector2(165f, 165f), 0.65f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _heldTween.TweenProperty(_heldHint, "modulate:a", 0.75f, 0.4f).SetDelay(0.55f);
    }

    /// <summary>Second beat: the hand lowers away and the fragment is held
    /// up large — title, one line of Mara, then a click bags it.</summary>
    private void AdvanceHeldToShowcase()
    {
        if (!_heldShowing || _heldPhase != 0 || _heldRoot == null)
            return;
        _heldPhase = 1;
        _heldAge = 0f;
        AudioManager.Play(Sfx.ShardCollect, -7f);

        Vector2 view = GetViewport().GetVisibleRect().Size;
        float cx = view.X / 2f;
        _heldHint.Text = "click — put it in the bag";

        _heldTween?.Kill();
        _heldTween = CreateTween();
        _heldTween.SetParallel(true);
        _heldTween.TweenProperty(_heldHand, "position:y", _heldHand.Position.Y + 240f, 0.5f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _heldTween.TweenProperty(_heldHand, "modulate:a", 0f, 0.5f);
        _heldTween.TweenProperty(_heldIcon, "position",
                new Vector2(cx - 150f, view.Y * 0.42f - 150f), 0.55f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        _heldTween.TweenProperty(_heldIcon, "scale", Vector2.One, 0.55f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        _heldTween.TweenProperty(_heldGlow, "position",
            new Vector2(cx - 215f, view.Y * 0.42f - 215f), 0.55f);
        _heldTween.TweenProperty(_heldGlow, "size", new Vector2(430f, 430f), 0.55f);
        _heldTween.TweenProperty(_heldName, "modulate:a", 1f, 0.4f).SetDelay(0.15f);
        _heldTween.TweenProperty(_heldLine, "modulate:a", 1f, 0.4f).SetDelay(0.2f);
        _heldTween.TweenProperty(_heldHint, "modulate:a", 0.75f, 0.4f).SetDelay(0.3f);
    }

    private void DismissFragmentHeld()
    {
        if (!_heldShowing || _heldRoot == null)
            return;
        _heldShowing = false;
        AudioManager.Play(Sfx.ShardPlace, -9f);

        // The icon's visual centre is Position + PivotOffset; land that
        // centre on the bag as it shrinks away into it.
        Vector2 bagCenter = _bagButton != null
            ? _bagButton.GetGlobalRect().GetCenter()
            : new Vector2(GetViewport().GetVisibleRect().Size.X - 60f, 60f);

        _heldTween?.Kill();
        _heldTween = CreateTween();
        _heldTween.SetParallel(true);
        _heldTween.TweenProperty(_heldIcon, "position",
                bagCenter - _heldIcon.PivotOffset, 0.55f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _heldTween.TweenProperty(_heldIcon, "scale", new Vector2(0.12f, 0.12f), 0.55f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _heldTween.TweenProperty(_heldRoot, "modulate:a", 0f, 0.55f);
        _heldTween.Chain().TweenCallback(Callable.From(() =>
        {
            if (_heldRoot != null)
                _heldRoot.Visible = false;
            _heldBusy = false; // bagged — the house may move on now
            Dialogue.EndInputBlock();
            SceneManager.ResumeDeferredSceneChange();
        }));
    }

    private void UpdateFragmentHeld(double delta)
    {
        if (_heldBusy)
        {
            _heldAge += (float)delta;
            return;
        }

        DetectNewFragments();
        if (_heldPending < 0 || !HeldMomentCanShow())
            return;

        ShowFragmentHeld(_heldPending);
    }

    private void DetectNewFragments()
    {
        var gs = GameState.Instance;
        if (gs == null || _context != UiContext.Game)
            return;

        // First sight of a (re)started run: whatever she already carries
        // is old news — only fragments earned from here get the moment.
        if (!_heldBaselineSynced)
        {
            for (int i = 0; i < _heldSeen.Length; i++)
                _heldSeen[i] = gs.IsShardRecovered(i);
            _heldBaselineSynced = true;
            return;
        }

        for (int i = 0; i < _heldSeen.Length; i++)
        {
            bool recovered = gs.IsShardRecovered(i);
            if (!recovered)
            {
                _heldSeen[i] = false; // a fresh run forgets with her
                if (_heldPending == i)
                    _heldPending = -1;
            }
            else if (!_heldSeen[i] && _heldPending < 0)
                _heldPending = i;
        }
    }

    private bool HeldMomentCanShow() =>
        !GetTree().Paused && !Dialogue.IsShowing && !AnyPanelOpen()
        && !_cutscenePlaying && !_endingPlaying && !_shatterPlaying;

    /// <summary>Asked by SceneManager before it honours a scene change:
    /// is a fragment still owed its moment (waiting to be picked up, or
    /// already in her hand / flying to the bag)? If so the change is
    /// deferred and resumed when the bag closes over it. Detection runs
    /// here too, so a fragment collected and left behind in the very same
    /// breath (Room 4's door) is still caught. Outside ordinary play —
    /// menus, pause, cutscenes, endings — the answer is always no: the
    /// house is never held hostage.</summary>
    public bool IsHeldMomentPending()
    {
        if (_heldBusy)
            return true;
        if (_context != UiContext.Game || GetTree().Paused
            || _cutscenePlaying || _endingPlaying || _shatterPlaying)
            return false;
        DetectNewFragments();
        return _heldPending >= 0;
    }

    private void BuildMaraAside()
    {
        _maraAsideLabel = MakeLabel(string.Empty, 21, new Color(0.95f, 0.9f, 0.8f));
        _maraAsideLabel.AnchorLeft = 0.5f;
        _maraAsideLabel.AnchorRight = 0.5f;
        _maraAsideLabel.AnchorTop = 1f;
        _maraAsideLabel.AnchorBottom = 1f;
        _maraAsideLabel.OffsetLeft = -460;
        _maraAsideLabel.OffsetRight = 460;
        _maraAsideLabel.OffsetTop = -88;
        _maraAsideLabel.OffsetBottom = -42;
        _maraAsideLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _maraAsideLabel.VerticalAlignment = VerticalAlignment.Center;
        _maraAsideLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _maraAsideLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _maraAsideLabel.Modulate = new Color(1, 1, 1, 0);

        var chip = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.035f, 0.03f, 0.72f),
            BorderColor = new Color(0.78f, 0.66f, 0.46f, 0.4f),
        };
        chip.SetBorderWidthAll(1);
        chip.SetCornerRadiusAll(4);
        chip.ContentMarginLeft = 14;
        chip.ContentMarginRight = 14;
        chip.ContentMarginTop = 6;
        chip.ContentMarginBottom = 6;
        _maraAsideLabel.AddThemeStyleboxOverride("normal", chip);
        _hud.AddChild(_maraAsideLabel);
    }

    /// <summary>Mara, thinking out loud — a caption in her own voice that
    /// never takes the controls away from the player.</summary>
    private void ShowMaraAside(string line)
    {
        if (_maraAsideLabel == null || string.IsNullOrWhiteSpace(line))
            return;

        _maraAsideTween?.Kill();
        _maraAsideLabel.Text = line;
        _maraAsideLabel.Modulate = new Color(1, 1, 1, 0);
        _maraAsideTween = CreateTween();
        _maraAsideTween.TweenProperty(_maraAsideLabel, "modulate:a", 1f, 0.3f);
        _maraAsideTween.TweenInterval(2.6f);
        _maraAsideTween.TweenProperty(_maraAsideLabel, "modulate:a", 0f, 0.7f);
    }

    private void UpdateMaraAsides(double delta)
    {
        _asideCooldown = Mathf.Max(0f, _asideCooldown - (float)delta);

        var gs = GameState.Instance;
        if (gs == null || _context != UiContext.Game)
            return;

        int examines = gs.ExaminesPerformed;
        if (_lastExaminesPerformed < 0 || examines < _lastExaminesPerformed)
        {
            _lastExaminesPerformed = examines;
            return;
        }
        if (examines == _lastExaminesPerformed)
            return;

        // Something was just looked at. Wait until Mara can be heard — the
        // look's own words finished, no panel up — then let her mutter.
        if (GetTree().Paused || Dialogue.IsShowing || AnyPanelOpen() ||
            _cutscenePlaying || _endingPlaying || _shatterPlaying)
            return;

        _lastExaminesPerformed = examines;
        if (_asideCooldown > 0f)
            return;
        if (_asideRandom.NextDouble() > 0.55)
        {
            _asideCooldown = 10f;
            return;
        }

        string line = NextAsideLine(GameState.CurrentRoomName());
        if (line == null)
            return;
        ShowMaraAside(line);
        _asideCooldown = 35f;
    }

    private string NextAsideLine(string room)
    {
        room ??= string.Empty;
        if (_asideBag.Count == 0 || _asideBagRoom != room)
        {
            _asideBagRoom = room;
            _asideBag.Clear();
            _asideBag.AddRange(GenericAsideLines);
            _asideBag.AddRange(room switch
            {
                "Room1" => Room1AsideLines,
                "Room2" => Room2AsideLines,
                "Room3" => Room3AsideLines,
                "Room4" => Room4AsideLines,
                "Room5" => Room5AsideLines,
                "Room6" => Room6AsideLines,
                "Room7" => Room7AsideLines,
                _ => System.Array.Empty<string>(),
            });
            for (int i = _asideBag.Count - 1; i > 0; i--)
            {
                int j = _asideRandom.Next(i + 1);
                (_asideBag[i], _asideBag[j]) = (_asideBag[j], _asideBag[i]);
            }
        }

        if (_asideBag.Count == 0)
            return null;
        int last = _asideBag.Count - 1;
        string line = _asideBag[last];
        _asideBag.RemoveAt(last);
        return line;
    }

    private static string RoomEntryFlag(string roomKey) => $"room_entry_monologue_{roomKey}";

    private static string RandomRoomFlashChoiceFlag(string roomKey) => $"random_room_flash_choice_{roomKey}";

    private static string SceneFileName(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return string.Empty;
        int slash = scenePath.LastIndexOf('/');
        return slash >= 0 ? scenePath.Substring(slash + 1) : scenePath;
    }

    private static string ArrivalParentRoomKey(string roomKey) => roomKey switch
    {
        "Room1Puzzle" => "Room1",
        "Room2Puzzle" => "Room2",
        "Room4Flashback" => "Room4",
        "ApartmentNow" => "Room5",
        "Porch447" => "Room6",
        _ => roomKey,
    };

    private static bool IsArrivalAlreadyUnderway(GameState gs, string roomKey)
    {
        RoomExamineTracker.GetRoomProgress(roomKey, out int done, out int total, out _);
        if (total > 0 && done > 0)
            return true;
        if (gs.IsRoomCompleted(ArrivalParentRoomKey(roomKey)))
            return true;

        return roomKey switch
        {
            "Room1Puzzle" => gs.IsShardRecovered(0),
            "Room2Puzzle" => gs.IsShardRecovered(1),
            "Room7" => gs.CountPlaced() > 0 || !string.IsNullOrWhiteSpace(gs.CompletedEnding),
            _ => false,
        };
    }

    /// <summary>Choose — once, and saved with the run — which non-kitchen
    /// main room will briefly lose its colour and show Mara everything in
    /// it. Choosing on the first eligible threshold keeps older saves in
    /// the game: rooms already entered or underway drop out, leaving the
    /// current and later rooms as the little deck.</summary>
    private string EnsureRandomRoomFlashChoice(GameState gs, string currentRoomKey)
    {
        foreach (string room in RandomRoomFlashRooms)
        {
            if (gs.HasFlag(RandomRoomFlashChoiceFlag(room)))
                return room;
        }

        if (gs.HasFlag(RandomRoomFlashSeenFlag) ||
            System.Array.IndexOf(RandomRoomFlashRooms, currentRoomKey) < 0)
            return null;

        var candidates = new List<string>();
        foreach (string room in RandomRoomFlashRooms)
        {
            bool underway = IsArrivalAlreadyUnderway(gs, room);
            if (room == currentRoomKey)
            {
                if (!underway)
                    candidates.Add(room);
            }
            else if (!underway && !gs.HasFlag(RoomEntryFlag(room)))
            {
                candidates.Add(room);
            }
        }

        if (candidates.Count == 0)
            return null;

        string chosen = candidates[_asideRandom.Next(candidates.Count)];
        gs.SetFlag(RandomRoomFlashChoiceFlag(chosen), save: false);
        gs.SaveGame($"random room flash chosen: {chosen}");
        return chosen;
    }

    private void SetRoomFlashAmount(float amount)
    {
        _corruptionMaterial?.SetShaderParameter("flashback", Mathf.Clamp(amount, 0f, 1f));
    }

    private async Task WaitArrivalSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
    }

    private bool StillInArrivalScene(string scenePath) =>
        IsInsideTree() && SceneManager.CurrentScenePath == scenePath;

    /// <summary>First-entry room voice, plus the once-per-run monochrome
    /// flash. The flash rings everything still to be faced for a few
    /// seconds — a memory accidentally pointing — then gives the room its
    /// colour back before Mara names where she is.</summary>
    private async Task PlayRoomArrivalAsync(string scenePath)
    {
        if (_roomArrivalPlaying || _cutscenePlaying || _endingPlaying || _shatterPlaying)
            return;
        if (!RoomEntryMonologuesByScene.TryGetValue(SceneFileName(scenePath), out var entry))
            return;

        var gs = GameState.Instance;
        if (gs == null)
            return;

        string roomKey = entry.RoomKey;
        string entryFlag = RoomEntryFlag(roomKey);
        bool entrySeen = gs.HasFlag(entryFlag);
        bool underway = IsArrivalAlreadyUnderway(gs, roomKey);
        string flashRoom = EnsureRandomRoomFlashChoice(gs, roomKey);
        bool playFlash = flashRoom == roomKey
            && !gs.HasFlag(RandomRoomFlashSeenFlag)
            && !gs.IsRoomCompleted(ArrivalParentRoomKey(roomKey));

        if (!playFlash && (entrySeen || underway))
        {
            if (!entrySeen)
            {
                gs.SetFlag(entryFlag, save: false);
                gs.SaveGame($"room entry already underway: {roomKey}");
            }
            return;
        }

        _roomArrivalPlaying = true;
        try
        {
            await WaitArrivalSeconds(0.35f);
            if (!StillInArrivalScene(scenePath))
                return;

            if (playFlash)
            {
                await PlayRandomRoomFlashAsync(scenePath);
                if (!StillInArrivalScene(scenePath))
                    return;
                gs.SetFlag(RandomRoomFlashSeenFlag, save: false);
                gs.SaveGame($"random room flash seen: {roomKey}");
            }

            if (!gs.HasFlag(entryFlag) && !underway)
            {
                foreach (string line in entry.Lines)
                {
                    await Dialogue.ShowText(this, line, 5.2f, 576f, 324f, fontSize: 24);
                    if (!StillInArrivalScene(scenePath))
                        return;
                }
                gs.SetFlag(entryFlag, save: false);
                gs.SaveGame($"room entry monologue: {roomKey}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"[GameUI] Room arrival interrupted: {ex.Message}");
        }
        finally
        {
            SetRoomFlashAmount(0f);
            _roomArrivalPlaying = false;
        }
    }

    private async Task PlayRandomRoomFlashAsync(string scenePath)
    {
        Dialogue.BeginInputBlock();
        bool revealed = false;
        _roomFlashPlaying = true;
        try
        {
            Hotspots.RevealAll();
            revealed = true;
            AudioManager.Play(Sfx.CorruptionGlitch, -10f, 0.72f);

            if (Settings.ReduceMotionOrDefault())
            {
                SetRoomFlashAmount(0.85f);
                await WaitArrivalSeconds(0.9f);
            }
            else
            {
                (float Amount, float Seconds)[] flicker =
                [
                    (1f, 0.14f),
                    (0.18f, 0.08f),
                    (0.92f, 0.18f),
                    (0.35f, 0.08f),
                    (1f, 0.65f),
                ];
                foreach (var step in flicker)
                {
                    if (!StillInArrivalScene(scenePath))
                        return;
                    SetRoomFlashAmount(step.Amount);
                    await WaitArrivalSeconds(step.Seconds);
                }
            }

            if (!StillInArrivalScene(scenePath))
                return;

            SetRoomFlashAmount(0.82f);
            await Dialogue.ShowText(this, "What was that?", 2.8f, 576f, 324f, fontSize: 27);
            if (!StillInArrivalScene(scenePath))
                return;
            await Dialogue.ShowText(this,
                "For a second the room went black and white — and everything in it showed itself at once.",
                5f, 576f, 324f, fontSize: 24);
            if (!StillInArrivalScene(scenePath))
                return;

            await WaitArrivalSeconds(0.45f);
        }
        finally
        {
            SetRoomFlashAmount(0f);
            _roomFlashPlaying = false;
            if (revealed && StillInArrivalScene(scenePath) && !_hotspotsHeld)
            {
                Hotspots.HideAll();
                Hotspots.RefreshPersistent();
            }
            Dialogue.EndInputBlock();
        }
    }

    private void RequestCutsceneAdvance()
    {
        _cutsceneAdvanceRequested = true;
        if (!_cutsceneWaitingForContinue)
            return;

        _cutsceneWaitingForContinue = false;
        AudioManager.Play(Sfx.UiClick, -10f, 0.9f);
        _cutsceneContinueSource?.TrySetResult(true);
    }

    private void RequestCutsceneSkip()
    {
        _cutsceneSkipRequested = true;
        _cutsceneWaitingForContinue = false;
        _cutsceneContinueSource?.TrySetResult(true);
    }

    private void UpdateCutsceneContinuePrompt(double delta)
    {
        if (_cutsceneContinueLabel == null)
            return;

        float target = _cutsceneWaitingForContinue
            ? (Settings.ReduceMotionOrDefault()
                ? 0.96f
                : 0.82f + 0.16f * (float)Math.Sin(Time.GetTicksMsec() / 520.0))
            : 0f;
        // The words themselves drift in slowly after the still and caption
        // are already there; once clicked, they clear promptly with the shot.
        float rate = _cutsceneWaitingForContinue ? 0.55f : 2.4f;
        var modulate = _cutsceneContinueLabel.Modulate;
        modulate.A = Mathf.MoveToward(modulate.A, target, (float)delta * rate);
        _cutsceneContinueLabel.Modulate = modulate;
        _cutsceneContinueLabel.Visible = modulate.A > 0.01f;
    }

    private async Task WaitForCutsceneSeconds(float seconds)
    {
        var timer = CreateTween();
        timer.SetPauseMode(Tween.TweenPauseMode.Process);
        timer.TweenInterval(seconds);
        await ToSignal(timer, Tween.SignalName.Finished);
    }

    /// <summary>Hold the current post-Start introduction still until the
    /// player explicitly continues. This is event-driven rather than a
    /// deadline or an indefinite frame-polling loop: input completes the
    /// source, so a caption can stay readable for as long as the player
    /// needs.</summary>
    private async Task WaitForCutsceneContinue()
    {
        if (_cutsceneSkipRequested)
            return;

        _cutsceneContinueSource = new TaskCompletionSource<bool>();
        // Clicks made while the still was fading in do not count: the
        // prompt appears only once the caption is fully readable.
        _cutsceneAdvanceRequested = false;
        _cutsceneWaitingForContinue = true;
        if (_cutsceneContinueLabel != null)
        {
            var modulate = _cutsceneContinueLabel.Modulate;
            modulate.A = 0f;
            _cutsceneContinueLabel.Modulate = modulate;
            _cutsceneContinueLabel.Visible = true;
        }

        await _cutsceneContinueSource.Task;
        _cutsceneWaitingForContinue = false;
    }

    private void BuildCutscene()
    {
        _cutsceneRoot = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _cutsceneRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_cutsceneRoot);

        var black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _cutsceneRoot.AddChild(black);
        _cutsceneBackdrop = black;

        _cutsceneImage = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _cutsceneImage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _cutsceneRoot.AddChild(_cutsceneImage);

        // The truth reveal arrives through white, not black (see
        // PlayFinaleTruthReveal); this wash stays transparent otherwise.
        _cutsceneWash = new ColorRect
        {
            Color = new Color(1, 1, 1, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _cutsceneWash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _cutsceneRoot.AddChild(_cutsceneWash);

        var band = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.5f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0,
            AnchorRight = 1,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = 0,
            OffsetRight = 0,
            OffsetTop = -196,
            OffsetBottom = 0,
        };
        _cutsceneRoot.AddChild(band);
        _cutsceneBand = band;

        _cutsceneCaption = MakeLabel("", 30, new Color(0.96f, 0.92f, 0.84f));
        _cutsceneCaption.AutowrapMode = TextServer.AutowrapMode.Word;
        _cutsceneCaption.HorizontalAlignment = HorizontalAlignment.Center;
        _cutsceneCaption.VerticalAlignment = VerticalAlignment.Center;
        _cutsceneCaption.MouseFilter = Control.MouseFilterEnum.Ignore;
        _cutsceneCaption.AnchorLeft = 0;
        _cutsceneCaption.AnchorRight = 1;
        _cutsceneCaption.AnchorTop = 1;
        _cutsceneCaption.AnchorBottom = 1;
        _cutsceneCaption.OffsetLeft = 90;
        _cutsceneCaption.OffsetRight = -90;
        _cutsceneCaption.OffsetTop = -182;
        _cutsceneCaption.OffsetBottom = -44;
        _cutsceneRoot.AddChild(_cutsceneCaption);

        _cutsceneHint = MakeLabel("Click — continue      Esc — skip", 16, new Color(0.9f, 0.86f, 0.78f, 0.55f));
        _cutsceneHint.HorizontalAlignment = HorizontalAlignment.Right;
        _cutsceneHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        _cutsceneHint.AnchorLeft = 1;
        _cutsceneHint.AnchorRight = 1;
        _cutsceneHint.AnchorTop = 1;
        _cutsceneHint.AnchorBottom = 1;
        _cutsceneHint.OffsetLeft = -430;
        _cutsceneHint.OffsetRight = -26;
        _cutsceneHint.OffsetTop = -38;
        _cutsceneHint.OffsetBottom = -12;
        _cutsceneRoot.AddChild(_cutsceneHint);

        // The post-Start introduction has its own unmistakable prompt:
        // centered under the caption and shown only while that still is
        // waiting for the player's click.
        _cutsceneContinueLabel = MakeLabel("Click to continue", 21,
            new Color(0.96f, 0.92f, 0.83f));
        _cutsceneContinueLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _cutsceneContinueLabel.VerticalAlignment = VerticalAlignment.Center;
        _cutsceneContinueLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _cutsceneContinueLabel.AnchorLeft = 0.5f;
        _cutsceneContinueLabel.AnchorRight = 0.5f;
        _cutsceneContinueLabel.AnchorTop = 1;
        _cutsceneContinueLabel.AnchorBottom = 1;
        _cutsceneContinueLabel.OffsetLeft = -180;
        _cutsceneContinueLabel.OffsetRight = 180;
        _cutsceneContinueLabel.OffsetTop = -40;
        _cutsceneContinueLabel.OffsetBottom = -10;
        _cutsceneContinueLabel.Visible = false;
        _cutsceneContinueLabel.Modulate = new Color(1, 1, 1, 0);
        // Just the words on the image: no panel, border, or background chip.
        _cutsceneContinueLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _cutsceneRoot.AddChild(_cutsceneContinueLabel);

        var skip = MakeButton("Skip", 20);
        // MakeButton's normal state is transparent — invisible over the bright
        // kitchen still. Give Skip the dark chip the Step Back button uses.
        var skipNormal = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.72f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.35f),
        };
        skipNormal.SetBorderWidthAll(1);
        skipNormal.SetCornerRadiusAll(4);
        skipNormal.SetContentMarginAll(8);
        var skipHover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.75f, 0.55f, 0.16f),
            BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.5f),
        };
        skipHover.SetBorderWidthAll(1);
        skipHover.SetCornerRadiusAll(4);
        skipHover.SetContentMarginAll(8);
        skip.AddThemeStyleboxOverride("normal", skipNormal);
        skip.AddThemeStyleboxOverride("hover", skipHover);
        skip.AddThemeStyleboxOverride("pressed", skipHover);
        skip.AddThemeStyleboxOverride("focus", skipHover);
        skip.AnchorLeft = 1;
        skip.AnchorRight = 1;
        skip.OffsetLeft = -168;
        skip.OffsetRight = -24;
        skip.OffsetTop = 16;
        skip.OffsetBottom = 58;
        skip.FocusMode = Control.FocusModeEnum.None;
        _cutsceneSkipButton = skip;
        skip.Pressed += RequestCutsceneSkip;
        _cutsceneRoot.AddChild(skip);

        _cutsceneRoot.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left
                && (_cutsceneSkipButton == null
                    || !_cutsceneSkipButton.GetGlobalRect().HasPoint(mb.Position)))
            {
                RequestCutsceneAdvance();
            }
        };
    }

    /// <summary>New Game → the original memory cutscene plays first, over the
    /// bedroom's first open; Mara's first-person waking follows it. The
    /// how-to-play cards are offered later in Room 1. Continue skips this.</summary>
    private void MaybePlayCutsceneThenTutorial(string scenePath)
    {
        var gs = GameState.Instance;

        // Just walked out of a bedroom straight into the kitchen: that
        // deserves a word before anything else the opening has to say.
        if (gs != null && scenePath != null && scenePath.EndsWith("/room_1.tscn")
            && gs.HasFlag("prologue_from_bedroom") && !gs.HasFlag("prologue_kitchen_confused"))
        {
            gs.SetFlag("prologue_kitchen_confused");
            PlayKitchenConfusionThenOpening(scenePath);
            return;
        }

        if (gs != null && gs.CutscenePending && scenePath != null
            && (scenePath.EndsWith("/bedroom_prologue.tscn") || scenePath.EndsWith("/room_1.tscn")))
        {
            gs.ConsumeCutscenePending();
            PlayCutsceneAsync();
            return;
        }

        MaybeAutoShowTutorial(scenePath);
    }

    /// <summary>"…The kitchen?" — Mara names the wrongness out loud, then
    /// the opening proceeds exactly as it always has (memory cutscene,
    /// tutorial question) by re-entering the same funnel.</summary>
    private async void PlayKitchenConfusionThenOpening(string scenePath)
    {
        Dialogue.BeginInputBlock();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Dialogue.ShowText(this, "…The kitchen?", 2.6f, 576f, 324f, fontSize: 28);
            await Dialogue.ShowText(this, "I was in bed. I was just in bed — in my room, upstairs. There was a whole hallway between that door and here.", 5.5f, 576f, 324f, fontSize: 24);
            await Dialogue.ShowText(this, "No hallway. No upstairs. Just this. …Okay. Okay. One room at a time, Mara.", 5f, 576f, 324f, fontSize: 24);
        }
        finally
        {
            Dialogue.EndInputBlock();
        }

        MaybePlayCutsceneThenTutorial(scenePath);
    }

    private async void PlayCutsceneAsync()
    {
        if (_cutscenePlaying || _cutsceneRoot == null)
            return;

        _cutscenePlaying = true;
        _cutsceneSkipRequested = false;
        _cutsceneAdvanceRequested = false;
        _cutsceneWaitingForContinue = false;
        _cutsceneContinueSource = null;
        // Only the original post-Start introduction (no borrowed shot list)
        // is click-paced. Borrowed screenings such as the finale truth
        // reveal keep their authored timed flow.
        _cutsceneClickOnly = _activeShots == null;
        if (_cutsceneHint != null)
            _cutsceneHint.Visible = !_cutsceneClickOnly;
        if (_cutsceneContinueLabel != null)
        {
            _cutsceneContinueLabel.Visible = false;
            _cutsceneContinueLabel.Modulate = new Color(1, 1, 1, 0);
        }
        _cutsceneRoot.Visible = true;
        GetTree().Paused = true;

        try
        {
            // One frame so the full-rect controls have real sizes (zoom pivot).
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            bool fromWhite = _cutsceneFadeFromWhite;
            if (fromWhite && _cutsceneWash != null)
            {
                // The room behind whites out: backdrop and caption band hide
                // so nothing dark stands between the player and the light,
                // and the wash reads as the world going overexposed into
                // somewhere else entirely.
                if (_cutsceneBackdrop != null)
                    _cutsceneBackdrop.Visible = false;
                if (_cutsceneBand != null)
                    _cutsceneBand.Visible = false;
                _cutsceneCaption.Modulate = new Color(1, 1, 1, 0);
                _cutsceneWash.Color = new Color(1, 1, 1, 0);
                AudioManager.Play(Sfx.ClarifyReveal, -12f, 0.8f);
                _cutsceneTween = CreateTween();
                _cutsceneTween.SetPauseMode(Tween.TweenPauseMode.Process);
                _cutsceneTween.TweenProperty(_cutsceneWash, "color:a", 1f,
                    Settings.ReduceMotionOrDefault() ? 0.3f : 1.5f);
                await ToSignal(_cutsceneTween, Tween.SignalName.Finished);
                if (_cutsceneBackdrop != null)
                    _cutsceneBackdrop.Visible = true;
                if (_cutsceneBand != null)
                    _cutsceneBand.Visible = true;
            }

            bool firstShot = true;
            foreach (var shot in (_activeShots ?? CutsceneShots))
            {
                if (_cutsceneSkipRequested)
                    break;

                _cutsceneAdvanceRequested = false;
                _cutsceneImage.Texture = string.IsNullOrEmpty(shot.ImagePath)
                    ? null
                    : GD.Load<Texture2D>(shot.ImagePath);
                SetCutsceneCaption(shot.Caption);

                _cutsceneTween?.Kill();
                _cutsceneImage.PivotOffset = _cutsceneImage.Size / 2f;
                _cutsceneImage.Scale = Vector2.One;
                _cutsceneImage.Modulate = new Color(1, 1, 1, 0);
                _cutsceneCaption.Modulate = new Color(1, 1, 1, 0);

                _cutsceneTween = CreateTween();
                _cutsceneTween.SetPauseMode(Tween.TweenPauseMode.Process);
                if (fromWhite && firstShot && _cutsceneWash != null)
                {
                    // The place resolves out of the white: the image is
                    // already at full strength underneath, the wash
                    // lifting off it like breath off glass.
                    _cutsceneImage.Modulate = new Color(1, 1, 1, 1);
                    _cutsceneTween.TweenProperty(_cutsceneWash, "color:a", 0f, 1.4f);
                    _cutsceneTween.Parallel().TweenProperty(_cutsceneCaption, "modulate:a", 1f, 1.2f);
                }
                else
                {
                    _cutsceneTween.TweenProperty(_cutsceneImage, "modulate:a", 1f, 0.9);
                    _cutsceneTween.Parallel().TweenProperty(_cutsceneCaption, "modulate:a", 1f, 0.9);
                }
                float zoomSeconds = _cutsceneClickOnly
                    ? Math.Max(shot.HoldSeconds + 1.5f, 18f)
                    : shot.HoldSeconds + 1.5f;
                _cutsceneTween.Parallel().TweenProperty(_cutsceneImage, "scale", Vector2.One * 1.07f, zoomSeconds);
                firstShot = false;
                AudioManager.Play(Sfx.PageTurn, -16f, 0.65f);

                if (_cutsceneClickOnly)
                {
                    // Let the still and caption finish fading in and sit
                    // quietly for a beat before the bare continue words
                    // begin their own slow fade. HoldSeconds no longer acts
                    // as a reading deadline for the post-Start introduction.
                    await WaitForCutsceneSeconds(1.75f);
                    if (!_cutsceneSkipRequested)
                        await WaitForCutsceneContinue();
                }
                else
                {
                    ulong deadline = Time.GetTicksMsec() + (ulong)(shot.HoldSeconds * 1000f);
                    while (Time.GetTicksMsec() < deadline && !_cutsceneAdvanceRequested && !_cutsceneSkipRequested)
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }

                if (_cutsceneSkipRequested)
                    break;

                _cutsceneTween?.Kill();
                _cutsceneTween = CreateTween();
                _cutsceneTween.SetPauseMode(Tween.TweenPauseMode.Process);
                _cutsceneTween.TweenProperty(_cutsceneImage, "modulate:a", 0f, 0.55);
                _cutsceneTween.Parallel().TweenProperty(_cutsceneCaption, "modulate:a", 0f, 0.55);
                await ToSignal(_cutsceneTween, Tween.SignalName.Finished);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Cutscene interrupted: {ex.Message}");
        }
        finally
        {
            EndCutscene();
        }
    }

    private void EndCutscene()
    {
        _cutsceneTween?.Kill();
        _cutsceneTween = null;
        _cutscenePlaying = false;
        _cutsceneWaitingForContinue = false;
        _cutsceneClickOnly = false;
        _cutsceneContinueSource?.TrySetResult(true);
        _cutsceneContinueSource = null;
        if (_cutsceneContinueLabel != null)
        {
            _cutsceneContinueLabel.Visible = false;
            _cutsceneContinueLabel.Modulate = new Color(1, 1, 1, 0);
        }
        if (_cutsceneHint != null)
            _cutsceneHint.Visible = true;
        if (_cutsceneRoot != null)
            _cutsceneRoot.Visible = false;

        if (GetTree().Paused)
            GetTree().Paused = false;

        // A borrowed screening (the finale's truth reveal) hands control
        // back to its caller instead of falling into the tutorial flow.
        _activeShots = null;
        _cutsceneFadeFromWhite = false;
        if (_cutsceneWash != null)
            _cutsceneWash.Color = new Color(1, 1, 1, 0);
        if (_cutsceneBackdrop != null)
            _cutsceneBackdrop.Visible = true;
        if (_cutsceneBand != null)
            _cutsceneBand.Visible = true;
        if (_cutsceneOnComplete != null)
        {
            var onComplete = _cutsceneOnComplete;
            _cutsceneOnComplete = null;
            onComplete();
            return;
        }

        // First visit ever: how-to-play follows the memory.
        MaybeAutoShowTutorial(SceneManager.CurrentScenePath);
    }

    /// <summary>What really happened, replayed first-person once the six
    /// fragments stand in order and Release is spent — before any choice is
    /// offered. The last room fades to white and Mara finds herself back in
    /// the garden on that day: she recognises it, she tries to be heard,
    /// and then she can only watch the whole of it — the morning Ida left
    /// home, the promise, the waiting, Mara's own ordinary lateness, the
    /// walk, the bridge — the way it actually went. Click advances;
    /// Esc/Skip works as everywhere.</summary>
    private static readonly (string ImagePath, string Caption, float HoldSeconds)[] TruthShots =
    [
        ("res://assets/images/bgs/Room_6_Final.png",
         "“Where... am I?”", 4f),
        ("res://assets/images/bgs/Room_6_Final.png",
         "“This place. I know this place. The apple trees — the swing —”", 5f),
        ("res://assets/images/bgs/Truth_Ida_Waits.png",
         "“Wai—”", 3f),
        ("res://assets/images/bgs/Truth_Ida_Waits.png",
         "“Ida. Ida, look at me — please look at me.” She doesn't. She can't.", 5f),
        ("res://assets/images/bgs/Truth_Ida_Waits.png",
         "This isn't a place I've walked into. It's that day — the whole of it. And all I can do is watch it the way it actually went.", 6f),
        ("res://assets/images/bgs/Room_1_BG.png",
         "That day she left home loud — toast in one hand, already talking. I said I'd fetch her at four. I said it like a fact. She believed me.", 6.5f),
        ("res://assets/images/bgs/Room_3_BG.png",
         "She spent her days naming the things that frightened her so they couldn't jump out of the dark. It was her whole way of being brave.", 6f),
        ("res://assets/images/bgs/Truth_Ida_Waits.png",
         "She waited by the apples, where I'd told her to wait. One swing for every minute she meant not to mind. She counted a long way past four.", 6.5f),
        ("res://assets/images/bgs/Room_1_BG.png",
         "And me? A book. Somebody's kitchen. A clock I didn't look at. I remember being sure there would be time. I remember choosing.", 6.5f),
        ("res://assets/images/bgs/Truth_Ida_Walks.png",
         "Nobody came for her, so in the end she came for herself. Her own hand worried that latch loose. Her own feet took the road home.", 6.5f),
        ("res://assets/images/bgs/Truth_Ida_Walks.png",
         "“Ida, don't —” She never looked back. There was no stranger on that road. No monster waiting. There never was.", 6f),
        ("res://assets/images/bgs/Truth_Bridge.png",
         "The little bridge over the Harren canal, a child's walk from home. Its railing was older than both of us. Rotten through. Nobody's job to mend.", 6.5f),
        ("res://assets/images/bgs/Truth_Bridge.png",
         "It gave all at once, the way bad wood gives. She went into the black water in her yellow coat. The water keeps moving whether anyone crosses or not.", 7f),
        ("res://assets/images/bgs/Porch_447_BG.png",
         "4:47. I came through that gate calling her name — and the garden gave me back a swing, still moving, finishing its swing.", 6.5f),
        ("res://assets/images/bgs/BgRoom4.png",
         "That night our mother sat on the landing until morning, under the light she left for latecomers. All of us, waiting for a girl on a road.", 6.5f),
        ("res://assets/images/bgs/Apartment_Now_BG.png",
         "Afterwards I built a quieter life and came back here every night instead of living in either. I became the one on the swing. I just count in years.", 6.5f),
        ("res://assets/images/bgs/room_7_bg.png",
         "Nothing in those rooms was a monster. Only minutes, and who did not come in them. I did not come. Say it the way it was.", 6.5f),
    ];

    /// <summary>Room 7 calls this after a true Release; the choice is
    /// offered only once Mara (and the player) has seen the whole of it.</summary>
    public void PlayFinaleTruthReveal(Action onComplete)
    {
        if (_cutscenePlaying || _cutsceneRoot == null)
        {
            onComplete?.Invoke();
            return;
        }
        _activeShots = TruthShots;
        _cutsceneFadeFromWhite = true;
        _cutsceneOnComplete = onComplete;
        PlayCutsceneAsync();
    }

    /// <summary>First entry into Room 1: the house ASKS before it teaches.
    /// Either answer is remembered, so the question is only ever posed once;
    /// the cards themselves stay replayable from the pause menu forever.</summary>
    private void MaybeAutoShowTutorial(string scenePath)
    {
        if (_tutorialAutoShown || Settings.Instance == null || Settings.Instance.TutorialSeen)
            return;
        if (scenePath == null || !scenePath.EndsWith("room_1.tscn"))
            return;

        _tutorialAutoShown = true;
        GetTree().Paused = true;
        ShowConfirm(
            "Before the house settles around you — would you like to be shown how remembering works here? Six short cards: looking, Clarity, and what it costs when the light runs out.",
            () => OpenTutorial(),
            "Show me",
            "I can find my way",
            () =>
            {
                if (Settings.Instance != null && !Settings.Instance.TutorialSeen)
                {
                    Settings.Instance.TutorialSeen = true;
                    Settings.Instance.Save();
                }
                GetTree().Paused = false;
                ShowToast("As you were, then. How to Play waits in the pause menu (Esc) if the house gets strange.");
            });
    }
}


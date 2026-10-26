using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

/// <summary>
/// Run state + persistence (autoload).
/// Globals remains the raw field store the original scenes read/write;
/// GameState is the *logic* layer over it: corruption tiers, the clarity
/// economy, idempotent shard collection, the Memory Journal, objectives,
/// hints, stats and JSON saves at user://mara_voss_save.json.
/// UI (GameUI) and audio react to this class's events — game code just calls
/// the service methods (AddCorruption, CollectShard, …).
/// </summary>
public partial class GameState : Node
{
    public static GameState Instance { get; private set; }

    // ------------------------------------------------------------------ events
    public event Action<int, CorruptionTier> CorruptionChanged;
    public event Action<int> ClarityChanged;
    public event Action ShardsChanged;
    public event Action ObjectiveChanged;
    public event Action<string> ToastRequested;
    public event Action Saved;
    public event Action VerbsChanged;
    public event Action StoryFlagsChanged;

    // ---------------------------------------------------------------- constants
    public const string SavePath = "user://mara_voss_save.json";
    public const int MaxShards = 6;

    /// <summary>CORRUPTION_COUNT at which each tier begins (index = tier).</summary>
    private static readonly int[] TierThresholds = [0, 5, 11, 18];

    /// <summary>Corruption at which the MEMORY meter is empty (Corruption01 = 1).</summary>
    public static int ShatterThreshold => TierThresholds[^1] + 6;

    // Click discipline is now part of the memory economy. The old repeat-
    // examine cost was one corruption point; these are the requested
    // multipliers against that unit. The 2.3 cost is carried fractionally
    // so it really averages 2.3 instead of silently rounding every click.
    public const float NonInteractableClickPenalty = 2.0f;
    public const float RepeatedNonInteractableClickPenalty = 2.3f;
    public const float RepeatExaminePenalty = 3.0f;
    private float _clickCorruptionDebt;
    private string _lastLooseClickScene = string.Empty;
    private Vector2 _lastLooseClickPosition;
    private ulong _lastLooseClickMsec;

    /// <summary>Fired once when the memory shatters. GameUI plays the bad ending.</summary>
    public event Action MemoryShattered;
    private bool _shattered;

    // ------------------------------------------------------ clarity collapse
    /// <summary>Seconds of grace after the last Clarity charge is spent in an
    /// unfinished room before the house takes the room back entirely.</summary>
    public const float CollapseGraceSeconds = 15f;

    /// <summary>The countdown has begun (GameUI shows the takeover).</summary>
    public event Action CollapseStarted;
    /// <summary>Whole-second countdown updates for the on-screen timer.</summary>
    public event Action<float> CollapseTicked;
    /// <summary>Countdown over. True = the room was steadied in time.</summary>
    public event Action<bool> CollapseEnded;

    public bool CollapseActive { get; private set; }
    public float CollapseSecondsLeft { get; private set; }
    private string _collapseRoom;
    private int _collapseLastWholeSecond = -1;
    private float _collapseCorruptionAccumulator;

    /// <summary>Story flags that mean "this puzzle just gave way" — any of
    /// them steadies a collapsing room.</summary>
    private static readonly HashSet<string> CollapseRescueFlags = new()
    {
        "room4_sentence_complete", "room4_door_opened",
        "room5_anchored_lamp", "room5_drawer_opened",
    };
    public bool IsShattered => _shattered;

    private static readonly string[] TierNames = ["Intact", "Strained", "Fractured", "Shattered"];

    public static readonly string[] ShardTitles =
    [
        "The Brass Key",
        "The Photograph",
        "The Preserved Names",
        "The Hallway Sentence",
        "The Unsent Apology",
        "The Last Afternoon",
    ];

    /// <summary>Canonical journal text for every fragment. Collectors may
    /// pass richer in-scene wording; backfills for older Part 1 saves use
    /// these defaults so no recovered fragment is ever wordless.</summary>
    public static readonly string[] DefaultShardFragments =
    [
        "Every morning started here. Every morning she was already waiting, already loud, already herself. You could set your clock by her. That's not something you notice until it's gone.",
        "She used to fall asleep in that chair and think it was the best thing in the world — to fall asleep to the sound of someone else in the room. Someone else breathing. Someone turning a page.",
        "Ida kept spiders in jars and gave them names so she would not be afraid of them. Mara kept the jars. It is easier to preserve a thing than to admit the hands that labelled it are gone.",
        "\"She would have loved to see how far you've come.\" Their mother said it in the hallway, years too late, to a daughter who still measured every distance from the morning she did not come.",
        "Mara wrote the apology every October and never sent it. Not because there was no address. Because an unsent letter can still be answered by the house.",
        "That afternoon, Ida waited by the apple trees until waiting became walking. The swing was still moving when Mara remembered. By then the gate was open, and the road had already taken the sound away.",
    ];

    /// <summary>Scene path (file name) → tracker room name.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> SceneToRoom = new()
    {
        ["bedroom_prologue.tscn"] = "Bedroom",
        ["journey_to_room_2.tscn"] = "Room1",
        ["journey_to_room_3.tscn"] = "Room2",
        ["journey_to_room_4.tscn"] = "Room3",
        ["journey_to_porch.tscn"] = "Room6",
        ["room_1.tscn"] = "Room1",
        ["room_1_puzzle.tscn"] = "Room1",
        ["kitchen_cabinet.tscn"] = "Room1",
        ["room_2.tscn"] = "Room2",
        ["room_2_puzzle.tscn"] = "Room2",
        ["difference_investigation.tscn"] = "Room2",
        ["room_3.tscn"] = "Room3",
        ["jar_closeup.tscn"] = "Room3",
        ["room_4.tscn"] = "Room4",
        ["flashback_landing.tscn"] = "Room4",
        ["Room_4_puzzle.tscn"] = "Room4",
        ["room_5.tscn"] = "Room5",
        ["apartment_now.tscn"] = "Room5",
        ["lockpick_puzzle.tscn"] = "Room5",
        ["lockpick_rewind.tscn"] = "Room5",
        ["vent_crawl.tscn"] = "Room5",
        ["basement.tscn"] = "Room5",
        ["breaker_box.tscn"] = "Room5",
        ["room_6.tscn"] = "Room6",
        ["bridge_repair.tscn"] = "Room6",
        ["porch_447.tscn"] = "Room6",
        ["room_7.tscn"] = "Room7",
    };

    /// <summary>Tracker object name → player-facing phrase for hints/journal.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> PrettyObjectNames = new()
    {
        ["BreakerBox"] = "the backup-power box",
        ["Window"] = "the window",
        ["Cup2"] = "the second cup",
        ["Toast"] = "the toast",
        ["Note"] = "the note",
        ["Clock"] = "the clock",
        ["Calendar"] = "the calendar",
        ["Drawing"] = "the drawing",
        ["Bookshelf"] = "the bookshelf",
        ["Phone"] = "the phone",
        ["RightChair"] = "the right-hand chair",
        ["LeftChair"] = "the left-hand chair",
        ["Painting"] = "the painting",
        ["Globe"] = "the globe",
        ["Letter"] = "the letter",
        ["Bag"] = "the bag",
        ["Bed"] = "the bed",
        ["Mirror"] = "the hallway mirror",
        ["HallDrawer"] = "the hall drawer",
        ["Photographs"] = "the hallway photographs",
        ["RainWindow"] = "the rain window",
        ["Lamp"] = "the bedside lamp",
        ["AlarmClock"] = "the alarm clock",
        ["Desk"] = "the writing desk",
        ["RotaryPhone"] = "the rotary phone",
        ["Wardrobe"] = "the wardrobe",
        ["FloorPhotographs"] = "the photographs on the floor",
        ["AppleBasket"] = "the apple basket",
        ["Swing"] = "the swing",
        ["BrokenGate"] = "the broken garden gate",
        ["EmptyRoad"] = "the empty road",
        ["Mirror"] = "the hallway mirror",
        ["HallDrawer"] = "the hall table drawer",
        ["TurnedFrames"] = "the two turned frames at the end of the hall",
        ["LastPhotograph"] = "the last photograph",
        ["WallLight"] = "the wall light",
        ["CarpetRunner"] = "the carpet runner",
        ["UnopenedMail"] = "the unopened mail",
        ["SilentPhone"] = "the silent phone",
        ["CornerChair"] = "the chair in the corner",
        ["LitWindow"] = "the lit window",
        ["PorchSteps"] = "the porch steps",
        ["GardenPath"] = "the garden path",
        ["Furnace"] = "the cold furnace",
        ["PreserveShelves"] = "the preserve shelves",
        ["ToyCrate"] = "the toy crate",
        ["LockedTrunk"] = "the locked trunk",
        ["ToolCabinet"] = "the tool cabinet",
        ["VentDuct"] = "the vent overhead",
    };

    // -------------------------------------------------------------------- state
    /// <summary>Journal fragment bodies, per shard index ("" = not remembered).</summary>
    public string[] ShardFragments { get; } = ["", "", "", "", "", ""];

    /// <summary>Shard index placed in each Room 7 slot, -1 = empty.</summary>
    public int[] PlacedShardBySlot { get; } = [-1, -1, -1, -1, -1, -1];

    /// <summary>Display position → shard index for the Bag, the Journal's
    /// fragment list and Room 7's tray. Mara carries the fragments jumbled:
    /// where a piece happens to sit in her bag says nothing about where it
    /// belongs in her life, so the last room cannot be solved by reading
    /// left to right. Shuffled once per run (never the lived order itself),
    /// saved with the run, and re-shuffled on New Game.</summary>
    private int[] _fragmentDisplayOrder;

    public int[] FragmentDisplayOrder => EnsureFragmentDisplayOrder();

    private int[] EnsureFragmentDisplayOrder()
    {
        if (!IsValidDisplayOrder(_fragmentDisplayOrder))
            _fragmentDisplayOrder = ShuffledDisplayOrder();
        return _fragmentDisplayOrder;
    }

    private static bool IsValidDisplayOrder(int[] order)
    {
        if (order == null || order.Length != MaxShards)
            return false;
        var seen = new bool[MaxShards];
        bool identity = true;
        for (int i = 0; i < MaxShards; i++)
        {
            int shard = order[i];
            if (shard < 0 || shard >= MaxShards || seen[shard])
                return false;
            seen[shard] = true;
            if (shard != i)
                identity = false;
        }
        return !identity;
    }

    private static int[] ShuffledDisplayOrder()
    {
        var order = new int[MaxShards];
        var rng = new Random();
        do
        {
            for (int i = 0; i < MaxShards; i++)
                order[i] = i;
            for (int i = MaxShards - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        } while (!IsValidDisplayOrder(order));
        return order;
    }

    /// <summary>Recent dialogue lines (the journal's "Echoes"). Oldest first.</summary>
    public List<string> EchoLog { get; } = new();

    private readonly HashSet<string> _roomsCompleted = new();
    private readonly HashSet<MemoryVerb> _unlockedVerbs = new();
    private readonly HashSet<string> _storyFlags = new();

    /// <summary>The verb currently armed by the player (runtime mode, not saved).</summary>
    public MemoryVerb ActiveVerb { get; private set; } = MemoryVerb.None;

    /// <summary>Ending id completed in this run ("waking", "kept_house", "hollow"), or empty.</summary>
    public string CompletedEnding { get; private set; } = string.Empty;

    public int VerbUses { get; private set; }

    // Stats
    public double PlayTimeSeconds { get; private set; }
    public int ExaminesPerformed { get; private set; }
    public int ClarifiesUsed { get; private set; }
    public int CorruptionPeak { get; private set; }

    private string _pendingStartScene;
    private readonly System.Collections.Generic.Dictionary<string, int> _hintLevelByRoom = new();
    private string _lastHintKey = string.Empty;

    // ------------------------------------------------------------ clues & notes
    // The Memory Journal's working pages: clues the house has shown you are
    // auto-noted here as you examine their sources (digits, dates, the word),
    // and the arrangement clue assembles them once all four morning numbers
    // are found — puzzles should be solved by remembering, not guessing.

    private readonly List<string> _unlockedClues = new();
    public IReadOnlyList<string> UnlockedClues => _unlockedClues;
    public event Action CluesChanged;

    /// <summary>Free-form player notes (journal), persisted with the save.</summary>
    public string PlayerNotes { get; private set; } = string.Empty;

    // Recollection (spend-all Clarity reveal): rings persist across saves,
    // and the next room's Clarity is set by how much was revealed.
    private readonly HashSet<string> _persistentReveals = new(); // "Room|Object"
    private int? _pendingClarityCarry;
    private string _lastRoomName;

    /// <summary>Deepest main-path scene reached this run. Drives the
    /// "go back to the new room" forward button and sealed finished rooms.</summary>
    private string _furthestScenePath = string.Empty;
    private ulong _lastSealedNotifyMsec;

    /// <summary>Full path of the furthest main-path scene reached ("" = none yet).</summary>
    public string FurthestScenePath => _furthestScenePath;

    /// <summary>Extra examinables a Recollection can ring beyond the required set.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string[]> RecollectionExtrasByRoom = new()
    {
        ["Room1"] = ["CupExclude"],
    };

    /// <summary>The four numbers hidden in Room 1's morning, in padlock order.</summary>
    private static readonly string[] MorningClueIds = ["clock_hour", "calendar_day", "drawing_eight", "cup_number"];

    /// <summary>Where each morning number hides (for "still to find" hints).</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> MorningClueSources = new()
    {
        ["clock_hour"] = "the kitchen clock",
        ["calendar_day"] = "the circled calendar",
        ["drawing_eight"] = "the child's drawing",
        ["cup_number"] = "the cup with ink on its base (easy to pass by — it isn't one of the room's seven)",
    };

    private static readonly System.Collections.Generic.Dictionary<string, (string Title, string Text)> ClueDefinitions = new()
    {
        ["clock_hour"] = ("The kitchen clock",
            "Stopped at the time she left that morning. A lock that remembers a morning would keep the hour hand's number. (The clock itself is still on the kitchen wall — go and read it.)"),
        ["calendar_day"] = ("The circled calendar",
            "One October day, circled hard enough to tear the paper. A day of the month is not one number but two, side by side — it would take two dials to say it."),
        ["drawing_eight"] = ("The drawing",
            "She drew a number the way you do when you've just learned how — oversized, pleased with it, impossible to miss if you look at the drawing again."),
        ["cup_number"] = ("Ink on the cup",
            "A number written in ink on the base of a cup, like someone pressed the pen down too hard while writing something else nearby. Turn the cup over again if you need it told back to you."),
        ["morning_order"] = ("The morning, in order",
            "The drawer lock remembers that morning the way she does — in the order it happened:\n\n" +
            "the hour she left, the day circled on the calendar, the number she drew, the number under her cup.\n\n" +
            "(Five dials: the calendar gives two of them.)"),
        ["letter_home"] = ("Her parents' letter",
            "Read how the letter ends: it begs her to come back to the one place a person never really leaves. The table lock wants that word — four letters, the warm word, not the building's name."),
        ["kept_names"] = ("The preserved names",
            "Ida named what frightened her and kept it where she could see it. Preservation was her courage. In Mara, it became a way not to say goodbye."),
        ["hallway_sentence"] = ("The hallway sentence",
            "\"She would have loved to see how far you've come.\"\n\n" +
            "Echo does not invent the words. It only lets them arrive in the order they were spoken."),
        ["anchor_lamp"] = ("What stayed lit",
            "Something in that room was left on for whoever came home late — in the child's years and the woman's alike. It never changed sides, colour, or purpose.\n\n" +
            "Anchor remembers around what stays."),
        ["unsent_letters"] = ("The unsent apology",
            "Every October, another letter. Every letter begins, \"I should have been there at four.\" None was sent — an apology cannot be answered by the dead, so Mara addressed it to the house instead."),
        ["last_afternoon_trace"] = ("The way she went",
            "The garden remembers in causes, not pictures. Four stops, and each one told you its part: somewhere she waited with something gathered; somewhere she passed the minutes; somewhere she finally left; somewhere no one arrived.\n\n" +
            "An afternoon only runs one way. Trace is asking you to walk it that way."),
        ["fragment_order"] = ("One morning, in order",
            "The fragments belong in the order Mara lived them: a morning in the kitchen; a quiet room and someone breathing in it; a childhood kept and labelled; a sentence spoken in a hall; years of Octobers at a writing desk; one last afternoon.\n\n" +
            "Nothing in the last room is labelled. The house will accept any order it is offered. Only one of them is the order she lived."),
        ["blank_keys"] = ("The blank keys",
            "In the hall drawer: a locksmith's ring of keys that were never cut, and one key tag in a child's careful hand — \"for when I'm taller.\" Their father cut keys for half of Eldfen. He never got to cut hers.\n\n" +
            "Mara has opened this drawer a hundred times in the memory and closed it without seeing them. That is what this house does with the sharpest things: it lets her look straight at them, gently."),
        ["landing_evening"] = ("The landing, that evening",
            "The mirror keeps one evening the hallway will not say out loud: their mother sat on the landing floor until morning, facing a door that did not open. Nobody in that house ever spoke about the night itself again — only around it, in sentences like the one the photographs are still trying to finish.\n\n" +
            "And in every photograph on that wall, Ida's face is half-turned away. Not because she was shy. Because the memory can no longer give her back straight-on. Something in the house is editing — kindly, and constantly."),
        ["way_echo"] = ("A way of remembering — Echo: listen",
            "Ida named the spiders so fear could be spoken aloud. Echo is that courage turned into listening.\n\n" +
            "Choose Echo first (E, or its button in the Memory bar), then touch the memory. In the hallway, drift across the photographs rather than clicking them: a rising note means the next word of her mother's sentence. The brass door beyond them is not an Echo puzzle at all — it wants three ordinary keys, carried there in Mara's bag and put into their matching keyholes by hand.\n\n" +
            "A verb stays in Mara's hands while she is using it. Right-click or Esc puts it down."),
        ["way_anchor"] = ("A way of remembering — Anchor: hold still",
            "\"She would have loved to see how far you've come.\" One whole sentence gives Mara one thing that does not move when she repeats it.\n\n" +
            "Look at a room normally first, so its contradictions can speak. Then choose Anchor (A, or its Memory-bar button) and click the one thing whose meaning never changed. A wrong Anchor does not spend the verb; the object will show why it slides. When the right thing is held, Anchor is spent and ordinary hands can work in the stillness it made."),
        ["way_trace"] = ("A way of remembering — Trace: connect",
            "An apology is not one missed minute. It is the minute, and every minute that followed because of it.\n\n" +
            "Examine each stop normally first. When all of them have been faced, choose Trace (T, or its Memory-bar button) and click the same places again in the order the afternoon actually happened — each stop already told you whether she was waiting, passing time, leaving, or gone. Rising notes and the gold line mean the memory is joining. A wrong click breaks the line, so begin again where the waiting began."),
        ["way_release"] = ("A way of remembering — Release: decide",
            "Release wakes only when Mara knows the way Ida went. It is not a better way to examine, and it cannot put a life in order for her.\n\n" +
            "In the last room, choose a fragment from the tray, then click a hollow frame. The frames are not labelled and the house will not grade each piece as it lands — it answers only once, when the whole morning stands assembled. Click any filled frame to take its fragment back. Then choose Release (R, or its Memory-bar button), click the assembled memory, and decide: wake with Ida carried forward, or keep the house that pretends she is coming back."),
        ["apartment_now"] = ("The life that continued",
            "Outside the house there is an apartment with mail unopened behind the door, a phone no one calls, and a chair worn into the shape of waiting. Mara did not stop living at twenty-two. She only stopped letting the life be witnessed.\n\n" +
            "The house is not where she lives. It is where she goes instead."),
        ["bedroom_hairpin"] = ("Mum's bent hairpin",
            "One of her mother's hairpins, bent from years of being borrowed for jobs it was never made for. It waited in the little drawer of Mara's childhood nightstand, in the very first room — where a mother's hand left it after some bedtime repair nobody wrote down.\n\n" +
            "Thin enough to worry the seal of a jar the way she did — tapped true each time the working line crosses where the rubber gives, until the lid sighs instead of breaking. Small practical things travel farther in this house than grand gestures do."),
        ["hall_key_bridge"] = ("The copper bridge key",
            "Found beside the brass key in the kitchen drawer: a smaller copper key gone green at the edges, its bow cut like a little bridge — two posts, one arch.\n\n" +
            "It is not a fragment. It has actual weight and actual teeth, and somewhere ahead there is a keyhole under a bridge waiting for exactly this shape."),
        ["hall_key_door"] = ("The little door key",
            "Taped behind the sitting-room photograph, where it could only be found by remembering the room honestly first: a little iron key whose bow is a miniature door, complete with its own impossible knob.\n\n" +
            "It belongs in the middle keyhole of the hallway door, beneath the little-door plate."),
        ["hall_key_circle"] = ("The circle key",
            "At the bottom of the last preserve jar, under Ida's folded name tape: a round-bowed key with no beginning and no end to its ring.\n\n" +
            "It belongs in the final keyhole of the hallway door, beneath the empty brass circle."),
        ["difference_room"] = ("What changed",
            "Behind the sitting-room photograph, Mara remembered one ordinary evening for five seconds and refused the house's gentler version of it. The clock had already gone past four. A book had changed chairs. A letter had vanished from the table. A cup lay spilled.\n\n" +
            "Remembering it herself — changes and all — is what loosened the key taped behind the photograph."),
        ["fine_tension_wrench"] = ("For Mara's hands",
            "Wrapped in tissue under the apology letters: one flat strip of spring steel, thinner than anything in her father's roll, with his pencil on the tissue — FOR MARA HANDS.\n\n" +
            "Fitted to the lockpick set, deep pins read about a degree sooner and the pick trembles a little less under a wrong turn. It does not pick a lock for her. It only listens more kindly."),
        ["blackout_games"] = ("What the dark was for",
            "When the power went out in Eldfen, the girls were not frightened — officially. Officially it was a game: a torch under the chin, a sheet, the worst ghost voice a person could do, and Ida always braver in the telling than under the stairs. Their father never hurried. Cabinet, flashlight, basement box, light. A small liturgy a child could set her heart by.\n\n" +
            "The house went dark the moment the garden opened, and the wardrobe forgot October. To have the light back, Mara has to do what he did — in his order, with his things, down where the house keeps what it needs to stay alive."),
        ["under_house_vent"] = ("Under the house",
            "Low in the wall of the room that will not stay one room, a ventilation grille is held by a latch rather than honest screws. Cold air moves behind it, smelling of stone and old apples.\n\n" +
            "A latch like that wants two slender tools and patience — the kind of patience a locksmith's daughter watched at a workbench. The dark beyond it wants a light of its own. And somewhere below, at the end of the duct, sits the box her father always went down to when the house went dark."),
        ["vent_route"] = ("The way the house breathes",
            "Mara cannot picture the basement door. She has tried, torch in hand, in the middle of a blackout, and what answered was a pain behind one eye — the memory's own way of keeping that one door shut.\n\n" +
            "But the house breathes, and everything that breathes has more than one mouth. The low vent under the writing desk runs down with every other duct to the cold heart below: furnace, shelves, the kept things, and the breaker box her father always threw by his pencilled rule. Pick the grille's latch with his tools, crawl with his light, and set the little heart beating the way his card says — three thumps, for the rooms that get up in the morning."),
        ["father_lockpicks"] = ("Father's lockpicks",
            "An oilcloth roll of slender picks and one flat tension wrench, kept clean long after the hands that used them were gone. Mara never once saw him break one; in her memory, they simply do not break.\n\n" +
            "The pick follows the mouse around the top of the lock, locked to it — no sliding, no drift. Hold A (or D) to turn the cylinder. How far it turns before it starts to shake is the whole secret: barely turning means the pick is far from the pins' truth; turning most of the way means you are close. Let go and the unset layer springs back; a layer turned all the way home clicks set like a little lock inside the lock, and stays set. Deeper layers sit narrower. Force only makes the pick tremble and complain — ease off, reseat it, listen again."),
        ["apartment_flashlight"] = ("A light for under the house",
            "Her father's flashlight, heavy as a small hammer, from the top shelf of the kitchen cabinet — the one he could find one-handed in any blackout while two girls pretended not to be frightened downstairs. The memory kept it charged the way it keeps all his things: ready.\n\n" +
            "Once it has been clicked into her Bag, move the mouse to aim its beam in the dark rooms, the duct, below the house, and over anything she is holding in her hand; until then, her hand stays dark. Hold W to crawl forward through the vent; release it and Mara stops where she is. Sweep the beam slowly in the basement; clicking at darkness she has not actually seen still strains the memory."),
        ["iron_cellar_key"] = ("The iron cellar key",
            "Their father kept a spare below-stairs key where a child was trusted not to look: beneath the loose stone by the garden gate.\n\n" +
            "It fits the little tool cabinet under the house — not the trunk. Some locks in that family were meant for keys. Others were only ever opened by knowing how they felt inside."),
        ["basement_negatives"] = ("Straight-on",
            "In the basement trunk, a tin of photograph negatives their mother never had printed. Held to the flashlight, Ida is looking straight into the camera in every one — not half-turned, not edited gentle by the house.\n\n" +
            "Mara cannot keep her that way. But for one breath she can know the straight-on face was real, and that the turning-away is only what twelve years have done to the remembering."),
        ["negatives_cellar_frame"] = ("A map in reverse",
            "One of the unprinted frames is not of the garden at all: their father in the cellar lamplight, hanging his claw hammer on the inside of the little tool cabinet's door.\\n\\n" +
            "The negatives are the only pictures of the rooms as they were actually kept — which makes them, held to any light, a map. What the mending of a crossing needs sleeps on the inside of that door."),
        ["porch_447"] = ("The porch, at 4:47",
            "The light was on. The garden was empty. Between the garden and the last room there is a porch Mara has crossed a thousand times in the memory without ever stopping on it: the exact place where being late became being too late.\n\n" +
            "She called from the steps until her voice gave out. The house has been answering for Ida ever since."),
    };

    // ---------------------------------------------------------------- lifecycle

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        SceneManager.SceneChanged += OnSceneChanged;
        RoomExamineTracker.ExamineRecorded += OnExamineRecorded;
        Logger.Info("GameState ready.");
    }

    public override void _Process(double delta)
    {
        if (GetTree().Paused)
            return;

        if (SceneManager.Instance?.CurrentScene != null)
            PlayTimeSeconds += delta;

        TickCollapse(delta);
    }

    /// <summary>The 15-second grace. Time only drains while Mara can actually
    /// act: never while paused, mid-dialogue, behind a panel, or between
    /// scenes. Corruption seeps in every few seconds while it runs.</summary>
    private void TickCollapse(double delta)
    {
        if (!CollapseActive || _shattered)
            return;
        if (SceneManager.IsChanging || Dialogue.IsShowing || Dialogue.IsInputBlocked)
            return;
        if (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen())
            return;

        CollapseSecondsLeft -= (float)delta;

        _collapseCorruptionAccumulator += (float)delta;
        if (_collapseCorruptionAccumulator >= 3f)
        {
            _collapseCorruptionAccumulator -= 3f;
            AddCorruption(1); // may shatter early if the memory was already frayed
            if (!CollapseActive)
                return;
        }

        int whole = Mathf.CeilToInt(Mathf.Max(CollapseSecondsLeft, 0f));
        if (whole != _collapseLastWholeSecond)
        {
            _collapseLastWholeSecond = whole;
            CollapseTicked?.Invoke(CollapseSecondsLeft);
        }

        if (CollapseSecondsLeft <= 0f)
            ExpireCollapse();
    }

    private void MaybeBeginCollapse()
    {
        if (CollapseActive || _shattered || ClarityCount > 0)
            return;

        string room = CurrentRoomName();
        // Clarity only has false surfaces to lift in the TileMap rooms; the
        // collapse is those rooms' punishment for spending the last charge
        // before the room has been faced and its lock opened.
        if (room is not ("Room1" or "Room2" or "Room3"))
            return;
        if (IsRoomCompleted(room))
            return;
        int shardIndex = room == "Room1" ? 0 : room == "Room2" ? 1 : 2;
        if (IsShardRecovered(shardIndex))
            return;

        CollapseActive = true;
        _collapseRoom = room;
        CollapseSecondsLeft = CollapseGraceSeconds;
        _collapseLastWholeSecond = (int)CollapseGraceSeconds;
        _collapseCorruptionAccumulator = 0f;
        CollapseStarted?.Invoke();
        CollapseTicked?.Invoke(CollapseSecondsLeft);
    }

    /// <summary>Locks, drawers and doors call this when they give way: a
    /// solved puzzle steadies the room before the house can take it.</summary>
    public void NotifyPuzzleSolved()
    {
        if (CollapseActive)
            EndCollapse(rescued: true);
    }

    private void EndCollapse(bool rescued, bool silent = false)
    {
        if (!CollapseActive)
            return;
        CollapseActive = false;
        _collapseRoom = null;
        CollapseEnded?.Invoke(rescued);
        if (rescued && !silent)
            Toast("The room steadies — barely. The house lets go of it, for now.");
    }

    private void ExpireCollapse()
    {
        EndCollapse(rescued: false, silent: true);
        // The count ran out: the room is taken, and the taking breaks the
        // whole memory with it.
        int need = ShatterThreshold - (Globals.Instance?.CORRUPTION_COUNT ?? 0);
        if (need > 0)
            AddCorruption(need);
        else if (!_shattered)
        {
            _shattered = true;
            MemoryShattered?.Invoke();
        }
    }

    private void OnSceneChanged(string scenePath)
    {
        AudioManager.Instance?.PlayMusicForScene(scenePath);
        EnsureFinalActProgress();

        // Forward progress: remember the deepest main-path room reached, so
        // stepping back into finished rooms can offer the way forward again.
        if (IsMainPathScene(scenePath) &&
            SceneProgressIndex(scenePath) > SceneProgressIndex(_furthestScenePath))
        {
            _furthestScenePath = scenePath;
        }

        // Fleeing a collapsing room abandons it to the house (room <-> its
        // puzzle close-up is the same room and never counts as fleeing).
        if (CollapseActive)
        {
            string nowRoom = CurrentRoomName();
            if (nowRoom != null && nowRoom != _collapseRoom)
            {
                EndCollapse(rescued: false, silent: true);
                AddCorruption(2);
                Toast("You leave that room to the house. It will not be the same when you return.");
            }
        }

        // A Recollection spent in the previous room sets this room's Clarity.
        string room = CurrentRoomName();
        if (room != null && room != _lastRoomName)
        {
            // First room after a load/new game: just note it. A pending carry
            // belongs to the NEXT room, never the one it was spent in.
            bool firstRoomSinceLoad = _lastRoomName == null;
            if (!firstRoomSinceLoad && _pendingClarityCarry.HasValue && Globals.Instance != null)
            {
                Globals.Instance.CLARITY_TOGGLE_COUNT = Mathf.Clamp(_pendingClarityCarry.Value, 0, Globals.MaxClarity);
                _pendingClarityCarry = null;
                ClarityChanged?.Invoke(ClarityCount);
                Toast($"The remembering cost you. Clarity here: {ClarityCount}.");
            }
            _lastRoomName = room;
        }

        if (IsCurrentSceneSealed())
            Toast("This room has given what it had. It rests now.");

        ObjectiveChanged?.Invoke();
        SaveGame("scene change");
    }

    private void OnExamineRecorded(string roomName, string objectName)
    {
        ExaminesPerformed++;
        _hintLevelByRoom.Remove(roomName); // progress resets progressive hints
        ObjectiveChanged?.Invoke();

        // Examining a clue's source notes it in the journal automatically.
        switch ((roomName, objectName))
        {
            case ("Room1", "Clock"): UnlockClue("clock_hour"); break;
            case ("Room1", "Calendar"): UnlockClue("calendar_day"); break;
            case ("Room1", "Drawing"): UnlockClue("drawing_eight"); break;
            case ("Room1", "CupExclude"): UnlockClue("cup_number"); break;
            case ("Room2", "Letter"): UnlockClue("letter_home"); break;
            case ("Room4", "HallDrawer"): UnlockClue("blank_keys"); break;
            case ("Room3", "Jars"): UnlockClue("kept_names"); break;
            case ("Room5", "Lamp"): UnlockClue("anchor_lamp"); break;
            case ("Room5", "Desk"): UnlockClue("unsent_letters"); break;
        }

        // The garden's arrangement clue assembles only once every stop on
        // Ida's last afternoon has been faced.
        if (roomName == "Room6" && RoomExamineTracker.HasRoomBeenFullyExamined("Room6"))
            UnlockClue("last_afternoon_trace");

        // The landing flashback writes itself down once all of it is faced.
        if (roomName == "Room4Flashback" && RoomExamineTracker.HasRoomBeenFullyExamined("Room4Flashback"))
            UnlockClue("landing_evening");

        if (roomName == "ApartmentNow" && RoomExamineTracker.HasRoomBeenFullyExamined("ApartmentNow"))
            UnlockClue("apartment_now");

        if (roomName == "Porch447" && RoomExamineTracker.HasRoomBeenFullyExamined("Porch447"))
            UnlockClue("porch_447");
    }

    public bool HasClue(string clueId) => clueId != null && _unlockedClues.Contains(clueId);

    public bool TryGetClue(string clueId, out string title, out string text)
    {
        if (clueId != null && ClueDefinitions.TryGetValue(clueId, out var def))
        {
            title = def.Title;
            text = def.Text;
            return true;
        }
        title = string.Empty;
        text = string.Empty;
        return false;
    }

    /// <summary>Notes a clue in the journal (idempotent). When the fourth
    /// morning number lands, the arrangement clue assembles itself.</summary>
    public bool UnlockClue(string clueId)
    {
        if (string.IsNullOrEmpty(clueId) || !ClueDefinitions.ContainsKey(clueId) || _unlockedClues.Contains(clueId))
            return false;

        _unlockedClues.Add(clueId);
        CluesChanged?.Invoke();
        AudioManager.Play(Sfx.PageTurn, -11f, 1.15f);
        Toast(clueId == "morning_order"
            ? "The arrangement is in your journal (J)."
            : _unlockedClues.Count == 1 ? "Clue noted — J opens your journal." : "Noted in your journal.");
        SaveGame($"clue {clueId}");

        if (clueId != "morning_order")
        {
            bool allMorning = true;
            foreach (var id in MorningClueIds)
            {
                if (!_unlockedClues.Contains(id))
                {
                    allMorning = false;
                    break;
                }
            }
            if (allMorning)
                UnlockClue("morning_order");
        }

        return true;
    }

    /// <summary>Saves written before the clue journal existed: derive noted
    /// clues from examine progress so returning players aren't locked out of
    /// clues whose layers they already clicked. (Uses raw clicked-layer data:
    /// HasObjectBeenFullyExamined always says yes for *Exclude objects.)</summary>
    private void BackfillCluesFromProgress()
    {
        void AddIfExamined(string room, string obj, string clueId)
        {
            if (RoomExamineTracker.GetClickedLayers(room, obj) != null
                && ClueDefinitions.ContainsKey(clueId) && !_unlockedClues.Contains(clueId))
                _unlockedClues.Add(clueId);
        }

        AddIfExamined("Room1", "Clock", "clock_hour");
        AddIfExamined("Room1", "Calendar", "calendar_day");
        AddIfExamined("Room1", "Drawing", "drawing_eight");
        AddIfExamined("Room1", "CupExclude", "cup_number");
        AddIfExamined("Room2", "Letter", "letter_home");
        AddIfExamined("Room3", "Jars", "kept_names");
        AddIfExamined("Room5", "Lamp", "anchor_lamp");
        AddIfExamined("Room5", "Desk", "unsent_letters");
        if (RoomExamineTracker.HasRoomBeenFullyExamined("Room6") && !_unlockedClues.Contains("last_afternoon_trace"))
            _unlockedClues.Add("last_afternoon_trace");
        if (RoomExamineTracker.HasRoomBeenFullyExamined("ApartmentNow") && !_unlockedClues.Contains("apartment_now"))
            _unlockedClues.Add("apartment_now");
        if (RoomExamineTracker.HasRoomBeenFullyExamined("Porch447") && !_unlockedClues.Contains("porch_447"))
            _unlockedClues.Add("porch_447");
        if (HasFlag("room5_vent_found") && !_unlockedClues.Contains("under_house_vent"))
            _unlockedClues.Add("under_house_vent");
        if (HasFlag("item_hairpin") && !_unlockedClues.Contains("bedroom_hairpin"))
            _unlockedClues.Add("bedroom_hairpin");
        if (HasFlag("item_hall_key_bridge") && !_unlockedClues.Contains("hall_key_bridge"))
            _unlockedClues.Add("hall_key_bridge");
        if (HasFlag("item_hall_key_door") && !_unlockedClues.Contains("hall_key_door"))
            _unlockedClues.Add("hall_key_door");
        if (HasFlag("item_hall_key_circle") && !_unlockedClues.Contains("hall_key_circle"))
            _unlockedClues.Add("hall_key_circle");
        if (HasFlag("difference_room2_solved") && !_unlockedClues.Contains("difference_room"))
            _unlockedClues.Add("difference_room");
        if (HasFlag("item_lockpick_set") && !_unlockedClues.Contains("father_lockpicks"))
            _unlockedClues.Add("father_lockpicks");
        if (HasFlag("blackout_began") && !_unlockedClues.Contains("blackout_games"))
            _unlockedClues.Add("blackout_games");
        if (HasFlag("item_fine_tension_wrench") && !_unlockedClues.Contains("fine_tension_wrench"))
            _unlockedClues.Add("fine_tension_wrench");
        if (HasFlag("item_flashlight") && !_unlockedClues.Contains("apartment_flashlight"))
            _unlockedClues.Add("apartment_flashlight");
        if (HasFlag("item_iron_cellar_key") && !_unlockedClues.Contains("iron_cellar_key"))
            _unlockedClues.Add("iron_cellar_key");
        if (HasFlag("basement_negatives_taken") && !_unlockedClues.Contains("basement_negatives"))
            _unlockedClues.Add("basement_negatives");

        bool allMorning = true;
        foreach (var id in MorningClueIds)
        {
            if (!_unlockedClues.Contains(id))
            {
                allMorning = false;
                break;
            }
        }
        if (allMorning && !_unlockedClues.Contains("morning_order"))
            _unlockedClues.Add("morning_order");
    }

    public void SetPlayerNotes(string text) => PlayerNotes = text ?? string.Empty;

    public bool IsRevealedPersistent(string roomName, string objectName)
        => roomName != null && objectName != null && _persistentReveals.Contains($"{roomName}|{objectName}");

    /// <summary>Everything in this room a Recollection could ring: required
    /// objects plus clue sources, minus what has already been examined.</summary>
    public List<string> GetRecollectionRemaining(string roomName, out int examinedCount, out int totalCount)
    {
        var remaining = new List<string>();
        examinedCount = 0;
        totalCount = 0;
        if (roomName == null || !RoomExamineTracker.RequiredObjectsByRoom.TryGetValue(roomName, out var required))
            return remaining;

        var all = new List<string>(required);
        if (RecollectionExtrasByRoom.TryGetValue(roomName, out var extras))
            all.AddRange(extras);

        totalCount = all.Count;
        foreach (var obj in all)
        {
            if (RoomExamineTracker.GetClickedLayers(roomName, obj) != null)
                examinedCount++;
            else
                remaining.Add(obj);
        }
        return remaining;
    }

    /// <summary>"Remember it all": a nearly-known room (at least 60%
    /// examined, never more than 5 required) and >= 3 Clarity charges.
    /// Offered when Clarity is armed, and always visible as its own HUD
    /// button while it is available.</summary>
    public bool CanRecollect(out List<string> remaining, out int nextRoomClarity)
    {
        remaining = new List<string>();
        nextRoomClarity = 0;
        if (_shattered || ClarityCount < 3)
            return false;

        string room = CurrentRoomName();
        if (room == null)
            return false;

        // Rooms 4-6 are built around Memory Verbs and authored hotspots,
        // not TileMap Clarify layers (Room 4's mirror/drawer/frames included).
        // Never offer a Recollection Clarity could not visually fulfil.
        if (room is "Room4" or "Room5" or "Room6")
            return false;

        remaining = GetRecollectionRemaining(room, out int examined, out int total);
        if (total == 0 || remaining.Count == 0)
            return false;

        int need = Mathf.Min(5, Mathf.CeilToInt(total * 0.6f));
        if (examined < need)
            return false;

        nextRoomClarity = Mathf.Clamp(remaining.Count + 1, 0, Globals.MaxClarity);
        return true;
    }

    /// <summary>Spend ALL Clarity: show everything left in its true form and ring it until it
    /// is faced. The next room's Clarity is set to (revealed + 1): one
    /// revealed, two; two revealed, three. Returns how many were revealed.
    /// Spending down to zero starts the collapse in an unfinished room,
    /// exactly like spending the last ordinary charge does.</summary>
    public int PerformRecollection(bool announce = true)
    {
        if (Globals.Instance == null || !CanRecollect(out var remaining, out int carry))
            return 0;

        string room = CurrentRoomName();
        foreach (var obj in remaining)
            _persistentReveals.Add($"{room}|{obj}");

        Globals.Instance.CLARITY_TOGGLE_COUNT = 0;
        ClarifiesUsed++;
        _pendingClarityCarry = carry;
        ClarityChanged?.Invoke(0);

        // Remembering spends the light down to nothing — and emptied light
        // is emptied light, however it was spent. While this room's lock is
        // still shut, the house starts its count just as it does for the
        // last ordinary Clarify. The count itself holds while the reveal
        // plays (input is blocked), so the fifteen seconds are all hers.
        MaybeBeginCollapse();
        if (announce)
        {
            AudioManager.Play(Sfx.ClarifyReveal, -3f);
            Toast(remaining.Count == 1
                ? "One last thing shows itself. All Clarity spent."
                : $"The room gives up its remaining {remaining.Count} secrets. All Clarity spent.");
        }
        SaveGame("recollection");
        return remaining.Count;
    }

    private int NextHintLevel(string key)
    {
        int level = Mathf.Min(_hintLevelByRoom.GetValueOrDefault(key, 0) + 1, 3);
        _hintLevelByRoom[key] = level;
        return level;
    }

    /// <summary>Level-2 padlock hint: what the journal holds vs what is missing.</summary>
    private string MorningClueStatus()
    {
        var found = new List<string>();
        var missing = new List<string>();
        foreach (var id in MorningClueIds)
        {
            if (HasClue(id) && TryGetClue(id, out string title, out _))
                found.Add(title);
            else
                missing.Add(MorningClueSources[id]);
        }

        if (missing.Count == 0)
            return "All four numbers are noted in your journal (J, Notes & Clues). What order did the morning happen in?";

        string foundText = found.Count > 0 ? $"Noted: {string.Join(" · ", found)}. " : string.Empty;
        return $"{foundText}Still to find: {string.Join(", ", missing)}. Step Back (Q) and look again — your journal (J) keeps what you find.";
    }

    // -------------------------------------------------------------------- rooms

    private static string FileNameOf(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    private static Variant Get(Dictionary data, string key, Variant fallback = default)
    {
        return data != null && data.ContainsKey(key) ? data[key] : fallback;
    }

    public static string CurrentRoomName()
    {
        string path = SceneManager.CurrentScenePath;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string file = FileNameOf(path);
        return SceneToRoom.TryGetValue(file, out var room) ? room : null;
    }

    /// <summary>The tracker room a scene belongs to (same fold as
    /// <see cref="CurrentRoomName"/>, for any path).</summary>
    public static string RoomNameForScene(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return null;
        string file = FileNameOf(scenePath);
        return SceneToRoom.TryGetValue(file, out var room) ? room : null;
    }

    /// <summary>
    /// The practical things kept in each early room beside its fragment.
    /// A fragment alone is enough to leave by — Mara may walk out with
    /// the memory and without the metal — but what was left behind stays
    /// where it was kept, and the puzzles ahead will ask for it by name.
    /// This one table drives three honest systems: the "look around a
    /// little more?" prompt when leaving a finished room light, the
    /// sealed-room exception that lets her come back for what she
    /// missed, and nothing else. Items are never granted from it.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, string[]> RoomPracticalItems = new()
    {
        ["Bedroom"] = ["item_hairpin"],
        ["Room1"] = ["item_hall_key_bridge"],
        ["Room2"] = ["item_hall_key_door"],
        ["Room3"] = ["item_hall_key_circle"],
        ["Room4"] = ["item_lockpick_set"],
    };

    /// <summary>Fragment index earned in each practical-item room.</summary>
    private static int ShardIndexForRoom(string room) => room switch
    {
        "Room1" => 0,
        "Room2" => 1,
        "Room3" => 2,
        "Room4" => 3,
        "Room5" => 4,
        "Room6" => 5,
        _ => -1,
    };

    /// <summary>True while a room still keeps a practical thing Mara has
    /// not picked up. Sealed rooms consult this before refusing her:
    /// a finished memory goes quiet, but an untaken key stays takeable.</summary>
    public bool RoomHasUnclaimedPracticalItem(string room)
    {
        if (room == null || !RoomPracticalItems.TryGetValue(room, out var flags))
            return false;
        foreach (var flag in flags)
        {
            if (!HasFlag(flag))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Asked by SceneManager before it honours forward travel: is Mara
    /// about to walk out of a room whose fragment she has, leaving a
    /// practical thing behind? She is always allowed to — this only
    /// gives her the chance to say "maybe I should look around a little
    /// more" first. Never fires stepping back, within a room's own
    /// close-ups, before the fragment, or during the blackout trek
    /// (when crossing the whole dark house is the point).
    /// </summary>
    /// <summary>Flag recording that Mara has already asked herself, out
    /// loud, whether she is ready to leave this room. She asks once per
    /// room, not every time she crosses it.</summary>
    public static string LeaveAskedFlag(string roomName) =>
        "leave_asked_" + (roomName ?? "room").ToLowerInvariant();

    public void MarkLeavePromptShown(string roomName)
    {
        if (!string.IsNullOrEmpty(roomName))
            SetFlag(LeaveAskedFlag(roomName));
    }

    public bool TryGetLeavePrompt(string fromScene, string toScene, out string roomName)
    {
        roomName = RoomNameForScene(fromScene);
        if (roomName == null || !RoomPracticalItems.ContainsKey(roomName))
            return false;
        if (HasFlag(LeaveAskedFlag(roomName)))
            return false;
        if (HasFlag("blackout_active"))
            return false;

        int shard = ShardIndexForRoom(roomName);
        if (shard < 0 || !IsShardRecovered(shard))
            return false;
        if (!RoomHasUnclaimedPracticalItem(roomName))
            return false;

        // Forward only, and not into one of this room's own close-ups.
        // Journey thresholds borrow the room behind them in SceneToRoom,
        // so they must count as leaving, not as staying.
        if (SceneProgressIndex(toScene) <= SceneProgressIndex(fromScene))
            return false;
        string toFile = FileNameOf(toScene ?? string.Empty);
        bool isJourney = toFile.StartsWith("journey_", System.StringComparison.Ordinal);
        if (!isJourney && RoomNameForScene(toScene) == roomName)
            return false;

        return true;
    }

    public static string PrettyObjectName(string objectName)
    {
        if (objectName != null && PrettyObjectNames.TryGetValue(objectName, out var pretty))
            return pretty;
        return "the " + (objectName ?? "room").ToLowerInvariant();
    }

    // ------------------------------------------------- progression & sealing
    // The main path, in order. Side places (the landing flashback, the
    // present-day apartment) borrow their parent room's place in line.
    private static readonly string[] MainPathSceneFiles =
    [
        "bedroom_prologue.tscn",
        "room_1.tscn", "room_1_puzzle.tscn", "journey_to_room_2.tscn",
        "room_2.tscn", "room_2_puzzle.tscn", "journey_to_room_3.tscn",
        "room_3.tscn", "journey_to_room_4.tscn",
        "room_4.tscn", "Room_4_puzzle.tscn",
        "room_5.tscn",
        "room_6.tscn", "journey_to_porch.tscn", "bridge_repair.tscn", "porch_447.tscn",
        "room_7.tscn",
    ];

    private static bool IsMainPathScene(string scenePath)
    {
        string file = FileNameOf(scenePath ?? string.Empty);
        return System.Array.IndexOf(MainPathSceneFiles, file) >= 0;
    }

    /// <summary>Position of a scene along the main path (-1 = not on it).
    /// Side places resolve to the room they branch from.</summary>
    public static int SceneProgressIndex(string scenePath)
    {
        string file = FileNameOf(scenePath ?? string.Empty);
        file = file switch
        {
            "difference_investigation.tscn" => "room_2_puzzle.tscn",
            "jar_closeup.tscn" => "room_3.tscn",
            "kitchen_cabinet.tscn" => "room_1.tscn",
            "flashback_landing.tscn" => "room_4.tscn",
            "apartment_now.tscn" => "room_5.tscn",
            "lockpick_puzzle.tscn" => "room_5.tscn",
            "lockpick_rewind.tscn" => "room_5.tscn",
            "vent_crawl.tscn" => "room_5.tscn",
            "basement.tscn" => "room_5.tscn",
            "breaker_box.tscn" => "room_5.tscn",
            _ => file,
        };
        return System.Array.IndexOf(MainPathSceneFiles, file);
    }

    /// <summary>The Leave button's target: once a room's fragment is
    /// Mara's, the next room is hers to walk to whenever she chooses —
    /// collecting a fragment never carries her anywhere by itself.
    /// Rooms whose exit is a thing in the world keep that exit
    /// instead: Room 4's brass door, the porch's front door, the
    /// bedroom's tutorial door. The first-departure walks and the
    /// once-per-room "am I ready to leave?" ride along inside
    /// SceneManager.ChangeScene.</summary>
    public bool TryGetLeaveScene(out string scenePath)
    {
        scenePath = string.Empty;
        string file = FileNameOf(SceneManager.CurrentScenePath ?? string.Empty);
        string next = file switch
        {
            "room_1.tscn" or "room_1_puzzle.tscn" => IsShardRecovered(0) ? "room_2.tscn" : null,
            "room_2.tscn" or "room_2_puzzle.tscn" => IsShardRecovered(1) ? "room_3.tscn" : null,
            "room_3.tscn" or "jar_closeup.tscn" => IsShardRecovered(2) ? "room_4.tscn" : null,
            // The afternoon's Leave stands open from the moment the
            // fragment is hers — the button names the wish early.
            // The walking of it stays the wardrobe's: GameUI hands
            // this target back to Room5Final.LeaveForAfternoon,
            // which the blackout still bars until power returns.
            "room_5.tscn" => IsShardRecovered(4) ? "room_6.tscn" : null,
            // No Leave shortcut from Room 6 to the porch: that road
            // is authored — front door, walked journey, and the
            // bridge her father's tools must mend first. A one-click
            // "Leave for the porch" stepped clean over all of it.
            _ => null,
        };
        if (next == null)
            return false;
        scenePath = "res://scenes/world/" + next;
        return true;
    }

    /// <summary>Tracker room for a scene file, keeping side places distinct
    /// (CurrentRoomName folds them into their parent room on purpose).</summary>
    private static string SceneTrackerRoomName(string scenePath)
    {
        string file = FileNameOf(scenePath ?? string.Empty);
        return file switch
        {
            "flashback_landing.tscn" => "Room4Flashback",
            "apartment_now.tscn" => "ApartmentNow",
            "basement.tscn" => "Basement",
            // The lockpick close-up, its Clarity rewind and the vent are
            // thresholds/puzzles, not rooms whose loose clicks should
            // use a tracker-room penalty.
            "lockpick_puzzle.tscn" => null,
            "lockpick_rewind.tscn" => null,
            "vent_crawl.tscn" => null,
            // The investigation grades its own clicks; it is a memory
            // test, not a room of examinables to loose-click through.
            "difference_investigation.tscn" => null,
            // Item close-ups own their clicks the same way: a jar, a
            // cabinet and a breaker box are puzzles, not examinables.
            "jar_closeup.tscn" => null,
            "kitchen_cabinet.tscn" => null,
            "breaker_box.tscn" => null,
            // The bridge is a repair, not a room of examinables; its
            // mistimed strikes are graded by the crossing itself.
            "bridge_repair.tscn" => null,
            "porch_447.tscn" => "Porch447",
            // Journeys borrow the completed room for Clarity/progress, but
            // they are thresholds, not rooms that can rest/seal.
            "journey_to_room_2.tscn" => null,
            "journey_to_room_3.tscn" => null,
            "journey_to_room_4.tscn" => null,
            "journey_to_porch.tscn" => null,
            _ => SceneToRoom.TryGetValue(file, out var room) ? room : null,
        };
    }

    private bool RoomFullyDone(string trackerRoom) => trackerRoom switch
    {
        "Bedroom" => HasFlag("prologue_from_bedroom"),
        "Room1" => IsShardRecovered(0),
        "Room2" => IsShardRecovered(1),
        "Room3" => IsShardRecovered(2),
        "Room4" => IsShardRecovered(3) && HasFlag("room4_door_opened"),
        "Room4Flashback" => HasFlag("flashback_landing_seen"),
        "Room5" => IsShardRecovered(4),
        "ApartmentNow" => HasFlag("apartment_seen"),
        "Basement" => HasFlag("basement_seen"),
        "Room6" => IsShardRecovered(5),
        "Porch447" => HasFlag("porch_passed"),
        _ => false,
    };

    /// <summary>A finished room the player has moved beyond: it can be
    /// looked at, but nothing in it answers anymore. Navigation (Step Back /
    /// Leave) is never sealed, and neither is a room still being worked —
    /// stepping back mid-puzzle to re-read a clue stays possible.</summary>
    public bool IsRoomSealed(string trackerRoom)
    {
        if (string.IsNullOrEmpty(trackerRoom) || !RoomFullyDone(trackerRoom))
            return false;
        int here = SceneProgressIndex(SceneManager.CurrentScenePath);
        int furthest = SceneProgressIndex(_furthestScenePath);
        return furthest > here;
    }

    public bool IsCurrentSceneSealed()
    {
        return IsRoomSealed(SceneTrackerRoomName(SceneManager.CurrentScenePath));
    }

    /// <summary>Throttled refusal for clicks inside a sealed room.</summary>
    public void NotifySealedInteraction()
    {
        ulong now = Time.GetTicksMsec();
        if (now - _lastSealedNotifyMsec < 1600)
            return;
        _lastSealedNotifyMsec = now;
        AudioManager.Play(Sfx.Denied, -11f, 0.9f);
        Toast("Nothing here answers anymore.");
    }

    public string GetCurrentObjective()
    {
        string path = SceneManager.CurrentScenePath;
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string file = FileNameOf(path);
        string room = CurrentRoomName();

        if (file == "lockpick_rewind.tscn")
            return "Back through every place she crossed — until the kitchen cabinet, and the hand that knew it.";

        // While the house is out of power, every room has the same small
        // liturgy to finish: cabinet, flashlight, basement box, light.
        if (HasFlag("blackout_active") && !HasFlag("backup_power_on"))
        {
            if (file == "kitchen_cabinet.tscn")
                return "Top shelf. By touch, the way he always found it.";
            if (file == "breaker_box.tscn")
                return "Set the breakers like Father's picture. Then the big handle.";
            if (!HasFlag("item_flashlight"))
                return "Dark. Father kept a flashlight in the kitchen cabinet — the one Mara swears was never there. Step Back through the dark rooms to the kitchen, and try anyway.";
            if (!HasFlag("blackout_vent_noticed"))
                return "Light in hand. Get back to the room that will not stay one room — the way down is somewhere in it.";
            if (!HasFlag("room5_vent_open"))
                return "The low vent is the way down. Father's latch, Father's tools.";
            return "Under the house: find the backup-power box and bring the light back. Then the wardrobe can remember the garden.";
        }

        if (file == "room_2.tscn" && HasFlag("difference_room2_solved") && !HasFlag("item_hall_key_door"))
            return "Something small and metal is waiting where the evening set it down — on the little table beside the globe, where the letter used to live. Click it and take it up.";
        if (file == "room_2.tscn" && IsShardRecovered(1) && !HasFlag("difference_room2_solved"))
            return "The photograph in her bag (B) remembers this room right. Look at it here — something small is taped behind what the house edited.";
        if (file == "bridge_repair.tscn")
            return HasFlag("item_hammer_nails")
                ? "Nail the bridge true again. Strike each nail as its glint crosses — a glanced blow can put her within the water's reach."
                : "The bridge won't hold her. Something of her father's could mend it — he kept his tools in a little cabinet, under the house.";

        switch (file)
        {
            case "room_1_puzzle.tscn":
                return Room1Puzzle.IsUnlocked
                    ? "The drawer is open. Take what the morning kept — all of it, or only what you need to go on."
                    : "Open the five-dial padlock. The kitchen remembers the combination.";
            case "room_2_puzzle.tscn":
                if (IsShardRecovered(1) && HasFlag("difference_room2_solved") && !HasFlag("item_hall_key_door"))
                    return "The drawer is just a drawer now. What the evening gave up is waiting beside the globe in the sitting room.";
                if (IsShardRecovered(1) && !HasFlag("difference_room2_solved"))
                    return "The photograph is Mara's. But its backing still nags at her — something small is taped behind what the house edited.";
                if (Room2Puzzle.IsUnlocked && !IsShardRecovered(1))
                    return "A face-down photograph waits in the drawer.";
                return "Spell the four-letter word. The sitting room has been saying it all along.";
            case "difference_investigation.tscn":
                return "The room from the photograph is coming apart. Point out everything that's wrong — the photograph in her bag (B) remembers it right — before the memory breaks.";
            case "jar_closeup.tscn":
                return HasFlag("item_hairpin")
                    ? "Tap as the working line sweeps across the red give in the seal. Seven true taps, faster each time — one miss and the seal starts over."
                    : "The last seal will not give to fingernails. It wants something thin.";
            case "breaker_box.tscn":
                return "Set the breakers like Father's picture. Then the big handle.";
            case "room_4.tscn":
                if (HasFlag("room4_door_opened"))
                    return "All three hallway keys have turned. The door beyond the sentence stands open.";
                if (IsShardRecovered(3) || HasFlag("room4_sentence_complete"))
                {
                    int inserted = (HasFlag("room4_key_bridge_inserted") ? 1 : 0) +
                        (HasFlag("room4_key_door_inserted") ? 1 : 0) +
                        (HasFlag("room4_key_circle_inserted") ? 1 : 0);
                    return $"The sentence is whole. The hallway door still wants its three physical keys ({inserted}/3 in).";
                }
                RoomExamineTracker.GetRoomProgress("Room4", out int r4Done, out int r4Total, out var r4Left);
                if (r4Total > 0 && r4Left.Count > 0)
                    return $"Face what this hallway keeps ({r4Done}/{r4Total}). The mirror keeps more than a reflection.";
                return "Cross the hallway. The photographs are trying to say something in order.";
            case "flashback_landing.tscn":
                return "A memory inside the memory — the landing, that evening.";
            case "apartment_now.tscn":
                RoomExamineTracker.GetRoomProgress("ApartmentNow", out int apDone, out int apTotal, out var apLeft);
                return apLeft.Count > 0
                    ? $"The life that continued, unopened ({apDone}/{apTotal})."
                    : "Nothing here is a puzzle. The door remembers the way back.";
            case "basement.tscn":
                if (HasFlag("blackout_active") && !HasFlag("backup_power_on"))
                    return "Somewhere on these walls: the backup-power box Father always came down to. The rest can wait for light.";
                RoomExamineTracker.GetRoomProgress("Basement", out int baDone, out int baTotal, out var baLeft);
                if (baLeft.Count > 0)
                {
                    string left = string.Join(", ", baLeft.ConvertAll(PrettyObjectName));
                    return $"Bring the light to what waits below: {left}. ({baDone}/{baTotal})";
                }
                if (!HasFlag("basement_trunk_open"))
                    return "One locked thing below still keeps its own counsel.";
                if (!HasFlag("basement_negatives_taken"))
                    return "The trunk below stands open. See what was never printed.";
                if (!HasFlag("item_hammer_nails"))
                    return HasFlag("item_iron_cellar_key")
                        ? "The iron key has a lock below the house — and something in that cabinet can mend what's broken on the road."
                        : "A little cabinet below wants an iron key from somewhere in the garden.";
                return "Nothing below the house is still hiding. Climb back when you are ready.";
            case "lockpick_puzzle.tscn":
                return "The pick follows the mouse around the lock. Hold A to turn; how far it turns before it shakes says how close you are. Set each layer home.";
            case "vent_crawl.tscn":
                return "Hold W to crawl toward the light. The flashlight follows the mouse; release W to stop.";
            case "bedroom_prologue.tscn":
                if (HasFlag("prologue_from_bedroom") && !HasFlag("item_hairpin"))
                    return "Back where she woke. The nightstand's little drawer is still keeping something thin.";
                RoomExamineTracker.GetRoomProgress("Bedroom", out int bDone, out int bTotal, out var bLeft);
                return bLeft.Count > 0
                    ? $"Just woke up somewhere impossible — look around ({bDone}/{bTotal})."
                    : "There's a door. Beyond it, whatever this is.";
            case "porch_447.tscn":
                if (HasFlag("porch_passed"))
                    return "Crossed, and standing. The front door remembers her knock now.";
                RoomExamineTracker.GetRoomProgress("Porch447", out int pDone, out int pTotal, out var pLeft);
                return pLeft.Count > 0
                    ? $"The porch, at 4:47 ({pDone}/{pTotal})."
                    : "The front door. Announce yourself the way family did — and keep your composure while the house tries to take it. The last room is through it.";
            case "journey_to_room_2.tscn":
                return "Straight on to the next door. Open it when you're ready.";
            case "journey_to_room_3.tscn":
                return "Left at the bent runner, to the next door. Open it when you're ready.";
            case "journey_to_room_4.tscn":
                return "Right at the photographs, to the next door. Open it when you're ready.";
            case "journey_to_porch.tscn":
                return HasFlag("bridge_fixed")
                    ? "Walk the empty road to the lit porch. The bridge holds now."
                    : "Walk the empty road toward the lit porch — and whatever the memory has left broken across it.";
            case "Room_4_puzzle.tscn":
                if (HasFlag("room4_door_opened"))
                    return "The way through stands open. Click the door.";
                int keysIn = (HasFlag("room4_key_bridge_inserted") ? 1 : 0) +
                    (HasFlag("room4_key_door_inserted") ? 1 : 0) +
                    (HasFlag("room4_key_circle_inserted") ? 1 : 0);
                int keysHeld = (HasFlag("item_hall_key_bridge") ? 1 : 0) +
                    (HasFlag("item_hall_key_door") ? 1 : 0) +
                    (HasFlag("item_hall_key_circle") ? 1 : 0);
                if (keysIn >= 3 && !(HasFlag("room4_sentence_complete") || IsShardRecovered(3)))
                    return "Three keys are in. The hallway sentence still has to be made whole.";
                return keysHeld >= 3
                    ? $"Drag each of Mara's keys from the tray into its matching keyhole ({keysIn}/3 in)."
                    : $"Three physical keys open this door. Mara carries {keysHeld}/3; the first three fragment rooms keep them.";
            case "room_7.tscn":
                if (!string.IsNullOrEmpty(CompletedEnding))
                    return "This memory has been decided.";
                if (CountRecovered() < MaxShards)
                    return $"The last room is waiting for every fragment ({CountRecovered()}/{MaxShards}). Step back and keep remembering.";
                if (CountPlaced() < MaxShards)
                    return $"Return the fragments to the six hollow frames ({CountPlaced()}/{MaxShards}).";
                return FragmentsInOrder()
                    ? "One morning, in order."
                    : "All six are here. The house is being kind about the order.";
        }

        if (room != null && RoomExamineTracker.RoomHasRequirements(room))
        {
            RoomExamineTracker.GetRoomProgress(room, out int done, out int total, out var remaining);
            if (remaining.Count > 0)
            {
                string lead = room switch
                {
                    "Room1" => "Examine everything in the kitchen",
                    "Room2" => "Examine everything in the sitting room",
                    "Room3" => "Examine everything kept in this room",
                    "Room5" => "First look normally — examine the room that will not stay one room",
                    "Room6" => "First face each stop normally — examine the last afternoon",
                    _ => "Examine everything here",
                };
                return $"{lead} ({done}/{total})";
            }

            return room switch
            {
                "Room1" when !ShardCollected(0) => "Everything faced — the drawer will open now. Click it.",
                "Room2" when !ShardCollected(1) => "Everything faced — the table drawer will open now. Click the table.",
                "Room3" => "Everything faced — open the jars, then take the hallway.",
                "Room1" => "Fragment recovered. Onward: take the arrow to the sitting room.",
                "Room2" => "Fragment recovered. Onward: through the table drawer.",
                "Room5" when !IsShardRecovered(4) && !HasFlag("room5_anchored_lamp") => "Everything faced — and still the room will not stay one room.",
                "Room5" when !IsShardRecovered(4) => "The room is holding still, in the present tense.",
                "Room5" when HasFlag("blackout_active") => "Fragment recovered — and the house has gone dark around it.",
                "Room5" => "Fragment recovered. The wardrobe has remembered the garden.",
                "Room6" when !IsShardRecovered(5) => "Everything faced — four separate truths, and one afternoon.",
                "Room6" => "Fragment recovered. The road beyond the broken gate.",
                _ => string.Empty,
            };
        }

        return string.Empty;
    }

    /// <summary>
    /// Next progressive hint for the current room:
    /// 1 = flavour nudge, 2 = names the object, 3 = also pulse it (caller does the pulse
    /// when the returned object name is non-null).
    /// </summary>
    public (string text, string objectToPulse) RequestHint()
    {
        string room = CurrentRoomName();
        string file = FileNameOf(SceneManager.CurrentScenePath ?? string.Empty);

        // The blackout has one answer in every room: her father's order.
        if (HasFlag("blackout_active") && !HasFlag("backup_power_on") &&
            file is not ("lockpick_puzzle.tscn" or "vent_crawl.tscn" or "breaker_box.tscn"))
        {
            if (!HasFlag("item_flashlight"))
                return ("He never hurried. Kitchen cabinet, top shelf, one hand, in the dark. The cabinet Mara is sure was never in that kitchen — Step Back (Q) through the dark rooms until the kitchen, then try the drawer counter anyway. Memory is keeping it where it always was.", null);
            if (!HasFlag("blackout_vent_noticed"))
                return ("Back to the room that will not stay one room (Step Forward). Feel for cold air where the wall meets the floor. He went down to the basement box — and something in that room breathes basement air.", null);
            if (!HasFlag("room5_vent_open"))
                return ("The low vent, under the writing desk. His latch, his tools: the pick follows the mouse; hold A to turn.", "LowVent");
            return ("Below the house, on the wall above the preserves: a grey box with his writing inside the door. Set the breakers like his picture, then pull the big handle.", file == "basement.tscn" ? "BreakerBox" : null);
        }

        if (file == "jar_closeup.tscn")
        {
            return NextHintLevel("JarCloseup") switch
            {
                1 => ("Fingernails only worry a seal like this — and glass keeps the score. It wants something thin worried under the rubber.", null),
                2 => ("Her mother opened these with a hairpin, worked patiently round the rim. One of those hairpins has been in Mara's old nightstand drawer since before she woke up here — the very first room. Step Back (Q) as far as the bedroom and open the little drawer.", null),
                _ => ("With the hairpin in Mara's bag (B): a line works its way around the seal, faster with every true tap, turning back on itself each time. Click the moment it crosses the red stretch where the rubber gives. Seven true taps sigh the lid open; one miss starts the seal again. Under the name tape at the bottom sleeps a key with no beginning and no end.", null),
            };
        }
        if (file == "breaker_box.tscn")
            return ("His picture is the whole instruction: copy the taped diagram's up-and-down pattern onto the six breakers exactly, then pull the big handle on the right. A wrong set only trips them all off again — look, copy, pull.", null);
        if (file == "kitchen_cabinet.tscn")
            return ("Top shelf, too high for two girls who were not supposed to reach it. She is not too small any more. Click what her hand finds there.", null);

        if (file == "room_1_puzzle.tscn")
        {
            if (Room1Puzzle.IsUnlocked)
                return ("The drawer kept more than the brass key. A smaller copper key lies beside it in the open drawer, green at the edges. Take it or leave it; the house will remember which.", null);
            return NextHintLevel("Room1") switch
            {
                1 => ("Numbers stay. A clock, a calendar, a drawing, a cup — the morning kept count. Your journal (J) keeps what you found.", null),
                2 => (MorningClueStatus(), null),
                _ => ("Read the morning the way it happened: first the stopped clock's hour, then the circled day (it takes two dials), then the number in her drawing, last the ink under her cup. Every one of them is still in the kitchen where she left it — Step Back (Q) and read them off the things themselves.", null),
            };
        }
        if (file == "room_2_puzzle.tscn")
        {
            if (IsShardRecovered(1) && !HasFlag("difference_room2_solved"))
                return ("The drawer is empty — the photograph is Mara's now, and the way forward is with it, not back here. Step Back (Q) into the sitting room and look at the photograph from her bag (B). On its backing, in her father's pencil: FOR WHEN YOU REMEMBER IT RIGHT. He taped a key behind the sitting room in that picture, and the house has been editing the room it shows. Remember it honestly — find what changed before the room comes apart.", null);
            if (HasFlag("difference_room2_solved") && !IsShardRecovered(1))
                return ("The room has been remembered honestly. The photograph itself is the fragment — click it; the little door key it was hiding waits beside the globe in the sitting room, to be picked up by hand.", null);
            return NextHintLevel("Room2") switch
            {
                1 => ("What did she call this place, before it was only a house?", null),
                2 => ("Read how her parents' letter ends — the place it begs her to come back to. It's noted in your journal (J, Notes & Clues).", null),
                _ => ("Four letters. Not the building's cold name — the warm word, the one this whole memory is trying to be. The letter in your journal (J) is your witness.", null),
            };
        }
        if (file == "difference_investigation.tscn")
        {
            return NextHintLevel("DifferenceRoom2") switch
            {
                1 => ("Hold the whole room, not its prettiness: mantel, the two chairs, and the little table between them.", null),
                2 => ("Ask what an ordinary evening would move while somebody waited: time, a reader's place, a letter, a drink.", null),
                _ => ("Four things changed: the mantel clock has gone later; the red book changed chairs; the letter left the table; and the cup that stood upright has gone over. Click each changed place.", null),
            };
        }
        if (file == "room_4.tscn")
        {
            return NextHintLevel("Room4") switch
            {
                1 => ("The photographs are not separate captions. Together they are one sentence someone once said to Mara.", null),
                2 => ("Press E for Echo, then move across the photographs from the first frame toward the last. Each correct word climbs one note.", null),
                _ => ("It is something a mother said, years later, about the daughter who is gone — a comfort meant for the one still here. Begin at the leftmost frame with Echo armed, and trust the notes: the true next word always sings higher.", null),
            };
        }
        if (file == "Room_4_puzzle.tscn")
        {
            if (HasFlag("room4_door_opened"))
                return ("All three locks have already turned. Click any part of the close-up door to go through.", null);
            bool holeL = HasFlag("room4_hole_left_bridge") || HasFlag("room4_hole_left_door") || HasFlag("room4_hole_left_circle");
            bool holeC = HasFlag("room4_hole_center_bridge") || HasFlag("room4_hole_center_door") || HasFlag("room4_hole_center_circle");
            bool holeR = HasFlag("room4_hole_right_bridge") || HasFlag("room4_hole_right_door") || HasFlag("room4_hole_right_circle");
            bool placedRight = HasFlag("room4_hole_left_bridge") && HasFlag("room4_hole_center_door") && HasFlag("room4_hole_right_circle");
            if (holeL && holeC && holeR && !placedRight)
                return ("They are all in, and the door has not answered — somebody is in the wrong bed. Click a placed key to lift it back out, then look at the brass pictures again: each one names the key beneath it.", null);
            string firstOpenKeyhole = !holeL ? "LeftKeyhole"
                : !holeC ? "CenterKeyhole"
                : !holeR ? "RightKeyhole"
                : null;
            int r4HintLevel = NextHintLevel("Room4Puzzle");
            return r4HintLevel switch
            {
                1 => ("The brass pictures are labels now, not a knock to copy. Each one names the keyhole directly beneath it. Any hole will take any key by hand — the door itself is the judge, and it only judges when all three are in.", null),
                2 => ("The keys travelled with the first three fragments: copper from the kitchen drawer, the little door from behind the sitting-room photograph, and the circle from the last preserve jar. Mara's Bag (B) shows which she carries. Click a placed key to lift it back out.", null),
                _ => ("Drag each key in by hand: copper bridge key → left keyhole; little door key → centre; circle key → right. A true key turns its full, easy turn; a borrowed bed only turns partway. And the door will still refuse her until the hallway sentence is whole.", firstOpenKeyhole),
            };
        }
        if (file == "room_5.tscn" && RoomExamineTracker.HasRoomBeenFullyExamined("Room5"))
        {
            if (IsShardRecovered(4))
                return ("The yellow raincoat should not fit an adult wardrobe — which is why the wardrobe now opens on the garden. Click it.", null);
            return NextHintLevel("Room5") switch
            {
                1 => ("One object in this room has the same purpose in childhood and adulthood: it was left on for whoever came home late.", null),
                2 => ("Anchor (A) must be chosen before the click. Choose the thing you examined that was warm in both years — the room will stop sliding around it.", null),
                _ => ("What stays lit for latecomers, by a bed, in a child's room and a woman's? Anchor that. Afterwards ordinary hands will do: the writing desk keeps a drawer.", null),
            };
        }
        if (file == "room_6.tscn" && RoomExamineTracker.HasRoomBeenFullyExamined("Room6"))
        {
            if (IsShardRecovered(5))
                return ("Do not go back to the swing. Follow the empty road beyond the gate; it is the only way left to walk.", null);
            return NextHintLevel("Room6") switch
            {
                1 => ("Trace does not ask where Ida is in the garden. It asks what happened, one cause at a time.", null),
                2 => ("Ask of each stop: was she waiting there, passing the minutes, leaving, or already gone? The stories they told you (J) hold the order.", null),
                _ => ("Begin where waiting begins — where she stood with something gathered. End where no one is left to arrive. Let the rising notes confirm each step.", null),
            };
        }
        if (file == "room_7.tscn")
        {
            if (CountRecovered() < MaxShards)
                return ($"Only {CountRecovered()} of six fragments have been recovered. Step Back (Q); the missing rooms still hold the rest.", null);
            return NextHintLevel("Room7") switch
            {
                1 => ("A frame is not filled merely by having any memory in it. The order matters — and a placed fragment can be taken back.", null),
                2 => ("Choose a carried fragment at the bottom, then click its hollow frame. Click a filled frame to reclaim it. Nothing here is labelled; the house answers only when all six stand together.", null),
                _ => ("Put them in the order Mara lived them: a morning in the kitchen; a quiet room; a childhood kept and labelled; a sentence in a hall; Octobers at a writing desk; one last afternoon. Your journal (J) keeps each fragment's words.", null),
            };
        }

        if (file.StartsWith("journey_"))
        {
            string journeyHint = file switch
            {
                "journey_to_room_2.tscn" => "Let Mara walk straight to the door, settle herself, then click the door itself to open it.",
                "journey_to_room_3.tscn" => "Let Mara turn left where the runner bends and settle herself, then click the door itself to open it.",
                "journey_to_room_4.tscn" => "Let Mara turn right at the photographs and settle herself, then click the door itself to open it.",
                "journey_to_porch.tscn" => "Let Mara walk until the porch light is close and settle herself, then click the light to arrive.",
                _ => "Let Mara reach the door, then click the door itself to open it.",
            };
            return (journeyHint, null);
        }

        if (file == "flashback_landing.tscn")
            return ("Nothing here can be changed — only finally seen. The photograph, the light, the runner. The end of the hall leads back when you are ready.", null);

        if (file == "apartment_now.tscn")
            return ("This is not a puzzle room. It is the life that continued while she was in the house. The mail, the phone, the chair. Her own flashlight here is dead — the living one is her father's, in a kitchen on the other side of memory. The door leads back.", null);

        if (file == "lockpick_puzzle.tscn")
        {
            return NextHintLevel("Lockpick") switch
            {
                1 => ("The pick is locked to the mouse: move the mouse around the top of the lock to seat it, then hold A (or D) to turn the cylinder. No clicking, no dragging. His picks do not break — not in her memory of them — so take your time.", null),
                2 => ("Read the turn, layer by layer. If holding A barely moves the cylinder before the pick starts shaking, you are far away — reseat the pick somewhere new. If it turns most of the way first, you are close: nudge the mouse a hair and try again.", null),
                _ => ("When the pick trembles, let go of A at once — forcing only complains, it never helps. Let the layer spring back, move the pick the width of a breath toward wherever it turned farthest, and turn again. A layer turned all the way home clicks set and stays set. Deeper layers sit narrower; the fine tension wrench from Room 5's desk reads them about a degree sooner.", null),
            };
        }

        if (file == "vent_crawl.tscn")
            return ("Hold W to crawl; Mara stops the moment you release it. The flashlight follows the mouse without any click. At the far end, click the lit grille when it fills the dark.", null);

        if (file == "basement.tscn")
        {
            RoomExamineTracker.GetRoomProgress("Basement", out _, out _, out var baRemaining);
            int baLevel = NextHintLevel("Basement");
            if (baRemaining.Count > 0)
                return (baLevel <= 1
                    ? "Sweep slowly. The beam is your eyes down here."
                    : $"The light has not rested on {string.Join(", ", baRemaining.ConvertAll(PrettyObjectName))} yet.", baLevel >= 3 ? baRemaining[0] : null);
            if (!HasFlag("basement_trunk_open"))
                return ("A trunk under the house keeps its own lock. The picks remember how locks feel inside.", baLevel >= 3 ? "LockedTrunk" : null);
            if (!HasFlag("basement_negatives_taken"))
                return ("The trunk is open now. Beneath cedar and blankets is one flat thing their mother never had printed.", baLevel >= 3 ? "LockedTrunk" : null);
            if (!HasFlag("item_hammer_nails"))
                return (HasFlag("item_iron_cellar_key")
                    ? "Try the iron key on the little cabinet in the wall. What a father files away below stairs is what a broken bridge will want."
                    : "The little cabinet wants a key. In the garden, their father trusted a loose stone by the gate more than a hook.", baLevel >= 3 ? "ToolCabinet" : null);
            return ("Nothing below is still hiding. The vent overhead is the way back up.", null);
        }

        if (file == "bridge_repair.tscn")
        {
            if (!HasFlag("item_hammer_nails"))
                return ("Bare hands won't mend that crossing. Her father kept his tools where the house keeps everything it needs to stay alive: below it. Garden wall by the gate for the iron key — a loose stone he trusted more than a hook — then the vent, then the little cabinet's lock.", null);
            return NextHintLevel("Bridge") switch
            {
                1 => ("Watch one nail's glint travel before you ever raise the hammer. It crosses the same bright point every swing.", null),
                2 => ("Strike only as the glint crosses the gold ring — two true strikes sink a nail. A glanced blow shifts the plank, and the bridge puts her within the water's reach; enough falls will break the memory entirely.", null),
                _ => ("Nails are laid in a steady rhythm, plank by plank, and the glint quickens as she crosses. Patience is the tool her father actually left her: wait for the gold, strike, breathe. Nailed planks stay nailed, whatever the water says.", null),
            };
        }

        if (file == "porch_447.tscn")
        {
            if (RoomExamineTracker.HasRoomBeenFullyExamined("Porch447") && !HasFlag("porch_passed"))
            {
                return NextHintLevel("Porch447") switch
                {
                    1 => ("The door will not be rushed or verb-ed open. Homes were announced: knock the way family knocked, be seen at the lit window, then hold the handle steady. Her own head will try to interrupt all three — let the interruptions pass through, not over her.", null),
                    2 => ("The knock their mother listened for: two, a breath, three — each knock close on the last, the breath honestly a breath. At the window, click only while the light is steady. At the handle, keep her hand inside the ring until it fills, however the porch sways.", null),
                    _ => ("Knock: click the door — knock knock (each within a breath of the last), wait one full second, knock knock knock. Window: click it in the steady light, three times; the flicker is the house blinking, not an invitation. Handle: press and HOLD inside the ring for a slow count of four while it sways; slipping off only costs the count, not the door. Migraines steal a second; let them.", null),
                };
            }
            return ("She stopped here for twelve years without ever stopping. The light, the steps, the path. The front door is the way on.", null);
        }

        if (room == null || !RoomExamineTracker.RoomHasRequirements(room))
            return ("Keep looking. The house notices being noticed.", null);

        RoomExamineTracker.GetRoomProgress(room, out _, out _, out var remaining);
        if (remaining.Count == 0)
            return ("This room has said what it can. Its lock is ready.", null);

        string next = remaining[0];
        string pretty = PrettyObjectName(next);
        int level = NextHintLevel(room);

        return level switch
        {
            1 => ("Something here hasn't been faced yet. The room is patient; be thorough.", null),
            2 => ($"Look again at {pretty}. It remembers being looked at.", null),
            _ => ($"There — {pretty}. Watch for the ring.", next),
        };
    }

    /// <summary>Called once when a room's examine set is completed.</summary>
    public void MarkRoomCompleted(string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName) || !_roomsCompleted.Add(roomName))
            return;

        if (CollapseActive && roomName == _collapseRoom)
            EndCollapse(rescued: true);
        AudioManager.Play(Sfx.RoomComplete, -4f);
        if (RefillClarity(1))
            Toast("The room settles. Clarity +1.");
        else
            Toast("The room settles.");

        ObjectiveChanged?.Invoke();
        SaveGame($"room completed: {roomName}");
    }

    public bool IsRoomCompleted(string roomName) => roomName != null && _roomsCompleted.Contains(roomName);

    // --------------------------------------------------------------- corruption

    public static CorruptionTier TierFor(int corruption)
    {
        CorruptionTier tier = CorruptionTier.Calm;
        for (int i = 0; i < TierThresholds.Length; i++)
        {
            if (corruption >= TierThresholds[i])
                tier = (CorruptionTier)i;
        }
        return tier;
    }

    public static string TierDisplayName(CorruptionTier tier) => TierNames[(int)tier];

    public CorruptionTier CurrentTier => TierFor(Globals.Instance?.CORRUPTION_COUNT ?? 0);

    /// <summary>0 at Calm start → 1 deep in Shattered; drives overlay/audio.</summary>
    public float Corruption01
    {
        get
        {
            int c = Globals.Instance?.CORRUPTION_COUNT ?? 0;
            return Mathf.Clamp(c / (float)(TierThresholds[^1] + 6), 0f, 1f);
        }
    }

    /// <summary>Add fractional click corruption, applying whole points as
    /// soon as they accumulate. This is what lets a repeated empty click
    /// cost 2.3 memory units without changing the integer MEMORY meter.</summary>
    public void AddClickCorruption(float amount)
    {
        if (amount <= 0f || _shattered)
            return;

        _clickCorruptionDebt = Mathf.Clamp(_clickCorruptionDebt + amount, 0f, 24f);
        int whole = Mathf.FloorToInt(_clickCorruptionDebt + 0.0001f);
        if (whole <= 0)
            return;

        _clickCorruptionDebt -= whole;
        AddCorruption(whole);
    }

    /// <summary>An already-faced examinable, deliberately faced again.</summary>
    public void RegisterRepeatExaminePenalty() => AddClickCorruption(RepeatExaminePenalty);

    /// <summary>Register a left click that reached the room but claimed no
    /// interactable. The first loose click costs 2.0× the old repeat-look
    /// unit; another click in nearly the same place, soon after, costs
    /// 2.3×. Journeys, menus, cutscenes and sealed rooms are excluded by
    /// the caller/scene gates here. Returns the multiplier actually used,
    /// or 0 when the click was not punishable.</summary>
    public float RegisterNonInteractableClick(Vector2 position)
    {
        if (Globals.Instance == null || _shattered || !string.IsNullOrWhiteSpace(CompletedEnding))
            return 0f;

        string scenePath = SceneManager.CurrentScenePath ?? string.Empty;
        string trackerRoom = SceneTrackerRoomName(scenePath);
        if (string.IsNullOrEmpty(trackerRoom) || IsRoomSealed(trackerRoom))
            return 0f;
        // Clicks during the first-person waking/blinking beat are not
        // exploration clicks yet.
        if (trackerRoom == "Bedroom" && !HasFlag("prologue_awake"))
            return 0f;

        ulong now = Time.GetTicksMsec();
        bool repeated = scenePath == _lastLooseClickScene
            && now - _lastLooseClickMsec <= 1400
            && position.DistanceSquaredTo(_lastLooseClickPosition) <= 110f * 110f;
        float penalty = repeated
            ? RepeatedNonInteractableClickPenalty
            : NonInteractableClickPenalty;

        _lastLooseClickScene = scenePath;
        _lastLooseClickPosition = position;
        _lastLooseClickMsec = now;
        AddClickCorruption(penalty);
        return penalty;
    }

    public void AddCorruption(int amount)
    {
        if (Globals.Instance == null || amount == 0 || _shattered)
            return;

        var before = CurrentTier;
        Globals.Instance.CORRUPTION_COUNT = Mathf.Max(0, Globals.Instance.CORRUPTION_COUNT + amount);
        CorruptionPeak = Mathf.Max(CorruptionPeak, Globals.Instance.CORRUPTION_COUNT);
        var after = CurrentTier;

        AudioManager.Instance?.SetCorruption01(Corruption01);
        CorruptionChanged?.Invoke(Globals.Instance.CORRUPTION_COUNT, after);

        if (after > before)
        {
            AudioManager.Play(Sfx.CorruptionGlitch, -8f);
            Toast(after switch
            {
                CorruptionTier.Unease => "Something in the memory shifts, almost too small to name.",
                CorruptionTier.Fractured => "The memory is fraying. Clarity can steady it.",
                CorruptionTier.Shattered => "The house can barely remember itself.",
                _ => string.Empty,
            });
        }

        if (amount > 0 && !_shattered && Globals.Instance.CORRUPTION_COUNT >= ShatterThreshold)
        {
            _shattered = true;
            EndCollapse(rescued: false, silent: true);
            MemoryShattered?.Invoke();
        }
    }

    /// <summary>Empty MEMORY in one step and shatter it. This is
    /// deliberately separate from ordinary corruption: a caller
    /// that offers Mara a way back must let her choose it before
    /// invoking the drain.</summary>
    public void DrainMemoryToShatter()
    {
        if (Globals.Instance == null || _shattered)
            return;

        int need = ShatterThreshold - Globals.Instance.CORRUPTION_COUNT;
        if (need > 0)
        {
            AddCorruption(need);
            return;
        }

        _shattered = true;
        EndCollapse(rescued: false, silent: true);
        MemoryShattered?.Invoke();
    }

    /// <summary>After the bad ending: pull the memory back from the brink —
    /// still Fractured (the house doesn't forget being dropped), a little
    /// Clarity to stand on, saved. Also un-latches the shatter.</summary>
    public void RecoverFromShatter()
    {
        EndCollapse(rescued: false, silent: true);
        if (Globals.Instance == null)
            return;

        Globals.Instance.CORRUPTION_COUNT = 13;
        _shattered = false;
        // The gathering takes the small breakages with it: a memory
        // put back together puts her father's roll back together
        // too, all seven picks lying whole in their pockets.
        RestoreAllPicks("gathered after the shatter");
        AudioManager.Instance?.SetCorruption01(Corruption01);
        CorruptionChanged?.Invoke(Globals.Instance.CORRUPTION_COUNT, CurrentTier);
        if (ClarityCount < 2)
        {
            Globals.Instance.CLARITY_TOGGLE_COUNT = 2;
            ClarityChanged?.Invoke(ClarityCount);
        }
        SaveGame("shatter recovery");
    }

    // ------------------------------------------------------------------ clarity

    public int ClarityCount => Globals.Instance?.CLARITY_TOGGLE_COUNT ?? 0;

    public void RegisterClarityUsed()
    {
        ClarifiesUsed++;
        ClarityChanged?.Invoke(ClarityCount);
        // Clarity doubles as stabilisation: seeing truly, once, quiets the noise.
        if ((Globals.Instance?.CORRUPTION_COUNT ?? 0) > 0)
            AddCorruption(-2);
        ObjectiveChanged?.Invoke();

        // Spending the very last charge in an unfinished room starts the
        // 15-second collapse: find what remains, open the lock, in time.
        MaybeBeginCollapse();
    }

    /// <returns>True if a charge was actually restored.</returns>
    public bool RefillClarity(int amount)
    {
        if (Globals.Instance == null)
            return false;

        int before = Globals.Instance.CLARITY_TOGGLE_COUNT;
        Globals.Instance.CLARITY_TOGGLE_COUNT = Mathf.Min(Globals.MaxClarity, before + amount);
        ClarityChanged?.Invoke(Globals.Instance.CLARITY_TOGGLE_COUNT);
        if (Globals.Instance.CLARITY_TOGGLE_COUNT > before && CollapseActive)
            EndCollapse(rescued: true);
        return Globals.Instance.CLARITY_TOGGLE_COUNT > before;
    }

    // ------------------------------------------------------------ lockpicks
    // Her father's roll holds seven picks, and in this house his
    // steel remembers it can break. Broken picks are run-wide: they
    // only come back whole when time itself is rewound by Clarity,
    // or when a shattered memory is gathered and takes the roll
    // with it. This is what saving Clarity is *for*.

    public const int LockpickTotal = 7;

    public int BrokenPickCount
    {
        get
        {
            int count = 0;
            for (int i = 1; i <= LockpickTotal; i++)
            {
                if (HasFlag($"lockpick_broken_{i}"))
                    count++;
            }
            return count;
        }
    }

    public int PicksLeft => LockpickTotal - BrokenPickCount;

    public void RegisterPickBroken()
    {
        int next = BrokenPickCount + 1;
        if (next <= LockpickTotal)
            SetFlag($"lockpick_broken_{next}", save: false);
    }

    public void RestoreAllPicks(string reason)
    {
        for (int i = 1; i <= LockpickTotal; i++)
            ClearFlag($"lockpick_broken_{i}", save: false);
        Logger.Info($"[GameState] Lockpicks restored: {reason}.");
    }

    /// <summary>Spend every remaining Clarity charge on turning the
    /// memory back. Deliberately silent to the collapse machinery:
    /// this spend is what stands between Mara and the fall, not
    /// what starts it. Returns how many charges were spent.</summary>
    public int SpendAllClarityForRewind()
    {
        if (Globals.Instance == null)
            return 0;
        int spent = Globals.Instance.CLARITY_TOGGLE_COUNT;
        if (spent <= 0)
            return 0;
        Globals.Instance.CLARITY_TOGGLE_COUNT = 0;
        ClarifiesUsed += spent;
        ClarityChanged?.Invoke(0);
        ObjectiveChanged?.Invoke();
        return spent;
    }

    /// <summary>Set while a Clarity rewind has deliberately un-thrown
    /// the breaker. The old-save backfill must not infer "power long
    /// restored" over the top of that reset; only throwing Father's
    /// breaker again clears this guard.</summary>
    public const string RewoundBackupPowerResetFlag = "lockpick_rewind_power_reset";

    /// <summary>Roll the under-house attempt itself back with the
    /// picks: the vent latch is shut, the duct has not been entered,
    /// the basement's investigated things are un-investigated, and a
    /// breaker already thrown is un-thrown (so the blackout it ended
    /// is the blackout Mara wakes into again). What is already in
    /// Mara's Bag is not revoked — the mercy rewinds the attempt and
    /// the places it touched, not the things she is carrying.</summary>
    public void ResetUnderHouseForLockpickRewind()
    {
        // Guard first: every clear below can notify listeners, and
        // no progress inference gets to undo this reset mid-flight.
        SetFlag(RewoundBackupPowerResetFlag, save: false);

        ClearFlag("room5_vent_open", save: false);
        ClearFlag("basement_entered", save: false);
        ClearFlag("basement_seen", save: false);
        ClearFlag("story_lesson_basement", save: false);
        _roomsCompleted.Remove("Basement");
        _hintLevelByRoom.Remove("Basement");
        RoomExamineTracker.ResetProgress("Basement");

        ClearFlag("backup_power_on", save: false);
        ClearFlag("room5_power_restored_seen", save: false);
        if (HasFlag("blackout_began"))
            SetFlag("blackout_active", save: false);

        ObjectiveChanged?.Invoke();
        Logger.Info("[GameState] Under-house reset for the lockpick Clarity rewind: duct, basement investigates and backup power rewound.");
    }

    // ------------------------------------------------------------- memory verbs

    public static string VerbDisplayName(MemoryVerb verb) => verb switch
    {
        MemoryVerb.Echo => "Echo",
        MemoryVerb.Anchor => "Anchor",
        MemoryVerb.Trace => "Trace",
        MemoryVerb.Release => "Release",
        _ => "Remember",
    };

    public bool HasVerb(MemoryVerb verb) => verb != MemoryVerb.None && _unlockedVerbs.Contains(verb);

    private static string ClueIdForVerb(MemoryVerb verb) => verb switch
    {
        MemoryVerb.Echo => "way_echo",
        MemoryVerb.Anchor => "way_anchor",
        MemoryVerb.Trace => "way_trace",
        MemoryVerb.Release => "way_release",
        _ => null,
    };

    /// <summary>Keep the story's own lesson for a verb in Notes &amp; Clues.
    /// The room still teaches it in dialogue; this is Mara writing it down so
    /// the player can return to it with J instead of relying on a toast.</summary>
    private void RememberVerbLesson(MemoryVerb verb, bool announce)
    {
        string clueId = ClueIdForVerb(verb);
        if (string.IsNullOrEmpty(clueId) || _unlockedClues.Contains(clueId))
            return;

        if (announce)
        {
            UnlockClue(clueId);
        }
        else
        {
            _unlockedClues.Add(clueId);
            CluesChanged?.Invoke();
        }
    }

    private void BackfillVerbLessons()
    {
        foreach (var verb in new[] { MemoryVerb.Echo, MemoryVerb.Anchor, MemoryVerb.Trace, MemoryVerb.Release })
        {
            if (HasVerb(verb))
                RememberVerbLesson(verb, announce: false);
        }
    }

    /// <summary>Unlock a way of remembering. Silent unlocks are used by save
    /// backfills; the surrounding scene-change/load save persists them.</summary>
    public bool UnlockVerb(MemoryVerb verb, bool announce = true)
    {
        if (verb == MemoryVerb.None || !_unlockedVerbs.Add(verb))
            return false;

        VerbsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
        RememberVerbLesson(verb, announce);
        if (announce)
        {
            AudioManager.Play(Sfx.ClarifyReveal, -5f, verb == MemoryVerb.Release ? 1.15f : 1f);
            Toast(verb switch
            {
                MemoryVerb.Echo => "Echo wakes in Mara — listen for order. Her note is in the Journal (J).",
                MemoryVerb.Anchor => "Anchor wakes in Mara — one true thing can hold still. Her note is in the Journal (J).",
                MemoryVerb.Trace => "Trace wakes in Mara — moments can be joined by cause. Her note is in the Journal (J).",
                MemoryVerb.Release => "Release wakes in Mara — a whole memory can be answered. Her note is in the Journal (J).",
                _ => $"{VerbDisplayName(verb)} learned.",
            });
            SaveGame($"verb unlocked: {verb}");
        }
        return true;
    }

    private void UnlockVerbForShard(int shardIndex, bool announce)
    {
        MemoryVerb verb = shardIndex switch
        {
            2 => MemoryVerb.Echo,
            3 => MemoryVerb.Anchor,
            4 => MemoryVerb.Trace,
            5 => MemoryVerb.Release,
            _ => MemoryVerb.None,
        };
        if (verb != MemoryVerb.None)
            UnlockVerb(verb, announce);
    }

    public void SetActiveVerb(MemoryVerb verb)
    {
        if (verb != MemoryVerb.None && !HasVerb(verb))
            verb = MemoryVerb.None;
        if (ActiveVerb == verb)
            return;
        ActiveVerb = verb;
        VerbsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
    }

    public void RegisterVerbUsed(MemoryVerb verb)
    {
        if (verb == MemoryVerb.None)
            return;
        VerbUses++;
        VerbsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
    }

    // --------------------------------------------------------------- story flags

    public bool HasFlag(string flag) => !string.IsNullOrWhiteSpace(flag) && _storyFlags.Contains(flag);

    /// <summary>Persistent one-way story/puzzle flags for the final act
    /// (anchored objects, sentence completion, ending choice availability).</summary>
    public bool SetFlag(string flag, bool save = true)
    {
        if (string.IsNullOrWhiteSpace(flag) || !_storyFlags.Add(flag))
            return false;
        if (CollapseRescueFlags.Contains(flag))
            NotifyPuzzleSolved();
        StoryFlagsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
        if (save)
            SaveGame($"flag: {flag}");
        return true;
    }

    public bool ClearFlag(string flag, bool save = true)
    {
        if (string.IsNullOrWhiteSpace(flag) || !_storyFlags.Remove(flag))
            return false;
        StoryFlagsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
        if (save)
            SaveGame($"flag cleared: {flag}");
        return true;
    }

    private static int RoomOrder(string room) => room switch
    {
        "Room1" => 1,
        "Room2" => 2,
        "Room3" => 3,
        "Room4" => 4,
        "Room5" => 5,
        "Room6" => 6,
        "Room7" => 7,
        _ => 0,
    };

    /// <summary>
    /// Cross-room practical items are deliberately skippable now: a
    /// fragment is enough to leave a room by, and what Mara did not pick
    /// up stays where it was kept until she comes back for it. So this
    /// backfill no longer derives keys or tools from fragments or
    /// examines — saves from the version where items rode along with the
    /// fragments already carry their flags, and everyone else can step
    /// back and fetch what they missed (sealed rooms keep untaken things
    /// takeable). What remains is pure consistency: proofs that can only
    /// exist if the item was already in her hands.
    /// </summary>
    private void BackfillCrossRoomItemsFromProgress()
    {
        void AddItem(string flag, string clueId)
        {
            if (!HasFlag(flag))
                SetFlag(flag, save: false);
            if (clueId != null && ClueDefinitions.ContainsKey(clueId) && !_unlockedClues.Contains(clueId))
            {
                _unlockedClues.Add(clueId);
                CluesChanged?.Invoke();
            }
        }

        // Remembering the room right does NOT put the little door
        // key in Mara's bag — that inference used to live here,
        // and it meant the key simply arrived with the solved flag,
        // never seen, never touched. Solving only sets the key down
        // on the table beside the globe; her hand still has to close
        // on it. What solving proves is the clue, nothing more. The
        // key in her bag, on the other hand, could only have got
        // there through an honest memory — that direction is proof.
        if (HasFlag("difference_room2_solved") &&
            ClueDefinitions.ContainsKey("hall_key_door") && !_unlockedClues.Contains("hall_key_door"))
        {
            _unlockedClues.Add("hall_key_door");
            CluesChanged?.Invoke();
        }
        if (HasFlag("item_hall_key_door"))
            AddItem("difference_room2_solved", "difference_room");

        // The fine wrench only exists fitted to the set, and the vent
        // latch could only have been picked with it.
        if (HasFlag("item_fine_tension_wrench") || HasFlag("room5_vent_open") ||
            HasFlag("basement_trunk_open"))
            AddItem("item_lockpick_set", "father_lockpicks");

        if (HasFlag("room4_door_opened"))
        {
            AddItem("room4_key_bridge_inserted", null);
            AddItem("room4_key_door_inserted", null);
            AddItem("room4_key_circle_inserted", null);
        }

        // The blackout belongs to the moment Room 5's letters become
        // Mara's. A save already in the garden or beyond lived it before
        // it had flags: mark the power long restored so the wardrobe
        // never goes dark behind them retroactively.
        if (IsShardRecovered(5) || HasFlag("porch_passed") ||
            !string.IsNullOrEmpty(CompletedEnding))
        {
            AddItem("blackout_began", null);
            // A Clarity rewind may have deliberately un-thrown the
            // breaker after all that progress. That newer, explicit
            // fact wins over this older-save inference until Mara
            // sets and throws the box again herself.
            if (!HasFlag(RewoundBackupPowerResetFlag))
            {
                AddItem("backup_power_on", null);
                if (HasFlag("blackout_active"))
                    ClearFlag("blackout_active", save: false);
            }
            else
            {
                AddItem("blackout_active", null);
            }
        }
        // Power restored is the one fact that ends the dark, however the
        // flags arrived.
        if (HasFlag("backup_power_on") && HasFlag("blackout_active"))
            ClearFlag("blackout_active", save: false);

        // A flicker is a moment, never a saved state — if one ever
        // outlives its second, let it go. And a blackout that "began"
        // without ever arriving (Mara stepped out mid-stutter, or the
        // game closed between the two) is owed a second chance to
        // happen when Room 5 is next entered.
        if (HasFlag("blackout_flickering"))
            ClearFlag("blackout_flickering", save: false);
        if (HasFlag("blackout_began") && !HasFlag("blackout_active") &&
            !HasFlag("backup_power_on"))
            ClearFlag("blackout_began", save: false);

        // The tool cabinet used to keep a stronger battery; it keeps
        // mending tools now. A save that already emptied it is not made
        // to loot it twice.
        if (HasFlag("basement_battery_taken"))
            AddItem("item_hammer_nails", null);

        // Hallway keys used to only ever fit their own holes. Old saves
        // record insertions, not placements; under the old law every
        // recorded insertion was the right bed, so say so.
        void PlaceLegacy(string insertedFlag, string holeFlag, string otherA, string otherB)
        {
            if (HasFlag(insertedFlag) && !HasFlag(otherA) && !HasFlag(otherB))
                AddItem(holeFlag, null);
        }
        PlaceLegacy("room4_key_bridge_inserted", "room4_hole_left_bridge",
            "room4_hole_center_bridge", "room4_hole_right_bridge");
        PlaceLegacy("room4_key_door_inserted", "room4_hole_center_door",
            "room4_hole_left_door", "room4_hole_right_door");
        PlaceLegacy("room4_key_circle_inserted", "room4_hole_right_circle",
            "room4_hole_left_circle", "room4_hole_center_circle");

        // A save already standing on the porch crossed the road before
        // the memory broke its bridge. It holds for them.
        if (HasFlag("porch_passed") || !string.IsNullOrEmpty(CompletedEnding))
            AddItem("bridge_fixed", null);
    }

    /// <summary>
    /// Older saves may sit at the old "Part 1 End" with rooms completed but
    /// no late fragments/verbs (those collectors did not exist yet). Derive
    /// them from durable progress whenever a room opens or a save loads, so
    /// returning players continue into the finished game instead of a dead end.
    /// </summary>
    public void EnsureFinalActProgress()
    {
        // NOTE: room *completion* (the examine set finished) is NOT
        // evidence a fragment was collected — in the current design
        // fragments sit behind puzzles beyond examining (Room 3's
        // jars cannot even be touched until the room is "complete",
        // yet its fragment still sleeps in the last jar). Deriving
        // shards from completion silently granted them and left
        // puzzles standing open. Only standing in a LATER room, or
        // already holding the shard, proves anything now.
        int order = RoomOrder(CurrentRoomName());

        if (order >= 4 || IsShardRecovered(2))
        {
            if (!IsShardRecovered(2))
                CollectShard(2, null, silent: true);
            UnlockVerb(MemoryVerb.Echo, announce: false);
        }
        if (order >= 5 || IsShardRecovered(3))
        {
            if (!IsShardRecovered(3))
                CollectShard(3, null, silent: true);
            UnlockVerb(MemoryVerb.Anchor, announce: false);
        }
        if (order >= 6 || IsShardRecovered(4))
        {
            if (!IsShardRecovered(4))
                CollectShard(4, null, silent: true);
            UnlockVerb(MemoryVerb.Trace, announce: false);
        }
        if (order >= 7 || IsShardRecovered(5))
        {
            if (!IsShardRecovered(5))
                CollectShard(5, null, silent: true);
            UnlockVerb(MemoryVerb.Release, announce: false);
        }

        BackfillVerbLessons();
        BackfillCrossRoomItemsFromProgress();

        if (CountRecovered() >= MaxShards && !_unlockedClues.Contains("fragment_order"))
        {
            _unlockedClues.Add("fragment_order");
            CluesChanged?.Invoke();
        }
    }

    /// <summary>Record an authored ending as cross-run meta-progress and let
    /// go of the run save, so Continue cannot resurrect a finished Mara.</summary>
    public void FinishRun(string endingId)
    {
        if (string.IsNullOrWhiteSpace(endingId))
            return;
        CompletedEnding = endingId;
        _storyFlags.Add($"ending:{endingId}");
        SetActiveVerb(MemoryVerb.None);
        Settings.Instance?.RecordEnding(endingId);
        StoryFlagsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
        DeleteSave();
    }

    // ------------------------------------------------------------------- shards

    public static bool ShardCollected(int index)
    {
        return index >= 0 && index < MaxShards && Globals.SHARDS_COLLECTED[index];
    }

    /// <summary>True whether a fragment is carried or already returned in Room 7.</summary>
    public bool IsShardRecovered(int index)
    {
        return index >= 0 && index < MaxShards &&
               (Globals.SHARDS_COLLECTED[index] || System.Array.IndexOf(PlacedShardBySlot, index) >= 0);
    }

    public int CountCarried()
    {
        int n = 0;
        for (int i = 0; i < MaxShards; i++)
            if (Globals.SHARDS_COLLECTED[i])
                n++;
        return n;
    }

    public int CountPlaced()
    {
        int n = 0;
        if (Globals.Instance == null)
            return 0;
        for (int i = 0; i < MaxShards; i++)
            if (Globals.Instance.SHARDS_PLACED[i])
                n++;
        return n;
    }

    public int CountRecovered() => CountCarried() + CountPlaced();

    /// <summary>
    /// Idempotent shard collection. Records the journal fragment text.
    /// Returns true only the first time (callers use it to gate dialogue/stings).
    /// </summary>
    public bool CollectShard(int index, string fragmentText = null, bool silent = false)
    {
        if (index < 0 || index >= MaxShards)
            return false;

        bool already = Globals.SHARDS_COLLECTED[index] ||
                       System.Array.IndexOf(PlacedShardBySlot, index) >= 0;
        if (string.IsNullOrWhiteSpace(ShardFragments[index]))
        {
            ShardFragments[index] = !string.IsNullOrWhiteSpace(fragmentText)
                ? fragmentText
                : DefaultShardFragments[index];
        }

        if (already)
            return false;

        Globals.SHARDS_COLLECTED[index] = true;
        if (!silent)
            NotifyPuzzleSolved();
        if (!silent)
        {
            AudioManager.Play(Sfx.ShardCollect, -3f);
            Toast($"{ShardTitles[index]} — recovered ({CountRecovered()}/{MaxShards})");
        }
        ShardsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
        UnlockVerbForShard(index, announce: !silent);
        if (CountRecovered() >= MaxShards && !_unlockedClues.Contains("fragment_order"))
        {
            if (silent)
            {
                _unlockedClues.Add("fragment_order");
                CluesChanged?.Invoke();
            }
            else
            {
                UnlockClue("fragment_order");
            }
        }
        if (!silent)
            SaveGame($"shard {index} collected");
        return true;
    }

    /// <summary>
    /// Room 7 placement. Prefers the shard matching the slot; falls back to any
    /// carried shard (preserving the original any-order intent) while recording
    /// exactly what went where.
    /// </summary>
    public ShardPlaceResult TryPlaceShard(int slotIndex)
    {
        if (Globals.Instance == null || slotIndex < 0 || slotIndex >= MaxShards)
            return ShardPlaceResult.NoShardCarried;

        if (Globals.Instance.SHARDS_PLACED[slotIndex])
            return ShardPlaceResult.AlreadyPlaced;

        int shard = -1;
        if (Globals.SHARDS_COLLECTED[slotIndex])
        {
            shard = slotIndex;
        }
        else
        {
            for (int i = 0; i < MaxShards; i++)
            {
                if (Globals.SHARDS_COLLECTED[i])
                {
                    shard = i;
                    break;
                }
            }
        }

        if (shard == -1)
            return ShardPlaceResult.NoShardCarried;

        Globals.SHARDS_COLLECTED[shard] = false;
        Globals.Instance.SHARDS_PLACED[slotIndex] = true;
        PlacedShardBySlot[slotIndex] = shard;

        ShardsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
        SaveGame($"shard {shard} placed in slot {slotIndex}");
        return CountPlaced() >= MaxShards ? ShardPlaceResult.AllPlaced : ShardPlaceResult.Placed;
    }

    /// <summary>
    /// The finished Room 7 is an ordering puzzle, not an any-shard-any-slot
    /// drop: the player chooses an exact fragment for an exact hollow frame,
    /// and may take it back until Release is used.
    /// </summary>
    public ShardPlaceResult TryPlaceSpecificShard(int slotIndex, int shardIndex)
    {
        if (Globals.Instance == null || slotIndex < 0 || slotIndex >= MaxShards ||
            shardIndex < 0 || shardIndex >= MaxShards)
            return ShardPlaceResult.NoShardCarried;

        if (Globals.Instance.SHARDS_PLACED[slotIndex])
            return ShardPlaceResult.AlreadyPlaced;
        if (!Globals.SHARDS_COLLECTED[shardIndex])
            return ShardPlaceResult.NoShardCarried;

        Globals.SHARDS_COLLECTED[shardIndex] = false;
        Globals.Instance.SHARDS_PLACED[slotIndex] = true;
        PlacedShardBySlot[slotIndex] = shardIndex;

        ShardsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
        SaveGame($"shard {shardIndex} placed in slot {slotIndex}");
        return CountPlaced() >= MaxShards ? ShardPlaceResult.AllPlaced : ShardPlaceResult.Placed;
    }

    /// <summary>Take a placed fragment back into Mara's hands for rearrangement.</summary>
    public bool TryRemoveShardFromSlot(int slotIndex, out int shardIndex)
    {
        shardIndex = -1;
        if (Globals.Instance == null || slotIndex < 0 || slotIndex >= MaxShards ||
            !Globals.Instance.SHARDS_PLACED[slotIndex])
            return false;

        shardIndex = PlacedShardBySlot[slotIndex];
        if (shardIndex < 0 || shardIndex >= MaxShards)
            shardIndex = slotIndex; // tolerate very old saves with only placed booleans

        Globals.Instance.SHARDS_PLACED[slotIndex] = false;
        PlacedShardBySlot[slotIndex] = -1;
        Globals.SHARDS_COLLECTED[shardIndex] = true;

        ShardsChanged?.Invoke();
        ObjectiveChanged?.Invoke();
        SaveGame($"shard {shardIndex} removed from slot {slotIndex}");
        return true;
    }

    /// <summary>All six frames filled in the order Mara lived them.</summary>
    public bool FragmentsInOrder()
    {
        if (Globals.Instance == null || CountPlaced() < MaxShards)
            return false;
        for (int i = 0; i < MaxShards; i++)
        {
            if (PlacedShardBySlot[i] != i)
                return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ journal

    public void LogEcho(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        line = line.Trim();
        if (EchoLog.Count > 0 && EchoLog[^1] == line)
            return;

        EchoLog.Add(line);
        while (EchoLog.Count > 80)
            EchoLog.RemoveAt(0);
    }

    // --------------------------------------------------------------------- misc

    public void Toast(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            ToastRequested?.Invoke(text);
    }

    public string StatsSummary()
    {
        int mins = (int)(PlayTimeSeconds / 60);
        int secs = (int)(PlayTimeSeconds % 60);
        return $"Time in the house: {mins}:{secs:00}   ·   Examined: {ExaminesPerformed}   ·   " +
               $"Clarity used: {ClarifiesUsed}   ·   Memory verbs used: {VerbUses}   ·   Memory integrity: {TierDisplayName(CurrentTier)}   ·   Clues noted: {_unlockedClues.Count}";
    }

    // ------------------------------------------------------------- save / load

    public bool HasSave => FileAccess.FileExists(SavePath);

    /// <summary>Set by NewGame only; GameUI plays the post-Play memory cutscene
    /// when Room 1 first opens, then consumes it. Never set by Continue/loads,
    /// and deliberately not part of the save file.</summary>
    public bool CutscenePending { get; private set; }

    public bool ConsumeCutscenePending()
    {
        bool pending = CutscenePending;
        CutscenePending = false;
        return pending;
    }

    public void NewGame()
    {
        Globals.Instance?.ResetRunState();
        RoomExamineTracker.ResetProgress();
        Room1Puzzle.ResetSavedState();
        Room2Puzzle.ResetSavedState();

        for (int i = 0; i < MaxShards; i++)
        {
            ShardFragments[i] = string.Empty;
            PlacedShardBySlot[i] = -1;
        }
        EchoLog.Clear();
        _unlockedClues.Clear();
        _unlockedVerbs.Clear();
        _storyFlags.Clear();
        ActiveVerb = MemoryVerb.None;
        CompletedEnding = string.Empty;
        VerbUses = 0;
        PlayerNotes = string.Empty;
        _persistentReveals.Clear();
        _pendingClarityCarry = null;
        _lastRoomName = null;
        _shattered = false;
        CollapseActive = false;
        _collapseRoom = null;
        CollapseSecondsLeft = 0f;
        _roomsCompleted.Clear();
        _hintLevelByRoom.Clear();
        PlayTimeSeconds = 0;
        ExaminesPerformed = 0;
        ClarifiesUsed = 0;
        CorruptionPeak = 0;
        _clickCorruptionDebt = 0f;
        _lastLooseClickScene = string.Empty;
        _lastLooseClickMsec = 0;
        _pendingStartScene = null;
        _furthestScenePath = string.Empty;
        _fragmentDisplayOrder = ShuffledDisplayOrder();

        AudioManager.Instance?.SetCorruption01(0f);
        CorruptionChanged?.Invoke(0, CorruptionTier.Calm);
        ClarityChanged?.Invoke(ClarityCount);
        ShardsChanged?.Invoke();
        VerbsChanged?.Invoke();
        StoryFlagsChanged?.Invoke();
        DeleteSave();
        CutscenePending = true;
        Logger.Info("GameState: new game.");
    }

    public void DeleteSave()
    {
        if (FileAccess.FileExists(SavePath))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
    }

    public void SaveGame(string reason = "")
    {
        if (Globals.Instance == null)
            return;

        var data = new Dictionary
        {
            ["version"] = 2,
            ["scene"] = SceneManager.CurrentScenePath ?? string.Empty,
            ["corruption"] = Globals.Instance.CORRUPTION_COUNT,
            ["clarity"] = Globals.Instance.CLARITY_TOGGLE_COUNT,
            ["photo1"] = Globals.Instance.PHOTO1_COLLECTED,
            ["playtime"] = PlayTimeSeconds,
            ["examines"] = ExaminesPerformed,
            ["clarifies"] = ClarifiesUsed,
            ["corruption_peak"] = CorruptionPeak,
            ["click_corruption_debt"] = _clickCorruptionDebt,
            ["progress"] = RoomExamineTracker.ExportProgress(),
        };

        var shardsCollected = new Array<bool>();
        var shardsPlaced = new Array<bool>();
        var placedBySlot = new Array<int>();
        var fragments = new Array<string>();
        for (int i = 0; i < MaxShards; i++)
        {
            shardsCollected.Add(Globals.SHARDS_COLLECTED[i]);
            shardsPlaced.Add(Globals.Instance.SHARDS_PLACED[i]);
            placedBySlot.Add(PlacedShardBySlot[i]);
            fragments.Add(ShardFragments[i] ?? string.Empty);
        }
        data["shards_collected"] = shardsCollected;
        data["shards_placed"] = shardsPlaced;
        data["placed_by_slot"] = placedBySlot;
        data["fragments"] = fragments;

        var displayOrder = new Array<int>();
        foreach (int shardIndex in FragmentDisplayOrder)
            displayOrder.Add(shardIndex);
        data["fragment_display_order"] = displayOrder;

        var rooms = new Array<string>();
        foreach (var r in _roomsCompleted)
            rooms.Add(r);
        data["rooms_completed"] = rooms;

        var echoes = new Array<string>();
        foreach (var e in EchoLog)
            echoes.Add(e);
        data["echoes"] = echoes;

        var clues = new Array<string>();
        foreach (var c in _unlockedClues)
            clues.Add(c);
        data["clues"] = clues;
        data["player_notes"] = PlayerNotes;

        var reveals = new Array<string>();
        foreach (var r in _persistentReveals)
            reveals.Add(r);
        data["reveals"] = reveals;
        data["clarity_carry"] = _pendingClarityCarry ?? -1;

        var verbs = new Array<string>();
        foreach (var verb in _unlockedVerbs)
            verbs.Add(verb.ToString());
        data["verbs"] = verbs;

        var flags = new Array<string>();
        foreach (var flag in _storyFlags)
            flags.Add(flag);
        data["story_flags"] = flags;
        data["verb_uses"] = VerbUses;
        data["completed_ending"] = CompletedEnding;
        data["furthest_scene"] = _furthestScenePath;

        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Logger.Error($"GameState: could not open save file ({FileAccess.GetOpenError()})");
            return;
        }
        file.StoreString(Json.Stringify(data));
        Saved?.Invoke();
        Logger.Debug($"GameState: saved ({reason}).");
    }

    /// <summary>Loads the save into Globals/tracker; returns the scene to open, or null.</summary>
    public string LoadGame()
    {
        if (!HasSave || Globals.Instance == null)
            return null;

        string text;
        using (var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read))
        {
            if (file == null)
                return null;
            text = file.GetAsText();
        }

        var parsed = Json.ParseString(text);
        if (parsed.VariantType != Variant.Type.Dictionary ||
            parsed.AsGodotDictionary() is not Dictionary data)
        {
            Logger.Error("GameState: save file is unreadable; starting fresh is recommended.");
            return null;
        }

        NewGameStateOnly();

        Globals.Instance.CORRUPTION_COUNT = Get(data, "corruption", 0).AsInt32();
        if (Globals.Instance.CORRUPTION_COUNT >= ShatterThreshold)
            Globals.Instance.CORRUPTION_COUNT = 13; // never load straight into the ending
        _shattered = false;
        Globals.Instance.CLARITY_TOGGLE_COUNT = Mathf.Clamp(
            Get(data, "clarity", Globals.MaxClarity).AsInt32(), 0, Globals.MaxClarity);
        Globals.Instance.PHOTO1_COLLECTED = Get(data, "photo1", false).AsBool();
        PlayTimeSeconds = Get(data, "playtime", 0).AsDouble();
        ExaminesPerformed = Get(data, "examines", 0).AsInt32();
        ClarifiesUsed = Get(data, "clarifies", 0).AsInt32();
        CorruptionPeak = Get(data, "corruption_peak", Globals.Instance.CORRUPTION_COUNT).AsInt32();
        _clickCorruptionDebt = Mathf.Clamp((float)Get(data, "click_corruption_debt", 0).AsDouble(), 0f, 0.999f);

        ApplyBoolArray(data, "shards_collected", Globals.SHARDS_COLLECTED);
        var placedCopy = new Array<bool>(Globals.Instance.SHARDS_PLACED);
        ApplyBoolArray(data, "shards_placed", placedCopy);
        Globals.Instance.SHARDS_PLACED = placedCopy;

        if (Get(data, "placed_by_slot").VariantType == Variant.Type.Array)
        {
            var arr = data["placed_by_slot"].AsGodotArray();
            for (int i = 0; i < MaxShards && i < arr.Count; i++)
                PlacedShardBySlot[i] = arr[i].AsInt32();
        }

        if (Get(data, "fragments").VariantType == Variant.Type.Array)
        {
            var arr = data["fragments"].AsGodotArray();
            for (int i = 0; i < MaxShards && i < arr.Count; i++)
                ShardFragments[i] = arr[i].AsString();
        }

        if (Get(data, "fragment_display_order").VariantType == Variant.Type.Array)
        {
            var arr = data["fragment_display_order"].AsGodotArray();
            var loaded = new int[MaxShards];
            for (int i = 0; i < MaxShards && i < arr.Count; i++)
                loaded[i] = arr[i].AsInt32();
            if (IsValidDisplayOrder(loaded))
                _fragmentDisplayOrder = loaded;
        }
        if (_fragmentDisplayOrder == null)
            _fragmentDisplayOrder = ShuffledDisplayOrder();

        if (Get(data, "rooms_completed").VariantType == Variant.Type.Array)
        {
            foreach (var v in data["rooms_completed"].AsGodotArray())
                _roomsCompleted.Add(v.AsString());
        }

        if (Get(data, "echoes").VariantType == Variant.Type.Array)
        {
            foreach (var v in data["echoes"].AsGodotArray())
                LogEcho(v.AsString());
        }

        if (Get(data, "clues").VariantType == Variant.Type.Array)
        {
            foreach (var v in data["clues"].AsGodotArray())
            {
                string id = v.AsString();
                if (ClueDefinitions.ContainsKey(id) && !_unlockedClues.Contains(id))
                    _unlockedClues.Add(id);
            }
        }
        PlayerNotes = Get(data, "player_notes", string.Empty).AsString();

        if (Get(data, "reveals").VariantType == Variant.Type.Array)
        {
            foreach (var v in data["reveals"].AsGodotArray())
                _persistentReveals.Add(v.AsString());
        }
        int carry = Get(data, "clarity_carry", -1).AsInt32();
        _pendingClarityCarry = carry >= 0 ? carry : null;

        if (Get(data, "verbs").VariantType == Variant.Type.Array)
        {
            foreach (var v in data["verbs"].AsGodotArray())
            {
                if (Enum.TryParse<MemoryVerb>(v.AsString(), out var verb) && verb != MemoryVerb.None)
                    _unlockedVerbs.Add(verb);
            }
        }
        ActiveVerb = MemoryVerb.None;

        if (Get(data, "story_flags").VariantType == Variant.Type.Array)
        {
            foreach (var v in data["story_flags"].AsGodotArray())
                _storyFlags.Add(v.AsString());
        }
        VerbUses = Get(data, "verb_uses", 0).AsInt32();
        CompletedEnding = Get(data, "completed_ending", string.Empty).AsString();
        _furthestScenePath = Get(data, "furthest_scene", string.Empty).AsString();
        CluesChanged?.Invoke();
        VerbsChanged?.Invoke();
        StoryFlagsChanged?.Invoke();

        if (Get(data, "progress").VariantType == Variant.Type.Dictionary)
            RoomExamineTracker.ImportProgress(data["progress"].AsGodotDictionary());

        BackfillCluesFromProgress();
        EnsureFinalActProgress();

        AudioManager.Instance?.SetCorruption01(Corruption01);
        CorruptionChanged?.Invoke(Globals.Instance.CORRUPTION_COUNT, CurrentTier);
        ClarityChanged?.Invoke(ClarityCount);
        ShardsChanged?.Invoke();
        ObjectiveChanged?.Invoke();

        string scene = Get(data, "scene", string.Empty).AsString();
        // Older saves never recorded progress depth: the room a save opens
        // in is, at minimum, as far as its Mara ever got.
        if (string.IsNullOrWhiteSpace(_furthestScenePath) && IsMainPathScene(scene))
            _furthestScenePath = scene;
        Logger.Info($"GameState: loaded save (scene: {scene}).");
        return string.IsNullOrWhiteSpace(scene) ? null : scene;
    }

    private static void ApplyBoolArray(Dictionary data, string key, Array<bool> target)
    {
        if (Get(data, key).VariantType != Variant.Type.Array)
            return;

        var arr = data[key].AsGodotArray();
        for (int i = 0; i < target.Count && i < arr.Count; i++)
            target[i] = arr[i].AsBool();
    }

    /// <summary>Reset like NewGame but keep the save file we are about to load.</summary>
    private void NewGameStateOnly()
    {
        Globals.Instance?.ResetRunState();
        RoomExamineTracker.ResetProgress();
        Room1Puzzle.ResetSavedState();
        Room2Puzzle.ResetSavedState();
        for (int i = 0; i < MaxShards; i++)
        {
            ShardFragments[i] = string.Empty;
            PlacedShardBySlot[i] = -1;
        }
        EchoLog.Clear();
        _unlockedClues.Clear();
        _unlockedVerbs.Clear();
        _storyFlags.Clear();
        ActiveVerb = MemoryVerb.None;
        CompletedEnding = string.Empty;
        VerbUses = 0;
        PlayerNotes = string.Empty;
        _persistentReveals.Clear();
        _pendingClarityCarry = null;
        _lastRoomName = null;
        _furthestScenePath = string.Empty;
        _fragmentDisplayOrder = null;
        _shattered = false;
        CollapseActive = false;
        _collapseRoom = null;
        CollapseSecondsLeft = 0f;
        _roomsCompleted.Clear();
        _hintLevelByRoom.Clear();
        PlayTimeSeconds = 0;
        ExaminesPerformed = 0;
        ClarifiesUsed = 0;
        CorruptionPeak = 0;
        _clickCorruptionDebt = 0f;
        _lastLooseClickScene = string.Empty;
        _lastLooseClickMsec = 0;
    }

    // ------------------------------------------------------------ continue flow

    /// <summary>Menu → Continue: load now, remember which room to open in GameManager.</summary>
    public bool PrepareContinue()
    {
        _pendingStartScene = LoadGame();
        return _pendingStartScene != null;
    }

    public string ConsumeStartScene(string fallback)
    {
        string scene = _pendingStartScene;
        _pendingStartScene = null;
        return string.IsNullOrWhiteSpace(scene) ? fallback : scene;
    }
}

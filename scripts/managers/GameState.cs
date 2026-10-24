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
        ["room_1.tscn"] = "Room1",
        ["room_1_puzzle.tscn"] = "Room1",
        ["room_2.tscn"] = "Room2",
        ["room_2_puzzle.tscn"] = "Room2",
        ["room_3.tscn"] = "Room3",
        ["room_4.tscn"] = "Room4",
        ["flashback_landing.tscn"] = "Room4",
        ["Room_4_puzzle.tscn"] = "Room4",
        ["room_5.tscn"] = "Room5",
        ["apartment_now.tscn"] = "Room5",
        ["room_6.tscn"] = "Room6",
        ["porch_447.tscn"] = "Room6",
        ["room_7.tscn"] = "Room7",
    };

    /// <summary>Tracker object name → player-facing phrase for hints/journal.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> PrettyObjectNames = new()
    {
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
            "Choose Echo first (E, or its button in the Memory bar), then touch the memory. In the hallway, drift across the photographs rather than clicking them: a rising note means the next word of her mother's sentence. At the brass door, Echo turns clicks into knocks — picture first, then the keyhole beneath it.\n\n" +
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
        if (SceneManager.IsChanging || Dialogue.IsShowing)
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
    /// revealed, two; two revealed, three. Returns how many were revealed.</summary>
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
        "room_1.tscn", "room_1_puzzle.tscn",
        "room_2.tscn", "room_2_puzzle.tscn",
        "room_3.tscn",
        "room_4.tscn", "Room_4_puzzle.tscn",
        "room_5.tscn",
        "room_6.tscn", "porch_447.tscn",
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
            "flashback_landing.tscn" => "room_4.tscn",
            "apartment_now.tscn" => "room_5.tscn",
            _ => file,
        };
        return System.Array.IndexOf(MainPathSceneFiles, file);
    }

    /// <summary>The "go back to the new room" target: the furthest main-path
    /// scene, whenever the player has stepped back behind it.</summary>
    public bool TryGetForwardScene(out string scenePath)
    {
        scenePath = _furthestScenePath;
        if (string.IsNullOrWhiteSpace(scenePath))
            return false;
        return SceneProgressIndex(scenePath) > SceneProgressIndex(SceneManager.CurrentScenePath);
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
            "porch_447.tscn" => "Porch447",
            _ => SceneToRoom.TryGetValue(file, out var room) ? room : null,
        };
    }

    private bool RoomFullyDone(string trackerRoom) => trackerRoom switch
    {
        "Room1" => IsShardRecovered(0),
        "Room2" => IsShardRecovered(1),
        "Room3" => IsShardRecovered(2),
        "Room4" => IsShardRecovered(3) && HasFlag("room4_door_opened"),
        "Room4Flashback" => HasFlag("flashback_landing_seen"),
        "Room5" => IsShardRecovered(4),
        "ApartmentNow" => HasFlag("apartment_seen"),
        "Room6" => IsShardRecovered(5),
        "Porch447" => HasFlag("porch_passed"),
        _ => false,
    };

    /// <summary>A finished room the player has moved beyond: it can be
    /// looked at, but nothing in it answers anymore. Navigation (Step Back /
    /// Return) is never sealed, and neither is a room still being worked —
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

        switch (file)
        {
            case "room_1_puzzle.tscn":
                return "Open the five-dial padlock. The kitchen remembers the combination.";
            case "room_2_puzzle.tscn":
                return "Spell the four-letter word. The sitting room has been saying it all along.";
            case "room_4.tscn":
                if (IsShardRecovered(3) || HasFlag("room4_sentence_complete"))
                    return "The sentence is whole. The hallway door is a door again.";
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
            case "porch_447.tscn":
                RoomExamineTracker.GetRoomProgress("Porch447", out int pDone, out int pTotal, out var pLeft);
                return pLeft.Count > 0
                    ? $"The porch, at 4:47 ({pDone}/{pTotal})."
                    : "The front door. The last room is through it.";
            case "Room_4_puzzle.tscn":
                return HasFlag("room4_door_opened")
                    ? "The way through stands open."
                    : "The door is listening for an old knock.";
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

        if (file == "room_1_puzzle.tscn")
        {
            return NextHintLevel("Room1") switch
            {
                1 => ("Numbers stay. A clock, a calendar, a drawing, a cup — the morning kept count. Your journal (J) keeps what you found.", null),
                2 => (MorningClueStatus(), null),
                _ => ("Read the morning the way it happened: first the stopped clock's hour, then the circled day (it takes two dials), then the number in her drawing, last the ink under her cup. Every one of them is still in the kitchen where she left it — Step Back (Q) and read them off the things themselves.", null),
            };
        }
        if (file == "room_2_puzzle.tscn")
        {
            return NextHintLevel("Room2") switch
            {
                1 => ("What did she call this place, before it was only a house?", null),
                2 => ("Read how her parents' letter ends — the place it begs her to come back to. It's noted in your journal (J, Notes & Clues).", null),
                _ => ("Four letters. Not the building's cold name — the warm word, the one this whole memory is trying to be. The letter in your journal (J) is your witness.", null),
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
            return NextHintLevel("Room4Puzzle") switch
            {
                1 => ("The three pictures are not a row to read once. Each one has a lock directly beneath it.", null),
                2 => ("Think in pairs, left to right: cross and turn, enter and turn, close and turn. Echo must be armed for a click to count.", null),
                _ => ("Echo armed (E): work left to right in pairs — touch a brass picture, then the keyhole directly beneath it. Three pairs, no skips. Examine each piece first and it will tell you its place in the pattern.", null),
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

        if (file == "flashback_landing.tscn")
            return ("Nothing here can be changed — only finally seen. The photograph, the light, the runner. The end of the hall leads back when you are ready.", null);

        if (file == "apartment_now.tscn")
            return ("This is not a puzzle room. It is the life that continued while she was in the house. The mail, the phone, the chair. The door leads back.", null);

        if (file == "porch_447.tscn")
            return ("She stopped here for twelve years without ever stopping. The light, the steps, the path. The front door is the way on.", null);

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
    /// Older saves may sit at the old "Part 1 End" with rooms completed but
    /// no late fragments/verbs (those collectors did not exist yet). Derive
    /// them from durable progress whenever a room opens or a save loads, so
    /// returning players continue into the finished game instead of a dead end.
    /// </summary>
    public void EnsureFinalActProgress()
    {
        int order = RoomOrder(CurrentRoomName());

        if (IsRoomCompleted("Room3") || order >= 4 || IsShardRecovered(2))
        {
            if (!IsShardRecovered(2))
                CollectShard(2, null, silent: true);
            UnlockVerb(MemoryVerb.Echo, announce: false);
        }
        if (IsRoomCompleted("Room4") || order >= 5 || IsShardRecovered(3))
        {
            if (!IsShardRecovered(3))
                CollectShard(3, null, silent: true);
            UnlockVerb(MemoryVerb.Anchor, announce: false);
        }
        if (IsRoomCompleted("Room5") || order >= 6 || IsShardRecovered(4))
        {
            if (!IsShardRecovered(4))
                CollectShard(4, null, silent: true);
            UnlockVerb(MemoryVerb.Trace, announce: false);
        }
        if (IsRoomCompleted("Room6") || order >= 7 || IsShardRecovered(5))
        {
            if (!IsShardRecovered(5))
                CollectShard(5, null, silent: true);
            UnlockVerb(MemoryVerb.Release, announce: false);
        }

        BackfillVerbLessons();

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

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
    public bool IsShattered => _shattered;

    private static readonly string[] TierNames = ["Intact", "Strained", "Fractured", "Shattered"];

    public static readonly string[] ShardTitles =
    [
        "Fragment I — The Brass Key",
        "Fragment II — The Photograph",
        "Fragment III",
        "Fragment IV",
        "Fragment V",
        "Fragment VI",
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
        ["room_5.tscn"] = "Room5",
        ["room_6.tscn"] = "Room6",
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
    };

    // -------------------------------------------------------------------- state
    /// <summary>Journal fragment bodies, per shard index ("" = not remembered).</summary>
    public string[] ShardFragments { get; } = ["", "", "", "", "", ""];

    /// <summary>Shard index placed in each Room 7 slot, -1 = empty.</summary>
    public int[] PlacedShardBySlot { get; } = [-1, -1, -1, -1, -1, -1];

    /// <summary>Recent dialogue lines (the journal's "Echoes"). Oldest first.</summary>
    public List<string> EchoLog { get; } = new();

    private readonly HashSet<string> _roomsCompleted = new();

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
        ["clock_hour"] = ("Kitchen clock — 7",
            "Stopped at 7:42 — the time she left that morning. The hour: 7."),
        ["calendar_day"] = ("Circled calendar — 14",
            "October 14th, circled hard. The day: 14. Two digits — 1, then 4."),
        ["drawing_eight"] = ("The drawing — 8",
            "She drew the 8 the way you do when you've just learned how. Oversized. Pleased with it."),
        ["cup_number"] = ("Ink on the cup — 3",
            "A 3 written in ink on the base of the cup, like someone pressed the pen down too hard while writing something else nearby."),
        ["morning_order"] = ("The morning, in order",
            "The drawer lock remembers that morning the way she does — in the order it happened:\n\n" +
            "the hour she left, the day circled on the calendar, the number she drew, the number under her cup.\n\n" +
            "(Five dials: the calendar gives two of them.)"),
        ["letter_home"] = ("Her parents' letter — the word",
            "\"Come home when you can — the house misses you.\"\n\n" +
            "The table lock wants four letters, spelled the way the word is spelled."),
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
    }

    private void OnSceneChanged(string scenePath)
    {
        AudioManager.Instance?.PlayMusicForScene(scenePath);

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
        }
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

    /// <summary>The offer appears when Clarity is armed: a nearly-known room
    /// (at least 60% examined, never more than 5 required) and >= 4 charges.</summary>
    public bool CanRecollect(out List<string> remaining, out int nextRoomClarity)
    {
        remaining = new List<string>();
        nextRoomClarity = 0;
        if (_shattered || ClarityCount < 4)
            return false;

        string room = CurrentRoomName();
        if (room == null)
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

    /// <summary>Spend ALL Clarity: ring everything left in this room until it
    /// is faced. The next room's Clarity is set to (revealed + 1): one
    /// revealed, two; two revealed, three. Returns how many were revealed.</summary>
    public int PerformRecollection()
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
        AudioManager.Play(Sfx.ClarifyReveal, -3f);
        Toast(remaining.Count == 1
            ? "One last thing shows itself. All Clarity spent."
            : $"The room gives up its remaining {remaining.Count} secrets. All Clarity spent.");
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
                return "Open the five-dial padlock. The combination is hidden in the kitchen — Step Back (Q) to go and look.";
            case "room_2_puzzle.tscn":
                return "Spell the four-letter word to open the lock. The sitting room has been saying it all along — Step Back (Q) to check.";
            case "room_4.tscn":
                return "Cross the hallway. Face what it shows you, then take the door.";
            case "room_7.tscn":
                return $"Place the fragments in their slots ({CountPlaced()}/{MaxShards})";
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
                _ => ("The morning in order: the hour she left (7), the circled day (1 · 4), the 8 she drew, the number under her cup (3).", null),
            };
        }
        if (file == "room_2_puzzle.tscn")
        {
            return NextHintLevel("Room2") switch
            {
                1 => ("What did she call this place, before it was a house?", null),
                2 => ("Her parents wrote it plainly, in the letter: \"Come home when you can.\" (It's noted in your journal — J.)", null),
                _ => ("Four letters, spelled the way the word is spelled: H-O-M-E.", null),
            };
        }
        if (file == "room_4.tscn")
            return ("The photographs are a sentence. Drift along them, in order, and listen.", null);
        if (file == "room_7.tscn")
            return (CountCarried() > 0
                ? "You are carrying pieces of her. The hollow frames are waiting."
                : "Nothing to return yet. The house still holds its fragments.", null);

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
            MemoryShattered?.Invoke();
        }
    }

    /// <summary>After the bad ending: pull the memory back from the brink —
    /// still Fractured (the house doesn't forget being dropped), a little
    /// Clarity to stand on, saved. Also un-latches the shatter.</summary>
    public void RecoverFromShatter()
    {
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
    }

    /// <returns>True if a charge was actually restored.</returns>
    public bool RefillClarity(int amount)
    {
        if (Globals.Instance == null)
            return false;

        int before = Globals.Instance.CLARITY_TOGGLE_COUNT;
        Globals.Instance.CLARITY_TOGGLE_COUNT = Mathf.Min(Globals.MaxClarity, before + amount);
        ClarityChanged?.Invoke(Globals.Instance.CLARITY_TOGGLE_COUNT);
        return Globals.Instance.CLARITY_TOGGLE_COUNT > before;
    }

    // ------------------------------------------------------------------- shards

    public static bool ShardCollected(int index)
    {
        return index >= 0 && index < MaxShards && Globals.SHARDS_COLLECTED[index];
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
    public bool CollectShard(int index, string fragmentText = null)
    {
        if (index < 0 || index >= MaxShards)
            return false;

        bool already = Globals.SHARDS_COLLECTED[index] ||
                       System.Array.IndexOf(PlacedShardBySlot, index) >= 0;
        if (!string.IsNullOrWhiteSpace(fragmentText) && string.IsNullOrWhiteSpace(ShardFragments[index]))
            ShardFragments[index] = fragmentText;

        if (already)
            return false;

        Globals.SHARDS_COLLECTED[index] = true;
        AudioManager.Play(Sfx.ShardCollect, -3f);
        Toast($"{ShardTitles[index]} — recovered ({CountRecovered()}/{MaxShards})");
        ShardsChanged?.Invoke();
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
               $"Clarity used: {ClarifiesUsed}   ·   Memory integrity: {TierDisplayName(CurrentTier)}   ·   Clues noted: {_unlockedClues.Count}";
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
        PlayerNotes = string.Empty;
        _persistentReveals.Clear();
        _pendingClarityCarry = null;
        _lastRoomName = null;
        _shattered = false;
        _roomsCompleted.Clear();
        _hintLevelByRoom.Clear();
        PlayTimeSeconds = 0;
        ExaminesPerformed = 0;
        ClarifiesUsed = 0;
        CorruptionPeak = 0;
        _pendingStartScene = null;

        AudioManager.Instance?.SetCorruption01(0f);
        CorruptionChanged?.Invoke(0, CorruptionTier.Calm);
        ClarityChanged?.Invoke(ClarityCount);
        ShardsChanged?.Invoke();
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
            ["version"] = 1,
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
        CluesChanged?.Invoke();

        if (Get(data, "progress").VariantType == Variant.Type.Dictionary)
            RoomExamineTracker.ImportProgress(data["progress"].AsGodotDictionary());

        BackfillCluesFromProgress();

        AudioManager.Instance?.SetCorruption01(Corruption01);
        CorruptionChanged?.Invoke(Globals.Instance.CORRUPTION_COUNT, CurrentTier);
        ClarityChanged?.Invoke(ClarityCount);
        ShardsChanged?.Invoke();
        ObjectiveChanged?.Invoke();

        string scene = Get(data, "scene", string.Empty).AsString();
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
        PlayerNotes = string.Empty;
        _persistentReveals.Clear();
        _pendingClarityCarry = null;
        _lastRoomName = null;
        _shattered = false;
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

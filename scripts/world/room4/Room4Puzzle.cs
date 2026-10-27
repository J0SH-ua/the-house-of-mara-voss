using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Room 4 close-up — the hallway door's three ordinary keys.
///
/// Room 4 proper teaches Echo by turning ten photographs into a sentence.
/// This door is deliberately not another pattern to knock back at the
/// house. Its three brass plates are labels for three physical keyholes:
/// the copper bridge key from the kitchen drawer, the little door key
/// from behind the sitting-room photograph, and the circle key from the
/// last preserve jar. Mara carries all three here and puts them in by
/// hand — and any hole will take any key. The holes do not judge; the
/// door does, and only when all three stand in it. A mixed-up door
/// simply refuses, Mara wonders aloud if she mixed them up, and a
/// click lifts a placed key back out to try again. Even a true set
/// will not open until the hallway sentence is whole.
/// </summary>
public partial class Room4Puzzle : MemoryRoomBase
{
    private const string Room5ScenePath = "res://scenes/world/room_5.tscn";
    private const string SentenceFlag = "room4_sentence_complete";
    private const string OpenedFlag = "room4_door_opened";

    private const string BridgeKeyFlag = "item_hall_key_bridge";
    private const string DoorKeyFlag = "item_hall_key_door";
    private const string CircleKeyFlag = "item_hall_key_circle";
    private const string BridgeInsertedFlag = "room4_key_bridge_inserted";
    private const string DoorInsertedFlag = "room4_key_door_inserted";
    private const string CircleInsertedFlag = "room4_key_circle_inserted";

    private const string BridgeKeyIconPath = "res://assets/images/core/items/bridge_key.png";
    private const string DoorKeyIconPath = "res://assets/images/core/items/door_key.png";
    private const string CircleKeyIconPath = "res://assets/images/core/items/circle_key.png";

    // DoorCloseup.png is a 1122x1402 portrait. This 1122x631 band has the
    // same 16:9 proportions as the room viewport and contains the three
    // brass plates plus the three keyholes without squashing the door.
    private static readonly Rect2 DoorSourceRect = new(0, 410, 1122, 631);

    private Texture2D _doorTexture;
    private Texture2D _bridgeKeyTexture;
    private Texture2D _doorKeyTexture;
    private Texture2D _circleKeyTexture;
    private Label _statusLabel;
    private Label _trayLabel;
    private bool _opened;

    /// <summary>One carried key resting on the ledge, waiting for a hand.
    /// The door is worked the way doors are: pick the key up, carry it
    /// to a hole, turn it. Any hole takes any key — a true key turns
    /// its full, easy turn, a borrowed bed only turns partway.</summary>
    private sealed class TrayKey
    {
        public string ShortId;
        public string KeyholeId;
        public string KeyFlag;
        public string InsertedFlag;
        public string ShortName;
        public Texture2D Texture;
        public Rect2 TrayRect;
    }

    private static readonly Rect2 TrayRect = new(78, 196, 144, 320);
    private readonly List<TrayKey> _trayKeys = new();
    private TrayKey _dragKey;
    private Vector2 _dragPosition;
    private TrayKey _turningKey;
    private string _turningHoleId;
    private float _turnProgress;
    private bool _turnSoundPlayed;
    private int _mixedUpCount;
    private bool _removedOnce;

    protected override string RoomName => "Room4";
    protected override string BackgroundPath => "res://assets/images/bgs/DoorCloseup.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Coordinates map the DoorSourceRect crop above onto 1152x648.
        // The door body comes first, so the six brass pieces below win
        // any overlap; once unlocked, a click anywhere opens it.
        yield return new Hotspot("HallwayDoor", "Hallway Door",
            new Rect2(312, 70, 552, 470), new Vector2(588, 325),
            Sfx.DoorCreak, 0.9f, PointerCursorPath, false);
        // The bridge plate starts at x=312 so the legacy BackArrow's
        // large click shape (x 85..305) can never steal its left edge.
        yield return new Hotspot("BridgePlate", "Bridge Plate",
            new Rect2(312, 105, 145, 210), new Vector2(382, 212),
            Sfx.DoorCreak, 0.9f, PointerCursorPath, false);
        yield return new Hotspot("DoorPlate", "Little Door Plate",
            new Rect2(492, 105, 172, 210), new Vector2(578, 212),
            Sfx.DoorCreak, 1f, PointerCursorPath, false);
        yield return new Hotspot("CirclePlate", "Circle Plate",
            new Rect2(697, 105, 167, 210), new Vector2(780, 212),
            Sfx.DoorCreak, 1.1f, PointerCursorPath, false);
        yield return new Hotspot("LeftKeyhole", "Bridge Keyhole",
            new Rect2(327, 390, 100, 150), new Vector2(377, 465),
            Sfx.LockRattle, 0.9f, PointerCursorPath, false);
        yield return new Hotspot("CenterKeyhole", "Little Door Keyhole",
            new Rect2(522, 390, 105, 150), new Vector2(575, 465),
            Sfx.LockRattle, 1f, PointerCursorPath, false);
        yield return new Hotspot("RightKeyhole", "Circle Keyhole",
            new Rect2(723, 390, 104, 150), new Vector2(775, 465),
            Sfx.LockRattle, 1.1f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        _doorTexture = GD.Load<Texture2D>(BackgroundPath);
        _bridgeKeyTexture = GD.Load<Texture2D>(BridgeKeyIconPath);
        _doorKeyTexture = GD.Load<Texture2D>(DoorKeyIconPath);
        _circleKeyTexture = GD.Load<Texture2D>(CircleKeyIconPath);

        // The authored scene still carries its old full-door Sprite2D.
        // The root draws the correctly cropped door so inserted keys sit
        // above the artwork instead of underneath that sprite.
        GetNodeOrNull<CanvasItem>("DoorCloseup2")?.Hide();

        _opened = GameState.Instance?.HasFlag(OpenedFlag) ?? false;

        _statusLabel = CreateLabel(string.Empty,
            new Vector2(276, 38), 21,
            new Color(1f, 0.9f, 0.62f, 0.92f), new Vector2(600, 36));
        _trayLabel = CreateLabel("Mara's keys",
            new Vector2(78, 164), 18,
            new Color(1f, 0.9f, 0.62f, 0.85f), new Vector2(144, 28));
        RefreshTray();
        UpdateStatusLabel();

        if (!_opened)
        {
            _ = PlayStoryLessonOnceAsync("story_lesson_room4_door_keys",
                "Up close, it isn't a pattern at all. Three keyholes. Three ordinary keys, if this house has ever given me anything ordinary to carry.");
        }
    }

    /// <summary>Rebuild the ledge: every carried key whose hole is still
    /// empty, in bridge–door–circle order, on its own peg.</summary>
    private void RefreshTray()
    {
        _trayKeys.Clear();
        var gs = GameState.Instance;
        if (gs == null || _opened)
        {
            if (_trayLabel != null)
                _trayLabel.Visible = false;
            return;
        }

        void AddIfCarried(string shortId, string keyholeId, string keyFlag, string insertedFlag,
            string shortName, Texture2D texture)
        {
            if (!gs.HasFlag(keyFlag) || gs.HasFlag(insertedFlag))
                return;
            float cy = 258f + _trayKeys.Count * 96f;
            _trayKeys.Add(new TrayKey
            {
                ShortId = shortId,
                KeyholeId = keyholeId,
                KeyFlag = keyFlag,
                InsertedFlag = insertedFlag,
                ShortName = shortName,
                Texture = texture,
                TrayRect = new Rect2(150f - 42f, cy - 42f, 84f, 84f),
            });
        }

        AddIfCarried("bridge", "LeftKeyhole", BridgeKeyFlag, BridgeInsertedFlag, "copper bridge key", _bridgeKeyTexture);
        AddIfCarried("door", "CenterKeyhole", DoorKeyFlag, DoorInsertedFlag, "little door key", _doorKeyTexture);
        AddIfCarried("circle", "RightKeyhole", CircleKeyFlag, CircleInsertedFlag, "circle key", _circleKeyTexture);

        if (_trayLabel != null)
            _trayLabel.Visible = _trayKeys.Count > 0;
    }

    private TrayKey TrayKeyAt(Vector2 position)
    {
        foreach (var key in _trayKeys)
        {
            if (key.TrayRect.HasPoint(position))
                return key;
        }
        return null;
    }

    private bool HandsFree() =>
        !Busy && !_opened && _turningKey == null &&
        !Dialogue.IsShowing && !Dialogue.IsInputBlocked &&
        !GetTree().Paused && !SceneManager.IsChanging;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
        {
            Vector2 pos = GetGlobalMousePosition();
            if (mouse.Pressed && _dragKey == null && HandsFree() &&
                !(GameUI.Instance != null && GameUI.Instance.IsPointOverInteractiveHud(pos)))
            {
                var key = TrayKeyAt(pos);
                if (key != null)
                {
                    _dragKey = key;
                    _dragPosition = pos;
                    AudioManager.Play(Sfx.KeyType, -12f, 1.12f);
                    GetTree().Root.SetInputAsHandled();
                    QueueRedraw();
                    return;
                }
            }
            else if (!mouse.Pressed && _dragKey != null)
            {
                FinishDrag(pos);
                GetTree().Root.SetInputAsHandled();
                return;
            }
        }

        base._Input(@event);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_dragKey != null)
        {
            _dragPosition = GetGlobalMousePosition();
            QueueRedraw();
        }

        if (_turningKey != null)
        {
            _turnProgress = Mathf.MoveToward(_turnProgress, 1f, (float)delta / 0.8f);
            if (!_turnSoundPlayed && _turnProgress > 0.5f)
            {
                _turnSoundPlayed = true;
                var hotspot = GetHotspot(_turningHoleId);
                bool home = _turningKey.KeyholeId == _turningHoleId;
                if (home)
                    AudioManager.Play(Sfx.LockOpen, -7f, hotspot?.SfxPitch ?? 1f);
                else
                {
                    // A borrowed bed sounds like one, if she listens.
                    AudioManager.Play(Sfx.LockRattle, -9f, (hotspot?.SfxPitch ?? 1f) * 0.82f);
                    AudioManager.Play(Sfx.KeyType, -12f, 0.8f);
                }
            }
            QueueRedraw();
            if (_turnProgress >= 1f)
            {
                var done = _turningKey;
                string hole = _turningHoleId;
                _turningKey = null;
                _turningHoleId = null;
                _ = FinishInsertAsync(done, hole);
            }
        }
    }

    private void FinishDrag(Vector2 pos)
    {
        var key = _dragKey;
        _dragKey = null;
        if (key == null)
            return;

        string[] keyholeIds = ["LeftKeyhole", "CenterKeyhole", "RightKeyhole"];
        foreach (var id in keyholeIds)
        {
            var hotspot = GetHotspot(id);
            if (hotspot == null || !hotspot.Rect.Grow(12f).HasPoint(pos))
                continue;

            if (KeyInHole(id) != null)
            {
                AudioManager.Play(Sfx.LockRattle, -11f, 0.9f);
                GameState.Instance?.Toast("That hole already has a key standing in it. Click it to lift it back out.");
                QueueRedraw();
                return;
            }

            // Any hole takes any key. The door is the judge, later.
            BeginInsert(key, hotspot);
            return;
        }

        // Let go anywhere else and the key simply goes back on its peg.
        AudioManager.Play(Sfx.KeyType, -13f, 0.9f);
        QueueRedraw();
    }

    private void BeginInsert(TrayKey key, Hotspot hotspot)
    {
        Busy = true;
        _turningKey = key;
        _turningHoleId = hotspot.Id;
        _turnProgress = 0f;
        _turnSoundPlayed = false;
        AudioManager.Play(Sfx.KeyType, -8f, hotspot.SfxPitch);
        QueueRedraw();
    }

    /// <summary>A placed key can always be lifted back out. Mixing
    /// them up is information, not a dead end.</summary>
    private async Task RemoveKeyAsync(Hotspot hotspot, string shortId)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            var key = TrayKeyFor(shortId);
            if (gs == null || key == null)
                return;

            AudioManager.Play(Sfx.KeyType, -9f, 0.85f);
            AudioManager.Play(Sfx.LockRattle, -12f, 0.9f);
            gs.ClearFlag(HoleFlag(hotspot.Id, shortId), save: false);
            gs.ClearFlag(key.InsertedFlag, save: false);
            gs.SaveGame("hallway key lifted out");
            RefreshTray();
            UpdateStatusLabel();
            QueueRedraw();

            if (!_removedOnce)
            {
                _removedOnce = true;
                await Say("Out it comes. No harm done — a key can be wrong without being stuck. The plates again: bridge, little door, circle.", 4.2f, 23);
            }
            else
            {
                gs.Toast($"The {key.ShortName} goes back on its peg.");
            }
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    /// <summary>Empty hole, nothing on the ledge, but this hole's own
    /// key exists — standing in somebody else's bed.</summary>
    private async Task WrongBedHintAsync(Hotspot hotspot)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.LockRattle, -10f, hotspot.SfxPitch);
            await Say("Empty — and the key that belongs here is standing over there, in somebody else's bed. Click it and it will come back out.", 4.2f, 23);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    /// <summary>Three keys in, sentence whole, and the door says
    /// nothing at all. It takes Mara a moment to say it herself.</summary>
    private async Task SayMixedUpAsync()
    {
        AudioManager.Play(Sfx.LockRattle, -6f, 0.8f);
        AudioManager.Play(Sfx.DoorCreak, -11f, 0.7f);
        if (_mixedUpCount == 0)
        {
            await SaySequence(
                "Three keys in. The sentence whole. And the door just… refuses. Quietly. Like it isn't even a door.",
                "Maybe I mixed them up. The plates were never decoration, Mara. Bridge. Little door. Circle. Look at who is actually standing where.");
            GameState.Instance?.Toast("Click a placed key to lift it back out, then try its own bed.");
        }
        else
        {
            await Say("Still nothing. Somebody is still in the wrong bed.", 3.6f, 23);
        }
        _mixedUpCount++;
    }

    public override void _Draw()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        if (_doorTexture != null)
            DrawTextureRectRegion(_doorTexture, screen, DoorSourceRect, Colors.White);
        else
            DrawRect(screen, new Color(0.03f, 0.025f, 0.02f));

        // This room replaces the base _Draw to crop the portrait door;
        // keep the ordinary hover outline the base would have drawn.
        DrawHoveredHotspotOutline();
        DrawInsertedKey("LeftKeyhole");
        DrawInsertedKey("CenterKeyhole");
        DrawInsertedKey("RightKeyhole");
        DrawTurningKey();
        DrawTray();
        DrawDraggedKey();
    }

    private void DrawTray()
    {
        if (_trayKeys.Count == 0 || _opened)
            return;

        // A plain wooden ledge on the wall beside the door, with a peg
        // for every key still in Mara's hand.
        DrawRect(TrayRect, new Color(0.09f, 0.065f, 0.045f, 0.92f), true);
        DrawRect(TrayRect, new Color(0.62f, 0.48f, 0.3f, 0.85f), false, 2f);
        DrawRect(new Rect2(TrayRect.Position + new Vector2(8, 8), TrayRect.Size - new Vector2(16, 16)),
            new Color(0.4f, 0.3f, 0.18f, 0.4f), false, 1f);

        foreach (var key in _trayKeys)
        {
            var center = key.TrayRect.GetCenter();
            DrawCircle(center + new Vector2(0, -34), 4f, new Color(0.72f, 0.58f, 0.34f, 0.9f));
            if (key == _dragKey || key == _turningKey)
                continue;
            bool hover = HandsFree() && key.TrayRect.HasPoint(GetGlobalMousePosition());
            if (hover)
                DrawArc(center, 50f, 0f, Mathf.Tau, 40,
                    new Color(1f, 0.86f, 0.52f, 0.75f), 2f, true);
            if (key.Texture != null)
                DrawTextureRect(key.Texture, key.TrayRect, false);
            else
                DrawCircle(center, 9f, new Color(1f, 0.88f, 0.52f, 0.9f));
        }
    }

    private void DrawDraggedKey()
    {
        if (_dragKey == null || _dragKey.Texture == null)
            return;
        var rect = new Rect2(_dragPosition - new Vector2(46, 46), new Vector2(92, 92));
        DrawCircle(_dragPosition + new Vector2(4, 10), 40f, new Color(0, 0, 0, 0.3f));
        DrawTextureRect(_dragKey.Texture, rect, false);

        // The hole it is hovering over, agreed or not, answers quietly.
        string[] keyholeIds = ["LeftKeyhole", "CenterKeyhole", "RightKeyhole"];
        foreach (var id in keyholeIds)
        {
            var hotspot = GetHotspot(id);
            if (hotspot != null && hotspot.Rect.Grow(12f).HasPoint(_dragPosition) &&
                KeyInHole(id) == null)
            {
                DrawArc(hotspot.Center, 56f, 0f, Mathf.Tau, 48,
                    new Color(1f, 0.88f, 0.52f, 0.65f), 2.5f, true);
            }
        }
    }

    private void DrawTurningKey()
    {
        if (_turningKey == null || _turningKey.Texture == null)
            return;
        var hotspot = GetHotspot(_turningHoleId ?? _turningKey.KeyholeId);
        if (hotspot == null)
            return;

        // The whole small ceremony of a key: it goes in sideways, her
        // wrist rolls, and the bow comes level as the lock lets go —
        // or stops partway, in a borrowed bed, and stays a little crooked.
        bool home = _turningKey.KeyholeId == hotspot.Id;
        float eased = 1f - Mathf.Pow(1f - Mathf.Clamp(_turnProgress, 0f, 1f), 3f);
        float angle = Mathf.Lerp(-1.25f, home ? 0f : -0.55f, eased);
        DrawSetTransform(hotspot.Center, angle, Vector2.One);
        DrawTextureRect(_turningKey.Texture, new Rect2(-46, -46, 92, 92), false);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawInsertedKey(string hotspotId)
    {
        string shortId = KeyInHole(hotspotId);
        if (shortId == null)
            return;
        if (_turningKey != null && _turningHoleId == hotspotId)
            return; // mid-turn; DrawTurningKey has it
        var hotspot = GetHotspot(hotspotId);
        if (hotspot == null)
            return;

        var gold = new Color(1f, 0.88f, 0.52f, 0.9f);
        DrawArc(hotspot.Center, 48f, 0f, Mathf.Tau, 48, gold, 2.2f, true);
        DrawArc(hotspot.Center, 40f, 0f, Mathf.Tau, 40,
            new Color(1f, 0.88f, 0.52f, 0.24f), 1.2f, true);

        var texture = TextureForKey(shortId);
        bool home = HomeKeyholeFor(shortId) == hotspotId;
        if (texture != null)
        {
            if (home)
            {
                var rect = new Rect2(hotspot.Center - new Vector2(46, 46), new Vector2(92, 92));
                DrawTextureRect(texture, rect, false);
            }
            else
            {
                // It stands a little crooked, the way it stopped turning.
                DrawSetTransform(hotspot.Center, -0.55f, Vector2.One);
                DrawTextureRect(texture, new Rect2(-46, -46, 92, 92), false);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
        }
        else
        {
            DrawCircle(hotspot.Center, 8f, gold);
        }
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        if (_opened)
        {
            _ = ProceedThroughDoorAsync();
            return;
        }

        if (IsKeyhole(hotspot.Id))
        {
            // Putting a physical key in a physical hole is ordinary-hand
            // work. It stays available even if a Memory Verb happens to
            // be armed, rather than making the player put the verb down
            // before the bag is allowed to matter.
            string standing = KeyInHole(hotspot.Id);
            if (standing != null)
                _ = RemoveKeyAsync(hotspot, standing);
            else if (_trayKeys.Count > 0)
                _ = DragHintAsync(hotspot);
            else if (HasKeyFor(hotspot.Id))
                _ = WrongBedHintAsync(hotspot);
            else
                _ = MissingKeyAsync(hotspot);
            return;
        }

        var activeVerb = MemoryVerbSystem.Active;
        if (activeVerb != MemoryVerb.None)
        {
            _ = WrongVerbAsync(activeVerb);
            return;
        }

        if (hotspot.Id == "HallwayDoor")
        {
            if (AllPlacedRight() && SentenceIsComplete())
                _ = CompleteDoorAsync();
            else
                _ = LockedDoorAsync();
        }
        else
        {
            _ = ExamineDoorPartAsync(hotspot);
        }
    }

    private bool SentenceIsComplete()
    {
        var gs = GameState.Instance;
        return gs != null && (gs.HasFlag(SentenceFlag) || gs.IsShardRecovered(3));
    }

    private static bool IsKeyhole(string id) =>
        id is "LeftKeyhole" or "CenterKeyhole" or "RightKeyhole";

    private static string KeyFlagFor(string keyholeId) => keyholeId switch
    {
        "LeftKeyhole" => BridgeKeyFlag,
        "CenterKeyhole" => DoorKeyFlag,
        "RightKeyhole" => CircleKeyFlag,
        _ => string.Empty,
    };

    private static string InsertedFlagFor(string keyholeId) => keyholeId switch
    {
        "LeftKeyhole" => BridgeInsertedFlag,
        "CenterKeyhole" => DoorInsertedFlag,
        "RightKeyhole" => CircleInsertedFlag,
        _ => string.Empty,
    };

    private bool HasKeyFor(string keyholeId)
    {
        var gs = GameState.Instance;
        string flag = KeyFlagFor(keyholeId);
        return gs != null && !string.IsNullOrEmpty(flag) && gs.HasFlag(flag);
    }

    private bool IsInserted(string insertedFlag)
    {
        if (_opened)
            return true;
        var gs = GameState.Instance;
        return gs != null && !string.IsNullOrEmpty(insertedFlag) && gs.HasFlag(insertedFlag);
    }

    private int InsertedCount()
    {
        int count = 0;
        if (IsInserted(BridgeInsertedFlag)) count++;
        if (IsInserted(DoorInsertedFlag)) count++;
        if (IsInserted(CircleInsertedFlag)) count++;
        return count;
    }

    private bool AllKeysInserted() => InsertedCount() >= 3;

    // ------------------------------------------------------ placements

    private static string HoleWord(string keyholeId) => keyholeId switch
    {
        "LeftKeyhole" => "left",
        "CenterKeyhole" => "center",
        "RightKeyhole" => "right",
        _ => string.Empty,
    };

    private static string HomeKeyholeFor(string shortId) => shortId switch
    {
        "bridge" => "LeftKeyhole",
        "door" => "CenterKeyhole",
        "circle" => "RightKeyhole",
        _ => string.Empty,
    };

    private static string HoleFlag(string keyholeId, string shortId) =>
        $"room4_hole_{HoleWord(keyholeId)}_{shortId}";

    /// <summary>Which key (by short id) stands in a hole, or null.
    /// Once the door has opened, every hole holds its true key.</summary>
    private string KeyInHole(string keyholeId)
    {
        if (_opened)
        {
            return keyholeId switch
            {
                "LeftKeyhole" => "bridge",
                "CenterKeyhole" => "door",
                "RightKeyhole" => "circle",
                _ => null,
            };
        }
        var gs = GameState.Instance;
        if (gs == null)
            return null;
        foreach (string shortId in new[] { "bridge", "door", "circle" })
        {
            if (gs.HasFlag(HoleFlag(keyholeId, shortId)))
                return shortId;
        }
        return null;
    }

    private int PlacedCount()
    {
        int n = 0;
        if (KeyInHole("LeftKeyhole") != null) n++;
        if (KeyInHole("CenterKeyhole") != null) n++;
        if (KeyInHole("RightKeyhole") != null) n++;
        return n;
    }

    private bool AllPlacedRight() =>
        _opened ||
        (KeyInHole("LeftKeyhole") == "bridge" &&
         KeyInHole("CenterKeyhole") == "door" &&
         KeyInHole("RightKeyhole") == "circle");

    private Texture2D TextureForKey(string shortId) => shortId switch
    {
        "bridge" => _bridgeKeyTexture,
        "door" => _doorKeyTexture,
        "circle" => _circleKeyTexture,
        _ => null,
    };

    private TrayKey TrayKeyFor(string shortId)
    {
        // A placed key is not on the ledge; rebuild its handle from the
        // flags so the turn/removal code speaks one language.
        return shortId switch
        {
            "bridge" => new TrayKey { ShortId = "bridge", KeyholeId = "LeftKeyhole", KeyFlag = BridgeKeyFlag, InsertedFlag = BridgeInsertedFlag, ShortName = "copper bridge key", Texture = _bridgeKeyTexture },
            "door" => new TrayKey { ShortId = "door", KeyholeId = "CenterKeyhole", KeyFlag = DoorKeyFlag, InsertedFlag = DoorInsertedFlag, ShortName = "little door key", Texture = _doorKeyTexture },
            "circle" => new TrayKey { ShortId = "circle", KeyholeId = "RightKeyhole", KeyFlag = CircleKeyFlag, InsertedFlag = CircleInsertedFlag, ShortName = "circle key", Texture = _circleKeyTexture },
            _ => null,
        };
    }

    /// <summary>Clicked an empty hole while carrying keys: the door is
    /// not worked by clicking any more. Say so once, plainly.</summary>
    private async Task DragHintAsync(Hotspot hotspot)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.LockRattle, -10f, hotspot.SfxPitch);
            await Say("By hand, this one. The keys I've carried here are on the ledge — drag one up and put it in properly. The brass pictures say which bed is whose.", 4.2f, 23);
            GameState.Instance?.Toast("Drag a key from Mara's ledge (left) into a keyhole, and let go. Click a placed key to lift it back out.");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    /// <summary>The turn has finished on screen; now the placement is real.</summary>
    private async Task FinishInsertAsync(TrayKey key, string holeId)
    {
        bool finishDoor = false;
        bool judgeNow = false;
        try
        {
            var gs = GameState.Instance;
            var hotspot = GetHotspot(holeId);
            if (gs == null || hotspot == null)
                return;

            bool home = key.KeyholeId == holeId;
            RecordExamine(hotspot);
            gs.SetFlag(HoleFlag(holeId, key.ShortId), save: false);
            gs.SetFlag(key.InsertedFlag);
            Hotspots.SpawnRipple(hotspot.Center, new Color(1f, 0.88f, 0.55f));
            RefreshTray();
            UpdateStatusLabel();
            QueueRedraw();

            await Say(home ? InsertLine(holeId) : WrongBedLine(key.ShortId), 4.5f, 23);
            if (!GodotObject.IsInstanceValid(this))
                return;

            if (PlacedCount() >= 3)
            {
                if (!SentenceIsComplete())
                {
                    await Say("Three keys, all turned partway — and the door still holds. Not out of cruelty. The hall behind me hasn't finished what it was saying.", 5f, 23);
                }
                else if (AllPlacedRight())
                {
                    finishDoor = true;
                }
                else
                {
                    judgeNow = true;
                }
            }
            else
            {
                gs.Toast($"A key turns ({PlacedCount()}/3 in). It stays where Mara put it — click it to lift it out.");
            }

            if (judgeNow && GodotObject.IsInstanceValid(this))
                await SayMixedUpAsync();
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }

        if (finishDoor && GodotObject.IsInstanceValid(this))
            await CompleteDoorAsync();
    }

    private static string InsertLine(string id) => id switch
    {
        "LeftKeyhole" => "The copper bridge key goes in under the bridge. It turns with a small, wet, river-mud click.",
        "CenterKeyhole" => "The little door key goes in under the little door. For one absurd second, Mara half expects its tiny knob to turn first.",
        "RightKeyhole" => "The circle key goes in under the circle. coldest last. It turns all the way round before it catches.",
        _ => "The key turns.",
    };

    private static string WrongBedLine(string shortId) => shortId switch
    {
        "bridge" => "The copper key goes in… and turns. Partway. It stands a little crooked, like a guest in the wrong chair.",
        "door" => "The little door key goes in… and turns. Partway. Its tiny knob points the way knobs don't.",
        "circle" => "The circle key goes in… and turns. Partway. A ring looks wrong at any angle, and somehow this is wronger.",
        _ => "The key goes in, and turns partway.",
    };

    private async Task MissingKeyAsync(Hotspot hotspot)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            RecordExamine(hotspot);
            AudioManager.Play(Sfx.LockRattle, -8f, 0.82f);
            AudioManager.Play(Sfx.Denied, -11f, 0.9f);
            await SaySequence(MissingLines(hotspot.Id));
            GameState.Instance?.Toast("That key is not in Mara's bag yet (B). Press H and the house will say where its keeping-place was.");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    private static string[] MissingLines(string id) => id switch
    {
        "LeftKeyhole" =>
        [
            "Empty. The bridge keyhole smells faintly of river mud and green copper.",
            "Whatever belongs here was kept with a morning, not with a memory trick. A copper key, in a kitchen drawer, beside a brass one — if I left it lying there, it's lying there still.",
        ],
        "CenterKeyhole" =>
        [
            "Empty. Behind the little-door keyhole: photograph paper, old tape, somebody breathing in a quiet room.",
            "The key for this one is taped behind a photograph, under a father's pencil: for when you remember it right. The sitting room keeps it — if I only took the picture and left its backing unexamined.",
        ],
        "RightKeyhole" =>
        [
            "Empty, and cold all the way through. Preserves. Glass. A name folded small enough to keep.",
            "The circle wants the key that was kept the way everything in that room was kept: sealed in the last jar, under the name tape. If I took the tape and never reached deeper, it's still down there in the dark glass.",
        ],
        _ => ["Empty."],
    };

    private async Task ExamineDoorPartAsync(Hotspot hotspot)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            bool first = RecordExamine(hotspot);
            AudioManager.PlayVaried(hotspot.SfxId, -6f, hotspot.SfxPitch, 0.04f);
            if (first)
            {
                await SaySequence(FirstLines(hotspot.Id));
            }
            else
            {
                GameState.Instance?.RegisterRepeatExaminePenalty();
                await Say(RepeatLine(hotspot.Id), 3.5f, 23);
            }
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    private string[] FirstLines(string id)
    {
        if (IsKeyhole(id) && KeyInHole(id) is string standing)
        {
            bool home = HomeKeyholeFor(standing) == id;
            return standing switch
            {
                "bridge" => [home
                    ? "The copper bridge key stands in its hole, green at the edges, turned as far as one lock alone will let it."
                    : "The copper bridge key stands here, in somebody else's bed, a little crooked from its partway turn."],
                "door" => [home
                    ? "The little door key stands in the middle hole. Its tiny knob has not turned. Mara checked. Twice."
                    : "The little door key stands here, in somebody else's bed. Its tiny knob points the way knobs don't."],
                "circle" => [home
                    ? "The circle key stands in the final hole, its ring catching what light the hall allows."
                    : "The circle key stands here, in somebody else's bed. A ring looks wrong at any angle, and somehow this is wronger."],
                _ => ["The key stands where Mara put it."],
            };
        }

        return id switch
        {
            "BridgePlate" =>
            [
                "A bridge stamped in brass, water moving underneath whether anyone crosses or not.",
                "Directly below it waits a keyhole, polished bright by thumbs. Not a symbol to solve. A shape to match.",
            ],
            "LeftKeyhole" =>
            [
                "The keyhole beneath the bridge. Its brass is polished brighter than the others.",
                "From inside, the faintest smell of river mud.",
            ],
            "DoorPlate" =>
            [
                "A little door set into the door, complete with its own impossible knob.",
                "Below the little door waits the middle keyhole.",
            ],
            "CenterKeyhole" =>
            [
                "The middle keyhole sits exactly beneath the little door. From inside comes the faintest smell of rain on a bedroom curtain.",
                "Something on the other side is holding very still, listening back.",
            ],
            "CirclePlate" =>
            [
                "An empty brass circle. No face, no landscape, no way through except around again.",
                "One keyhole remains beneath the circle.",
            ],
            "RightKeyhole" =>
            [
                "The final keyhole, under the circle. Something behind it shifts — not a lock giving way, but a room deciding which year to be.",
                "It is the coldest of the three.",
            ],
            _ => ["cold brass, patient under my fingers."],
        };
    }

    private static string RepeatLine(string id) => id switch
    {
        "BridgePlate" => "Water underneath, whether anyone crosses or not.",
        "LeftKeyhole" => "Brass, polished by thumbs.",
        "DoorPlate" => "A little door, complete with its own impossible knob.",
        "CenterKeyhole" => "Rain on a bedroom curtain, from inside.",
        "CirclePlate" => "No way through except around again.",
        "RightKeyhole" => "The coldest of the three.",
        _ => "cold brass.",
    };

    private async Task WrongVerbAsync(MemoryVerb verb)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -9f, 0.92f);
            string line = verb switch
            {
                MemoryVerb.Echo => "Echo can make the brass sound. It cannot put a key in Mara's hand, and this door has stopped pretending otherwise.",
                MemoryVerb.Anchor => "You could hold this door still forever and it would only become a better-kept wall. It wants metal, carried here.",
                MemoryVerb.Trace => "There is no chain of causes to trace in the brass — only three keeping-places behind her and the keys they held.",
                MemoryVerb.Release => "There is nothing whole enough here to release.",
                _ => "That way of remembering does not fit a keyhole.",
            };
            await Say(line, 4f, 23);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private async Task LockedDoorAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.LockRattle, -7f, 0.82f);
            if (PlacedCount() >= 3 && SentenceIsComplete() && !AllPlacedRight())
            {
                await SayMixedUpAsync();
            }
            else if (AllKeysInserted())
            {
                await SaySequence(
                    "All three keys turn partway and stop, together, like they are waiting for a word.",
                    "The hall behind me hasn't finished its sentence. Metal alone won't bully this door.");
            }
            else if (!SentenceIsComplete())
            {
                await SaySequence(
                    "The door gives back three separate rattles and nothing else.",
                    $"It wants the sentence whole — and {3 - PlacedCount()} more of its keys actually in their holes. Patterns won't substitute for either.");
            }
            else
            {
                await SaySequence(
                    "The sentence is whole. The door knows it. It still gives back only the rattle of empty keyholes.",
                    "Three ordinary keys. In my bag or still in their keeping-places — that is the whole of what stands here now.");
            }
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private async Task CompleteDoorAsync()
    {
        if (Busy || _opened || !AllPlacedRight() || !SentenceIsComplete())
            return;
        Busy = true;
        try
        {
            _opened = true;
            GameState.Instance?.SetFlag(OpenedFlag);
            MemoryVerbSystem.Cancel(silent: true);

            AudioManager.Play(Sfx.DoorUnlock, -5f);
            AudioManager.Play(Sfx.PuzzleSolve, -8f, 1.06f);
            UpdateStatusLabel();
            QueueRedraw();

            await SaySequence(
                "Bridge. Little door. Circle. Three keys I carried here in my own bag, out of three rooms that made me earn them.",
                "At the last turn all three locks go together — no echo, no trick of memory. Just metal remembering what hands are for. Beyond them, a bedroom cannot decide which year it is.");

            GameState.Instance?.Toast("All three keys have turned. Click the door to open it.");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private async Task ProceedThroughDoorAsync()
    {
        if (Busy)
            return;
        Busy = true;
        AudioManager.Play(Sfx.DoorCreak, -6f, 0.98f);
        await WaitSeconds(0.2f);
        SceneManager.ChangeScene(Room5ScenePath);
        Busy = false;
    }

    private void UpdateStatusLabel()
    {
        if (_statusLabel == null)
            return;

        if (_opened)
        {
            _statusLabel.Text = "all three keys have turned — click the door to open it";
            return;
        }

        int inserted = PlacedCount();
        if (inserted >= 3)
        {
            _statusLabel.Text = !SentenceIsComplete()
                ? "three keys in — the sentence is not whole yet"
                : AllPlacedRight()
                    ? "three keys in — click the door"
                    : "three keys in — and the door hasn't answered. Click a key to lift it out.";
            return;
        }

        int carried = 0;
        var gs = GameState.Instance;
        if (gs != null)
        {
            if (gs.HasFlag(BridgeKeyFlag)) carried++;
            if (gs.HasFlag(DoorKeyFlag)) carried++;
            if (gs.HasFlag(CircleKeyFlag)) carried++;
        }
        _statusLabel.Text = carried > inserted
            ? $"keys in: {inserted} of 3 — drag a carried key into a keyhole"
            : $"keys in: {inserted} of 3 — the rest are still in their keeping-places";
    }
}

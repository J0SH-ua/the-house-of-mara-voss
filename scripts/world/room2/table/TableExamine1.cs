using Godot;

/// <summary>
/// The sitting room's side table — the way into the drawer close-up,
/// and, after the crumbling room has been remembered right, the
/// place the evening sets its secret down.
///
/// When the investigation is solved and the little door key has not
/// yet been taken, the key simply lies here, on the table between
/// the chairs, drawn in code over the painting (the tiles never
/// knew about it). One click on the key itself takes it — its own
/// held moment, its own place in the bag. Everything else about the
/// table is unchanged: it opens the drawer once the room has been
/// fully faced, and a sealed room still lets Mara back for an
/// untaken key.
/// </summary>
public partial class TableExamine1 : ExamineHandler
{
    private const string SolvedFlag = "difference_room2_solved";
    private const string DoorKeyFlag = "item_hall_key_door";
    private const string DoorKeyIconPath = "res://assets/images/core/items/door_key.png";

    private Sprite2D _keySprite;
    private bool _takingKey;

    private bool KeyWaiting =>
        (GameState.Instance?.HasFlag(SolvedFlag) ?? false) &&
        !(GameState.Instance?.HasFlag(DoorKeyFlag) ?? false);

    public override void _Ready()
    {
        if (KeyWaiting)
            LayKeyOnTable();
    }

    private void LayKeyOnTable()
    {
        var texture = GD.Load<Texture2D>(DoorKeyIconPath);
        if (texture == null)
            return;

        Vector2 anchor = MapToLocal(GetUsedRect().GetCenter());
        _keySprite = new Sprite2D
        {
            Name = "LittleDoorKeyProp",
            Texture = texture,
            Position = anchor,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ZIndex = 6,
        };
        // This layer (and its parent) are scaled up; aim for a key
        // about 84 px tall on screen whatever the nesting says.
        Vector2 parentScale = GlobalTransform.Scale;
        float s = 84f / 512f / Mathf.Max(0.01f, Mathf.Abs(parentScale.X));
        _keySprite.Scale = Vector2.One * s;
        AddChild(_keySprite);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_keySprite == null)
            return;
        if (!KeyWaiting)
        {
            _keySprite.QueueFree();
            _keySprite = null;
            return;
        }
        float pulse = Settings.ReduceMotionOrDefault()
            ? 1f
            : 0.86f + 0.14f * (float)System.Math.Sin(Time.GetTicksMsec() / 240.0);
        _keySprite.Modulate = new Color(pulse, pulse, pulse);
    }

    private bool ClickHitsKey(Vector2 globalMouse)
    {
        if (_keySprite == null || !KeyWaiting)
            return false;
        return new Rect2(_keySprite.GlobalPosition - new Vector2(48, 48),
            new Vector2(96, 96)).HasPoint(globalMouse);
    }

    public override void _Input(InputEvent @event)
    {
        if (Dialogue.IsInputBlocked)
            return;

        if (!(@event is InputEventMouseButton mouseEvent) || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
            return;

        Vector2 globalMouse = GetGlobalMousePosition();

        // The key first: it is small, it is the point, and the table
        // underneath it can wait its turn like everything else here.
        if (!_takingKey && !Dialogue.IsShowing && ClickHitsKey(globalMouse))
        {
            GetTree().Root.SetInputAsHandled();
            _ = TakeDoorKeyAsync();
            return;
        }

        Vector2 localMouse = ToLocal(globalMouse);
        Vector2I cell = LocalToMap(localMouse);

        bool hasTile = GetCellSourceId(cell) != -1;

        // A sealed sitting room turns Mara away — unless the little
        // door key still sleeps unclaimed somewhere in it.
        if (hasTile && GameState.Instance?.IsRoomSealed("Room2") == true &&
            GameState.Instance?.RoomHasUnclaimedPracticalItem("Room2") != true)
        {
            GameState.Instance.NotifySealedInteraction();
            GetTree().Root.SetInputAsHandled();
            return;
        }

        if (hasTile)
        {
            RoomExamineTracker.OnExamineClicked(this);
            OnExamineClicked();
            GetTree().Root.SetInputAsHandled();
        }
    }

    private async System.Threading.Tasks.Task TakeDoorKeyAsync()
    {
        var gs = GameState.Instance;
        if (_takingKey || gs == null || gs.HasFlag(DoorKeyFlag))
            return;
        _takingKey = true;
        try
        {
            AudioManager.Play(Sfx.KeyType, -8f, 0.95f);
            await Dialogue.ShowText(this,
                "A key. Not a metaphor wearing key-coloured paint. Iron — and its bow is a little door, down to a knob too small for any hand but memory's.",
                5.5f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
            if (!GodotObject.IsInstanceValid(this))
                return;
            await Dialogue.ShowText(this,
                "He taped it behind an evening and trusted I'd remember the room right. …I did, Dad. Just barely, with the ceiling coming down. But I did.",
                5.5f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
            if (!GodotObject.IsInstanceValid(this))
                return;

            gs.SetFlag(DoorKeyFlag, save: false);
            gs.UnlockClue("hall_key_door");
            gs.Toast("Little door key — kept in Mara's bag (B). Somewhere ahead is a door with a little door stamped above its middle keyhole.");
            gs.SaveGame("little door key taken");
            if (_keySprite != null)
            {
                _keySprite.QueueFree();
                _keySprite = null;
            }
        }
        finally
        {
            _takingKey = false;
        }
    }

    protected override async void OnExamineClicked()
    {
        // Re-entering after the shard is taken stays allowed: Room 2's
        // arrows are decorative, so with step-back navigation this table
        // (via the photograph) is the only way onward to Room 3.

        string roomName = "Room2";
        var parent = GetParent();
        bool isFullyExamined = RoomExamineTracker.HasRoomBeenFullyExamined(roomName);
        bool hasRightParent = parent != null && parent.Name.ToString().EndsWith("Final");

        if (isFullyExamined && hasRightParent)
        {
            Logger.Debug("[Table Unlocked] Changing to puzzle scene!");
            Logger.Info("Current Corruption Count: " + Globals.Instance.CORRUPTION_COUNT);
            AudioManager.Play(Sfx.DoorUnlock, -5f);
            SceneManager.ChangeScene("res://scenes/world/room_2_puzzle.tscn");
            return;
        }

        RoomExamineTracker.GetRoomProgress(roomName, out int done, out int total, out _);
        AudioManager.Play(Sfx.LockRattle, -7f);
        await Dialogue.ShowText(this,
            $"The table keeps its secret. The room hasn't finished being looked at. ({done}/{total})",
            3.5f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 26);
    }
}

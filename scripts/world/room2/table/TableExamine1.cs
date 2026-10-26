using Godot;

/// <summary>
/// The sitting room's side table — the way into the drawer close-up.
/// (The little door key the crumbling room gives up waits on this
/// table where the letter lived; see Room2.LayDoorKeyOnTable.)
/// </summary>
public partial class TableExamine1 : ExamineHandler
{
    public override void _Input(InputEvent @event)
    {
        if (Dialogue.IsInputBlocked)
            return;

        if (!(@event is InputEventMouseButton mouseEvent) || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
            return;

        Vector2 globalMouse = GetGlobalMousePosition();

        // The waiting little door key lies on this table. Its click
        // is the pickup's, never the drawer shortcut's: let it pass
        // untouched instead of claiming the table tile beneath it.
        if (Room2.ShouldYieldToDoorKey(globalMouse))
            return;

        Vector2 localMouse = ToLocal(globalMouse);
        Vector2I cell = LocalToMap(localMouse);

        bool hasTile = GetCellSourceId(cell) != -1;

        // A sealed sitting room turns Mara away — unless the little
        // door key still sleeps unclaimed beside the globe.
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

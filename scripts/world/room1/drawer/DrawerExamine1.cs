using Godot;

public partial class DrawerExamine1 : ExamineHandler
{
	public override void _Input(InputEvent @event)
	{
		if (Dialogue.IsInputBlocked)
			return;

		if (!(@event is InputEventMouseButton mouseEvent) || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		Vector2 globalMouse = GetGlobalMousePosition();
		Vector2 localMouse = ToLocal(globalMouse);
		Vector2I cell = LocalToMap(localMouse);

		bool hasTile = GetCellSourceId(cell) != -1;

		if (hasTile && GameState.Instance?.IsRoomSealed("Room1") == true)
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
		// Re-entering after the shard is taken stays allowed: with step-back
		// navigation the drawer remains a way through, and collecting the
		// brass key again is idempotent (short line, onward to Room 2).

		string roomName = "Room1";
		var parent = GetParent();
		bool isFullyExamined = RoomExamineTracker.HasRoomBeenFullyExamined(roomName);
		bool hasRightParent = parent != null && parent.Name.ToString().EndsWith("Final");

		if (isFullyExamined && hasRightParent)
		{
			Logger.Debug("[Drawer Unlocked] Changing to puzzle scene!");
			Logger.Info("Current Corruption Count: " + Globals.Instance.CORRUPTION_COUNT);
			AudioManager.Play(Sfx.DoorUnlock, -5f);
			SceneManager.ChangeScene("res://scenes/world/room_1_puzzle.tscn");
			return;
		}

		// Locked used to mean *total silence*. Tell the player why, and how close they are.
		RoomExamineTracker.GetRoomProgress(roomName, out int done, out int total, out _);
		AudioManager.Play(Sfx.LockRattle, -7f);
		await Dialogue.ShowText(this,
			$"It won't move. Not the lock — the room. Something here hasn't been faced yet. ({done}/{total})",
			3.5f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 26);
	}
}
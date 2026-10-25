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

		// A sealed kitchen turns Mara away — unless the drawer still
		// keeps something practical she walked past, or the blackout has
		// sent her back here for the cabinet she swears was never there.
		var gs = GameState.Instance;
		bool drawerStillKeeping = gs != null &&
			(gs.RoomHasUnclaimedPracticalItem("Room1") ||
			 (gs.HasFlag("blackout_remembered_cabinet") && !gs.HasFlag("item_flashlight")));
		if (hasTile && gs?.IsRoomSealed("Room1") == true && !drawerStillKeeping)
		{
			gs.NotifySealedInteraction();
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

		// In the blackout, this counter is not about the drawer at all.
		// Her father's route — cabinet, flashlight — starts above it,
		// at a cabinet Mara would swear this kitchen never had.
		var gsNow = GameState.Instance;
		if (gsNow != null && gsNow.HasFlag("blackout_remembered_cabinet") &&
			!gsNow.HasFlag("item_flashlight"))
		{
			AudioManager.Play(Sfx.Examine, -8f, 0.9f);
			if (!gsNow.HasFlag("blackout_cabinet_found"))
			{
				await Dialogue.ShowText(this,
					"My hand knows this counter in the dark. Drawer. Counter. And above it — no. There isn't anything above it. There wasn't this morning. I examined every inch of this kitchen.",
					5.5f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
				if (!GodotObject.IsInstanceValid(this))
					return;
				await Dialogue.ShowText(this,
					"My fingers find a cabinet door anyway. Old wood. A knob worn smooth. It has either always been here, or this house keeps what it needs where I can't look directly at it. ...I'm not going to think about that. Top shelf. Like Dad.",
					6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
				if (!GodotObject.IsInstanceValid(this))
					return;
				gsNow.SetFlag("blackout_cabinet_found", save: false);
			}
			AudioManager.Play(Sfx.DrawerOpen, -7f, 0.8f);
			SceneManager.ChangeScene("res://scenes/world/kitchen_cabinet.tscn");
			return;
		}

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
using Godot;

/// <summary>
/// The photograph in the sitting-room drawer — shard 1, and behind it,
/// a second thing entirely.
///
/// The photograph is faced and taken — and then it turns over in her
/// hands. On its back is a second photograph: the sitting room itself,
/// exactly as it was, kept in her bag from then on to study whenever
/// she likes (B, then "Look at the photograph"). Under the print, in
/// her father's pencil, is FOR WHEN YOU REMEMBER IT RIGHT. Something
/// small and metal is taped behind this picture; something in the
/// room it shows has been edited for twelve years to keep it hidden.
///
/// The wind does the rest. It takes the fire, the light, and Mara —
/// into the room from the photograph, with something off, crumbling
/// around a memory held wrong. Name what moved before it breaks, and
/// the steadied room sets the little door key down beside the globe
/// in the real sitting room, to be picked up by hand.
/// </summary>
public partial class Photo1Reward : Area2D
{
	// The investigation itself is entered from the sitting room
	// (Room2's wind beat), never straight from this drawer.
	private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
	private const string FragmentText =
		"She used to fall asleep in that chair and think it was the best thing in the world — " +
		"to fall asleep to the sound of someone else in the room. Someone else breathing. " +
		"Someone turning a page.";
	private string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";

	private bool _isHovering = false;
	private bool _collecting;

	public override void _Ready()
	{
		// Taken is taken: once the photograph rides in Mara's bag,
		// the drawer keeps nothing where it lay. From then on the
		// print is faced from the bag itself (B — "Look at the
		// photograph"), in the sitting room it remembers — the
		// drawer is never the way back to it.
		bool inBag = (GameState.Instance?.IsShardRecovered(1) ?? false) ||
			(Globals.Instance != null &&
				(Globals.Instance.PHOTO1_COLLECTED || Globals.SHARDS_COLLECTED[1]));
		if (inBag)
		{
			Visible = false;
			InputPickable = false;
			SetProcess(false);
			return;
		}

		InputPickable = true;
		SetProcess(true);
	}

	public override void _ExitTree()
	{
		if (_isHovering)
		{
			CursorHelper.EndHover(this);
			_isHovering = false;
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!Visible || _collecting || Dialogue.IsInputBlocked)
			return;

		if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		var shapeOwner = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (shapeOwner == null || shapeOwner.Shape is not RectangleShape2D rectShape)
			return;

		var globalMousePos = GetGlobalMousePosition();
		var localMousePos = shapeOwner.ToLocal(globalMousePos);

		var size = rectShape.Size;
		var rect = new Rect2(-size / 2, size);

		if (rect.HasPoint(localMousePos))
		{
			GetTree().Root.SetInputAsHandled();
			var gs = GameState.Instance;
			// A sealed sitting room goes quiet — unless the little door
			// key still sleeps behind this photograph. Untaken things
			// stay takeable, however finished the memory around them.
			if (gs?.IsRoomSealed("Room2") == true &&
				gs?.RoomHasUnclaimedPracticalItem("Room2") != true)
			{
				gs.NotifySealedInteraction();
				return;
			}

			if (gs == null || !gs.IsShardRecovered(1))
				CollectAndChangeScene();
			else if (!gs.HasFlag("difference_room2_solved"))
				EnterDifferenceInvestigation();
			else
				CollectAndChangeScene();
		}
	}

	public override void _Process(double delta)
	{
		if (!Visible)
		{
			if (_isHovering)
			{
				CursorHelper.EndHover(this);
				_isHovering = false;
			}

			return;
		}

		var shapeOwner = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (shapeOwner?.Shape is not RectangleShape2D rectShape)
		{
			if (_isHovering)
			{
				CursorHelper.EndHover(this);
				_isHovering = false;
			}

			return;
		}

		var globalMousePos = GetGlobalMousePosition();
		var localMousePos = shapeOwner.ToLocal(globalMousePos);
		var size = rectShape.Size;
		var rect = new Rect2(-size / 2, size);
		bool isHovering = rect.HasPoint(localMousePos);

		if (isHovering)
		{
			if (!_isHovering)
			{
				CursorHelper.BeginHover(this, PointerCursorPath, 32, 32);
				_isHovering = true;
				AudioManager.Play(Sfx.UiHover, -12f);
			}
		}
		else if (_isHovering)
		{
			CursorHelper.EndHover(this);
			_isHovering = false;
		}
	}

	private async void EnterDifferenceInvestigation()
	{
		_collecting = true;
		AudioManager.Play(Sfx.PhotoPickup, -7f, 0.94f);
		await Dialogue.ShowText(this, "FOR WHEN YOU REMEMBER IT RIGHT. Under the pencil, one corner of the backing lifts — something flat and metal, taped down where only an honest memory would ever think to look.", 6.5f, GlobalPosition.X, GlobalPosition.Y, FontPath, 22);
		if (!GodotObject.IsInstanceValid(this))
			return;
		await Dialogue.ShowText(this, "The room as it was is in my bag. The room as it isn't is waiting. All right, house. Blow.", 5.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
		if (!GodotObject.IsInstanceValid(this))
			return;
		await WindTakesTheRoomAsync();
	}

	/// <summary>Every version of this moment ends in the real sitting
	/// room. The wind itself belongs there — standing in front of the
	/// actual fire while it goes out — so the drawer only raises the
	/// weather's flag and puts Mara back in the room to meet it. (The
	/// flag is unsaved on purpose: quit mid-beat and clicking the
	/// photograph simply raises it again.)</summary>
	private async System.Threading.Tasks.Task WindTakesTheRoomAsync()
	{
		await Dialogue.ShowText(this, "Something about holding this makes the whole room feel watched. The room itself, then. Let me look at the room itself.", 5.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
		if (!GodotObject.IsInstanceValid(this))
			return;
		GameState.Instance?.SetFlag("difference_wind_pending", save: false);
		SceneManager.ChangeScene("res://scenes/world/room_2.tscn");
	}

	/// <summary>Hold the flipped photograph up to the light and study
	/// it — zoom, drag, take as long as memory needs. The room waits
	/// (input-blocked) until she puts it away.</summary>
	private async System.Threading.Tasks.Task LookAtPhotographAsync()
	{
		var ui = GameUI.Instance;
		if (ui == null)
			return;

		bool putAway = false;
		Dialogue.BeginInputBlock();
		try
		{
			ui.ShowPhotograph(() => putAway = true, flipIntro: true);
			while (!putAway && GodotObject.IsInstanceValid(this) && IsInsideTree())
				await ToSignal(GetTree().CreateTimer(0.1, true), SceneTreeTimer.SignalName.Timeout);
		}
		finally
		{
			Dialogue.EndInputBlock();
		}
	}

	private async void CollectAndChangeScene()
	{
		_collecting = true;

		if (Globals.Instance != null)
			Globals.Instance.PHOTO1_COLLECTED = true;

		AudioManager.Play(Sfx.PhotoPickup, -5f);
		AudioManager.Play(Sfx.CameraShutter, -10f);
		bool firstTime;
		if (GameState.Instance != null)
		{
			firstTime = GameState.Instance.CollectShard(1, FragmentText);
		}
		else
		{
			firstTime = !Globals.SHARDS_COLLECTED[1];
			Globals.SHARDS_COLLECTED[1] = true;
		}

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		if (firstTime)
		{
			await Dialogue.ShowText(this, "\"She used to fall asleep in that chair and think it was the best thing in the world — to fall asleep to the sound of someone else in the room.\"", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 18);
			await Dialogue.ShowText(this, "\"Not silence. Not the sound of herself.\"", 4.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 30);
			await Dialogue.ShowText(this, "\"Someone else breathing. Someone turning a page.\"", 4.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 30);
			if (!GodotObject.IsInstanceValid(this))
				return;

			// Something on the back. There is always something on the back.
			AudioManager.Play(Sfx.PageTurn, -8f, 0.85f);
			await Dialogue.ShowText(this, "Wait. The back of it feels wrong — stiff. Printed. I turn it over…", 4.5f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
			if (!GodotObject.IsInstanceValid(this))
				return;
			await Dialogue.ShowText(this, "It's another photograph. A room. Two chairs, a fire, a little table between them. This room. This room before the house started… helping. And under the print, in Dad's pencil: FOR WHEN YOU REMEMBER IT RIGHT.", 6.5f, GlobalPosition.X, GlobalPosition.Y, FontPath, 22);
			if (!GodotObject.IsInstanceValid(this))
				return;

			await LookAtPhotographAsync();
			if (!GodotObject.IsInstanceValid(this))
				return;

			await Dialogue.ShowText(this, "Under one corner of the backing: tape. Something flat and metal underneath. He hid it the way he hid everything he trusted me with — in the keeping, not the locking.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 22);
			if (!GodotObject.IsInstanceValid(this))
				return;

			await WindTakesTheRoomAsync();
			return;
		}

		// Passing through again on a later visit — keep it brief.
		await Dialogue.ShowText(this, "\"Someone else breathing. Someone turning a page.\"", 2.5f, GlobalPosition.X, GlobalPosition.Y, FontPath, 26);

		// Having the fragment opens the way on; taking it is the
		// Leave button's work now (top right of the screen) — and the
		// first-departure walk waits there, by seen-flag, however
		// long she looks around first.
		_collecting = false;
		if (GameState.Instance != null &&
			!GameState.Instance.HasFlag("journey_seen_Room2ToRoom3"))
			GameState.Instance.Toast("The way on is open. Leave (top right) when you're ready.");
	}
}

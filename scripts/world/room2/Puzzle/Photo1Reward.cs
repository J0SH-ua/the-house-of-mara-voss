using Godot;

/// <summary>
/// The photograph in the sitting-room drawer — shard 1.
/// Before Mara may take it for the first time, the house makes her earn
/// what lies behind it honestly: a five-second look at the sitting
/// room, followed by a search for what memory moved. That investigation
/// yields the little door key for Room 4. Collection itself remains
/// idempotent via GameState (journal fragment, sting, autosave).
/// </summary>
public partial class Photo1Reward : Area2D
{
	private const string NextScenePath = "res://scenes/world/room_3.tscn";
	private const string JourneyScenePath = "res://scenes/world/journey_to_room_3.tscn";
	private const string DifferenceScenePath = "res://scenes/world/difference_investigation.tscn";
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
			if (GameState.Instance?.IsRoomSealed("Room2") == true)
			{
				GameState.Instance.NotifySealedInteraction();
				return;
			}
			var gs = GameState.Instance;
			bool needsDifference = gs != null &&
				!gs.HasFlag("difference_room2_solved") &&
				!gs.IsShardRecovered(1);
			if (needsDifference)
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
		await Dialogue.ShowText(this, "It isn't fixed down. But something small has slid behind it, out of reach — and under my fingers the photograph does what this house always does. It asks whether I actually remember the room I claim to remember.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 22);
		if (!GodotObject.IsInstanceValid(this))
			return;
		await Dialogue.ShowText(this, "Five seconds, then. I can hold a room for five seconds. I have held worse for twelve years.", 5.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
		if (!GodotObject.IsInstanceValid(this))
			return;
		SceneManager.ChangeScene(DifferenceScenePath);
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
		}
		else
		{
			// Passing through again on a later visit — keep it brief.
			await Dialogue.ShowText(this, "\"Someone else breathing. Someone turning a page.\"", 2.5f, GlobalPosition.X, GlobalPosition.Y, FontPath, 26);
		}


		SceneManager.ChangeScene(firstTime ? JourneyScenePath : NextScenePath);
	}
}

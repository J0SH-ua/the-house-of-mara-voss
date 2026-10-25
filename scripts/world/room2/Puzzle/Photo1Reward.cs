using Godot;

/// <summary>
/// The photograph in the sitting-room drawer — shard 1, and behind it,
/// a second thing entirely.
///
/// The photograph itself can simply be faced and taken: the fragment
/// asks nothing practical of Mara. But on its backing, in her father's
/// pencil, is FOR WHEN YOU REMEMBER IT RIGHT — he taped a little door
/// key behind the sitting room in this picture, the way he hid anything
/// he wanted only an honest memory to find. The house has been gently
/// editing the room the photograph shows ever since. Looking at the
/// photograph again after taking it sends Mara into that room as it was
/// kept: five seconds to hold it, then a search for what memory moved.
/// Only a room remembered right gives up what was taped behind it.
///
/// She may walk away with just the fragment. The key stays behind the
/// picture — findable later, the same way, whenever she comes back.
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
		await Dialogue.ShowText(this, "I turn it over. On the backing, in pencil gone soft at the edges — Dad's handwriting. FOR WHEN YOU REMEMBER IT RIGHT. Under that, one corner of the backing lifts. Something flat and metal is taped behind this photograph.", 6.5f, GlobalPosition.X, GlobalPosition.Y, FontPath, 22);
		if (!GodotObject.IsInstanceValid(this))
			return;
		await Dialogue.ShowText(this, "Of course it won't just peel. This house has been editing the room in this picture for twelve years — gentler, tidier, later than it was. He knew. That's why the condition. Remember it right, and what he hid stays findable. Remember it kind, and it rots behind the glass with everything else.", 6.5f, GlobalPosition.X, GlobalPosition.Y, FontPath, 22);
		if (!GodotObject.IsInstanceValid(this))
			return;
		await Dialogue.ShowText(this, "Five seconds, then, Dad. I can hold a room for five seconds. I have held worse for twelve years.", 5.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
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

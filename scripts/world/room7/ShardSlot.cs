using Godot;

/// <summary>
/// One hollow frame in Room 7. Placement rules live in
/// GameState.TryPlaceShard (matching shard preferred, any carried shard
/// accepted, everything recorded); this slot handles hover, feedback and
/// the completion moment when all six fragments are home.
/// </summary>
public partial class ShardSlot : Area2D
{
	[Export] public int SlotIndex = 0;

	private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
	private const string LockedCursorPath = "res://assets/images/core/cursor_locked_magnifying_glass.png";
	private string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";

	private Sprite2D _sprite;
	private bool _isHovering;
	private bool _completing;

	public override void _Ready()
	{
		InputPickable = true;
		_sprite = GetNode<Sprite2D>("Sprite2D");
		UpdateVisuals();
		MouseEntered += OnMouseEntered;
		MouseExited += OnMouseExited;
	}

	public override void _ExitTree()
	{
		if (_isHovering)
		{
			CursorHelper.EndHover(this);
			_isHovering = false;
		}
	}

	private void OnMouseEntered()
	{
		bool placed = Globals.Instance?.SHARDS_PLACED[SlotIndex] ?? false;
		string cursor = placed ? LockedCursorPath : PointerCursorPath;
		CursorHelper.BeginHover(this, cursor, 32, 32);
		_isHovering = true;
	}

	private void OnMouseExited()
	{
		if (_isHovering)
		{
			CursorHelper.EndHover(this);
			_isHovering = false;
		}
	}

	private void UpdateVisuals()
	{
		_sprite.Visible = Globals.Instance != null && Globals.Instance.SHARDS_PLACED[SlotIndex];
	}

	public override void _InputEvent(Viewport viewport, InputEvent @event, int shapeIdx)
	{
		if (Dialogue.IsInputBlocked || _completing)
			return;

		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			GetTree().Root.SetInputAsHandled();
			TryPlaceShard();
		}
	}

	private async void TryPlaceShard()
	{
		if (GameState.Instance == null)
			return;

		switch (GameState.Instance.TryPlaceShard(SlotIndex))
		{
			case ShardPlaceResult.Placed:
				AudioManager.Play(Sfx.ShardPlace, -4f);
				Hotspots.SpawnRipple(GlobalPosition);
				UpdateVisuals();
				GameState.Instance.Toast($"Returned. ({GameState.Instance.CountPlaced()}/{GameState.MaxShards})");
				break;

			case ShardPlaceResult.AlreadyPlaced:
				AudioManager.Play(Sfx.UiHover, -8f, 0.8f);
				break;

			case ShardPlaceResult.NoShardCarried:
				AudioManager.Play(Sfx.Denied, -7f);
				GameState.Instance.Toast("Your hands are empty. The house still holds this piece.");
				break;

			case ShardPlaceResult.AllPlaced:
				_completing = true;
				AudioManager.Play(Sfx.ShardPlace, -4f);
				AudioManager.Play(Sfx.RoomComplete, -3f);
				Hotspots.SpawnRipple(GlobalPosition);
				UpdateVisuals();
				GameState.Instance.MarkRoomCompleted("Room7");
				await Dialogue.ShowText(this,
					"Six fragments. One house. One morning, remembered as it was.",
					5.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 28);
				GameState.Instance.SaveGame("all shards placed");
				AudioManager.Instance?.StopAmbience();
				GetTree().ChangeSceneToFile("res://scenes/ui/main_menu.tscn");
				break;
		}
	}
}

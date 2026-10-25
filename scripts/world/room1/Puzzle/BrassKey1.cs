using Godot;

/// <summary>
/// What the kitchen drawer was keeping, in the close-up, each thing in
/// its own place and taken by its own hand:
///
///   • the brass key — shard 0, the morning itself;
///   • beside it, a smaller copper key gone green at the edges, its bow
///     cut like a little bridge — the first of the hallway door's three
///     ordinary keys.
///
/// (Mum's bent hairpin is not here. It sleeps where it always slept:
/// the little drawer of the nightstand in the very first room.)
///
/// The brass key alone is enough to go on. The copper one is simply
/// there, visible, takeable or leavable — and if Mara leaves it, the
/// house will ask once, on her way out, whether she meant to. Whatever
/// she skips stays in the drawer, and a sealed kitchen still lets her
/// come back for it: finished rooms go quiet, but untaken things stay
/// takeable. Collection is idempotent and goes through GameState so
/// the journal, HUD, held moments and autosave happen exactly once.
/// </summary>
public partial class BrassKey1 : Area2D
{
	private const string FragmentText =
		"Every morning started here. Every morning she was already waiting, already loud, " +
		"already herself. You could set your clock by her. That's not something you notice until it's gone.";

	private const string NextScenePath = "res://scenes/world/room_2.tscn";
	private const string JourneyScenePath = "res://scenes/world/journey_to_room_2.tscn";
	private const string CopperKeyIconPath = "res://assets/images/core/items/bridge_key.png";
	private string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";

	// Local-space shapes, in this Area2D's own scaled coordinates. The
	// authored brass shape is 142.5x44 under a (1.54, 1.49) shape scale;
	// the two added shapes sit below it in the open drawer and never
	// overlap it, so a click always names exactly one thing.
	private static readonly Rect2 BrassRect = new(23 - 110, -6 - 33, 220, 66);
	private static readonly Rect2 CopperRect = new(82 - 60, 71 - 38, 120, 76);

	private bool _collecting;
	private Sprite2D _copperSprite;
	private CollisionShape2D _copperShape;

	public override void _Ready()
	{
		InputPickable = true;
		BuildDrawerExtras();
	}

	/// <summary>The copper key was never in the authored scene, so it is
	/// laid in the drawer from code — present exactly while Mara has
	/// not yet taken it, gone for good once she has.</summary>
	private void BuildDrawerExtras()
	{
		var gs = GameState.Instance;

		if (gs?.HasFlag("item_hall_key_bridge") != true)
		{
			_copperSprite = new Sprite2D
			{
				Name = "CopperKeySprite",
				Texture = GD.Load<Texture2D>(CopperKeyIconPath),
				Position = CopperRect.GetCenter(),
				Scale = new Vector2(0.25f, 0.25f),
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			AddChild(_copperSprite);
			_copperShape = new CollisionShape2D
			{
				Name = "CopperKeyShape",
				Position = CopperRect.GetCenter(),
				Shape = new RectangleShape2D { Size = CopperRect.Size },
			};
			AddChild(_copperShape);
		}

	}

	/// <summary>Asked by Room1Puzzle's hover cursor: is the hand over one
	/// of the smaller takeable still lying in the drawer?</summary>
	public bool IsOverTakeableItem(Vector2 globalPosition)
	{
		Vector2 local = ToLocal(globalPosition);
		return _copperSprite != null && _copperSprite.Visible && CopperRect.HasPoint(local);
	}

	public override async void _InputEvent(Viewport viewport, InputEvent @event, int shapeIdx)
	{
		if (!Visible || _collecting || Dialogue.IsInputBlocked)
			return;

		if (@event is not InputEventMouseButton mouseEvent)
			return;

		if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		Vector2 local = ToLocal(GetGlobalMousePosition());

		// The smaller thing first: taking it is its own small moment
		// and never moves Mara anywhere.
		if (_copperSprite != null && _copperSprite.Visible && CopperRect.HasPoint(local))
		{
			_collecting = true;
			try { await TakeCopperKeyAsync(); }
			finally { _collecting = false; }
			return;
		}
		if (!BrassRect.HasPoint(local))
			return;

		var gs = GameState.Instance;
		// A sealed kitchen refuses ordinary traffic — but while the
		// drawer still keeps something untaken, the brass key stays a
		// way through, and leaving by it asks the look-around question.
		if (gs?.IsRoomSealed("Room1") == true &&
			gs?.RoomHasUnclaimedPracticalItem("Room1") != true)
		{
			gs.NotifySealedInteraction();
			return;
		}

		_collecting = true;
		bool firstTime;
		if (GameState.Instance != null)
		{
			firstTime = GameState.Instance.CollectShard(0, FragmentText);
		}
		else
		{
			firstTime = !Globals.SHARDS_COLLECTED[0];
			Globals.SHARDS_COLLECTED[0] = true;
		}

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		if (firstTime)
		{
			await Dialogue.ShowText(this, "Every morning started here. Every morning she was already waiting, already loud, already herself.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 26);
			await Dialogue.ShowText(this, "You could set your clock by her. You always knew, in that kitchen, that someone else was in the world with you.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
			await Dialogue.ShowText(this, "That's not something you notice until it's gone.", 4.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 30);
		}
		else
		{
			// Passing through again on a later visit — keep it brief.
			await Dialogue.ShowText(this, "\"That's not something you notice until it's gone.\"", 2.5f, GlobalPosition.X, GlobalPosition.Y, FontPath, 26);
		}

		// The walk belongs to the first time she leaves with the
		// fragment — by seen-flag, not by collection, so looking
		// around first (or fetching the copper key late) never eats it.
		bool goJourney = gs != null && !gs.HasFlag("journey_seen_Room1ToRoom2");
		SceneManager.ChangeScene(goJourney ? JourneyScenePath : NextScenePath);
	}

	private async System.Threading.Tasks.Task TakeCopperKeyAsync()
	{
		var gs = GameState.Instance;
		if (gs == null || gs.HasFlag("item_hall_key_bridge"))
			return;

		AudioManager.Play(Sfx.KeyType, -9f, 0.95f);
		await Dialogue.ShowText(this, "Beside the brass one lies a second, smaller key — copper gone green at the edges. Its bow is cut like a little bridge: two posts, one arch.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
		if (!GodotObject.IsInstanceValid(this))
			return;
		await Dialogue.ShowText(this, "That one isn't a memory. It's a key. Actual weight. Actual teeth. The house doesn't hand those out as decoration.", 5.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
		if (!GodotObject.IsInstanceValid(this))
			return;

		gs.SetFlag("item_hall_key_bridge", save: false);
		gs.UnlockClue("hall_key_bridge");
		gs.Toast("Copper bridge key — kept in Mara's bag (B).");
		HideTaken(_copperSprite, _copperShape);
	}

	private static void HideTaken(Sprite2D sprite, CollisionShape2D shape)
	{
		if (sprite != null)
			sprite.Visible = false;
		// _InputEvent arrives from physics picking; shape state changes
		// must wait for the deferred call.
		if (shape != null)
			shape.SetDeferred("disabled", true);
	}
}

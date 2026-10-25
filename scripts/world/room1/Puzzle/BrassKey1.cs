using Godot;

/// <summary>
/// What the kitchen drawer was keeping, in the close-up, each thing in
/// its own place and taken by its own hand:
///
///   • the brass key — shard 0, the morning itself;
///   • beside it, a smaller copper key gone green at the edges, its bow
///     cut like a little bridge — the first of the hallway door's three
///     ordinary keys;
///   • in the drawer's back corner, one of Mum's bent hairpins,
///     borrowed for a hundred little jobs it wasn't made for.
///
/// The brass key alone is enough to go on. The other two are simply
/// there, visible, takeable or leavable — and if Mara leaves them, the
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
	private const string HairpinIconPath = "res://assets/images/core/items/hairpin.png";
	private string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";

	// Local-space shapes, in this Area2D's own scaled coordinates. The
	// authored brass shape is 142.5x44 under a (1.54, 1.49) shape scale;
	// the two added shapes sit below it in the open drawer and never
	// overlap it, so a click always names exactly one thing.
	private static readonly Rect2 BrassRect = new(23 - 110, -6 - 33, 220, 66);
	private static readonly Rect2 CopperRect = new(82 - 60, 71 - 38, 120, 76);
	private static readonly Rect2 HairpinRect = new(-134 - 60, 95 - 38, 120, 76);

	private bool _collecting;
	private Sprite2D _copperSprite;
	private CollisionShape2D _copperShape;
	private Sprite2D _hairpinSprite;
	private CollisionShape2D _hairpinShape;

	public override void _Ready()
	{
		InputPickable = true;
		BuildDrawerExtras();
	}

	/// <summary>The copper key and the hairpin were never in the authored
	/// scene, so they are laid in the drawer from code — present exactly
	/// while Mara has not yet taken them, gone for good once she has.</summary>
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

		if (gs?.HasFlag("item_hairpin") != true)
		{
			_hairpinSprite = new Sprite2D
			{
				Name = "HairpinSprite",
				Texture = GD.Load<Texture2D>(HairpinIconPath),
				Position = HairpinRect.GetCenter(),
				Scale = new Vector2(0.25f, 0.25f),
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			AddChild(_hairpinSprite);
			_hairpinShape = new CollisionShape2D
			{
				Name = "HairpinShape",
				Position = HairpinRect.GetCenter(),
				Shape = new RectangleShape2D { Size = HairpinRect.Size },
			};
			AddChild(_hairpinShape);
		}
	}

	/// <summary>Asked by Room1Puzzle's hover cursor: is the hand over one
	/// of the two smaller takeables still lying in the drawer?</summary>
	public bool IsOverTakeableItem(Vector2 globalPosition)
	{
		Vector2 local = ToLocal(globalPosition);
		if (_copperSprite != null && _copperSprite.Visible && CopperRect.HasPoint(local))
			return true;
		if (_hairpinSprite != null && _hairpinSprite.Visible && HairpinRect.HasPoint(local))
			return true;
		return false;
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

		// The two smaller things first: taking one is its own small
		// moment and never moves Mara anywhere.
		if (_copperSprite != null && _copperSprite.Visible && CopperRect.HasPoint(local))
		{
			_collecting = true;
			try { await TakeCopperKeyAsync(); }
			finally { _collecting = false; }
			return;
		}
		if (_hairpinSprite != null && _hairpinSprite.Visible && HairpinRect.HasPoint(local))
		{
			_collecting = true;
			try { await TakeHairpinAsync(); }
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

		SceneManager.ChangeScene(firstTime ? JourneyScenePath : NextScenePath);
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

	private async System.Threading.Tasks.Task TakeHairpinAsync()
	{
		var gs = GameState.Instance;
		if (gs == null || gs.HasFlag("item_hairpin"))
			return;

		AudioManager.Play(Sfx.PhotoPickup, -10f, 0.9f);
		AudioManager.Play(Sfx.KeyType, -13f, 1.35f);
		await Dialogue.ShowText(this, "In the drawer's back corner, half under a twist of string — one of Mum's hairpins. Bent, the way every hairpin in that house ended up bent: borrowed for catches, seals, little jobs it wasn't made for.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
		if (!GodotObject.IsInstanceValid(this))
			return;
		await Dialogue.ShowText(this, "I slide it into my sleeve. Old habits travel.", 4.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 26);
		if (!GodotObject.IsInstanceValid(this))
			return;

		gs.SetFlag("item_hairpin", save: false);
		gs.UnlockClue("bedroom_hairpin");
		gs.Toast("Mum's bent hairpin — kept in Mara's bag (B).");
		HideTaken(_hairpinSprite, _hairpinShape);
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

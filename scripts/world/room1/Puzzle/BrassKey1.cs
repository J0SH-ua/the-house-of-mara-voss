using Godot;

/// <summary>
/// The brass key in the kitchen drawer — shard 0 — and, threaded on
/// the same ring, the first of the hallway door's three ordinary keys.
/// Collection is idempotent and goes through GameState so the journal,
/// HUD, sting and autosave all happen exactly once.
/// </summary>
public partial class BrassKey1 : Area2D
{
	private const string FragmentText =
		"Every morning started here. Every morning she was already waiting, already loud, " +
		"already herself. You could set your clock by her. That's not something you notice until it's gone.";

	private const string NextScenePath = "res://scenes/world/room_2.tscn";
	private const string JourneyScenePath = "res://scenes/world/journey_to_room_2.tscn";
	private string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
	private bool _collecting;

	public override void _Ready()
	{
		InputPickable = true;
	}

	public override async void _InputEvent(Viewport viewport, InputEvent @event, int shapeIdx)
	{
		if (!Visible || _collecting || Dialogue.IsInputBlocked)
			return;

		if (@event is not InputEventMouseButton mouseEvent)
			return;

		if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		if (GameState.Instance?.IsRoomSealed("Room1") == true)
		{
			GameState.Instance.NotifySealedInteraction();
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

		var gs = GameState.Instance;
		if (gs != null && !gs.HasFlag("item_hall_key_bridge"))
		{
			AudioManager.Play(Sfx.KeyType, -9f, 0.95f);
			await Dialogue.ShowText(this, "Threaded on the same ring is a second, smaller key — copper gone green at the edges. Its bow is cut like a little bridge: two posts, one arch.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
			await Dialogue.ShowText(this, "That one isn't a memory. It's a key. Actual weight. Actual teeth. The house doesn't hand those out as decoration.", 5.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
			if (GodotObject.IsInstanceValid(this))
			{
				gs.SetFlag("item_hall_key_bridge", save: false);
				gs.UnlockClue("hall_key_bridge");
				gs.Toast("Copper bridge key — kept in Mara's bag (B).");
			}
		}

		SceneManager.ChangeScene(firstTime ? JourneyScenePath : NextScenePath);
	}
}

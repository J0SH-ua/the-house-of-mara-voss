using Godot;

/// <summary>
/// The brass key in the kitchen drawer — shard 0.
/// Collection is idempotent and goes through GameState so the journal,
/// HUD, sting and autosave all happen exactly once.
/// </summary>
public partial class BrassKey1 : Area2D
{
	private const string FragmentText =
		"Every morning started here. Every morning she was already waiting, already loud, " +
		"already herself. You could set your clock by her. That's not something you notice until it's gone.";

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

		_collecting = true;
		GameState.Instance?.CollectShard(0, FragmentText);
		if (GameState.Instance == null)
			Globals.SHARDS_COLLECTED[0] = true;

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		await Dialogue.ShowText(this, "Every morning started here. Every morning she was already waiting, already loud, already herself.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 26);
		await Dialogue.ShowText(this, "You could set your clock by her. You always knew, in that kitchen, that someone else was in the world with you.", 6.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 24);
		await Dialogue.ShowText(this, "That's not something you notice until it's gone.", 4.0f, GlobalPosition.X, GlobalPosition.Y, FontPath, 30);

		SceneManager.ChangeScene("res://scenes/world/room_2.tscn");
	}
}

using Godot;

/// <summary>
/// The in-room Clarity eye. Clicking it arms Clarity; clicking again (or
/// right-clicking anywhere, or pressing the toggle_clarity key) releases it
/// for free. All rules live in ClaritySystem — this class only translates
/// clicks on its tiles.
/// </summary>
public partial class ClarityToggle : TileMapHoverCursor
{
	public override void _Input(InputEvent @event)
	{
		if (Dialogue.IsInputBlocked)
			return;

		if (@event is not InputEventMouseButton mouseButton)
			return;

		if (!mouseButton.Pressed || mouseButton.ButtonIndex != MouseButton.Left)
			return;

		if (!HasPointedTile(mouseButton.Position))
			return;

		ClaritySystem.Toggle();
		GetTree().Root.SetInputAsHandled();
	}

	public override void _Process(double delta)
	{
		if (Dialogue.IsInputBlocked)
			return;

		if (ClaritySystem.IsArmed)
			return;

		base._Process(delta);
	}

	private bool HasPointedTile(Vector2 globalMousePosition)
	{
		Vector2 localMousePosition = ToLocal(globalMousePosition);
		Vector2I cell = LocalToMap(localMousePosition);
		return GetCellSourceId(cell) != -1;
	}
}

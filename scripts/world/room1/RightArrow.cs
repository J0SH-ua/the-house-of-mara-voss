using Godot;

/// <summary>
/// Room 1 → Room 2 navigation arrow. Navigation only: this used to also set
/// SHARDS_COLLECTED[0] unconditionally, quietly granting the shard without
/// the drawer puzzle ever being solved.
/// </summary>
public partial class RightArrow : Area2D
{
    public override void _Ready()
    {
        InputPickable = true;
    }

    public override void _InputEvent(Viewport viewport, InputEvent @event, int shapeIdx)
    {
        if (Dialogue.IsInputBlocked || SceneManager.IsChanging)
            return;

        if (@event is not InputEventMouseButton mouseEvent)
            return;

        if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
            return;

        if (GameState.Instance?.IsRoomSealed("Room1") == true)
        {
            GameState.Instance.NotifySealedInteraction();
            GetTree().Root.SetInputAsHandled();
            return;
        }

        AudioManager.Play(Sfx.UiClick, -6f);
        SceneManager.ChangeScene("res://scenes/world/room_2.tscn");
        GetTree().Root.SetInputAsHandled();
    }
}

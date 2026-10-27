using Godot;

/// <summary>
/// The preserve jars in Room 3 — now only the way in.
///
/// The last jar has its own close-up (JarCloseup): a seal to work by
/// hand, and inside it, in layers, Ida's folded name tape (shard 2)
/// and the circle key sleeping underneath. This handler keeps the
/// room's older rules — everything here is faced before the jars will
/// be touched — and one new mercy: a sealed Room 3 still lets Mara
/// back to the jar while the circle key lies untaken inside it.
/// Whether she has anything thin enough to open the seal is the
/// close-up's question to ask, with her hands, not a flag's.
/// </summary>
public partial class JarsExamine1 : ExamineHandler
{
    private const string JarCloseupScenePath = "res://scenes/world/jar_closeup.tscn";

    public override async void _Input(InputEvent @event)
    {
        if (Dialogue.IsInputBlocked)
            return;

        // A click, or F: the house's hand, for the thing under the
        // cursor. Same reach, same rules, either way.
        bool clicked = @event is InputEventMouseButton mouseEvent && mouseEvent.Pressed &&
            mouseEvent.ButtonIndex == MouseButton.Left;
        bool useKey = @event is InputEventKey keyEvent && keyEvent.Pressed &&
            !keyEvent.Echo && keyEvent.PhysicalKeycode == Key.F;
        if (!clicked && !useKey)
            return;

        Vector2 globalMouse = GetGlobalMousePosition();
        Vector2 localMouse = ToLocal(globalMouse);
        Vector2I cell = LocalToMap(localMouse);

        if (GetCellSourceId(cell) == -1)
            return;

        var gs = GameState.Instance;
        if (gs?.IsRoomSealed("Room3") == true &&
            gs?.RoomHasUnclaimedPracticalItem("Room3") != true)
        {
            gs.NotifySealedInteraction();
            GetTree().Root.SetInputAsHandled();
            return;
        }

        if (!RoomExamineTracker.HasRoomBeenFullyExamined("Room3"))
        {
            AudioManager.Play(Sfx.Denied, -8f);
            GetTree().Root.SetInputAsHandled();
            await Dialogue.ShowText(this,
                "The jars can wait. Everything in this room was kept for a reason.",
                3.5f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
            return;
        }

        GetTree().Root.SetInputAsHandled();
        AudioManager.Play(Sfx.Examine, -6f);
        SceneManager.ChangeScene(JarCloseupScenePath);
    }
}

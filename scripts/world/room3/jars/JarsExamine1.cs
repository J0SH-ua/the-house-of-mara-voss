using Godot;

public partial class JarsExamine1 : ExamineHandler
{
    public override async void _Input(InputEvent @event)
    {
        if (Dialogue.IsInputBlocked)
            return;

        if (!(@event is InputEventMouseButton mouseEvent) || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
            return;

        Vector2 globalMouse = GetGlobalMousePosition();
        Vector2 localMouse = ToLocal(globalMouse);
        Vector2I cell = LocalToMap(localMouse);

        if (GetCellSourceId(cell) == -1)
            return;

        if (!RoomExamineTracker.HasRoomBeenFullyExamined("Room3"))
        {
            // Locked used to mean silence; explain the gate and progress.
            RoomExamineTracker.GetRoomProgress("Room3", out int done, out int total, out _);
            AudioManager.Play(Sfx.Denied, -8f);
            await Dialogue.ShowText(this,
                $"The jars can wait. Everything in this room was kept for a reason — look at all of it first. ({done}/{total})",
                3.5f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
            return;
        }

        AudioManager.Play(Sfx.Examine, -6f);
        GameState.Instance?.MarkRoomCompleted("Room3");

        GetTree().Root.SetInputAsHandled();

        Dialogue.BeginInputBlock();
        try
        {
            await Dialogue.ShowText(this, ExamineText, DialogueDuration, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: DialogueFontSize);

            await Dialogue.ShowText(this, "She kept everything. That's what love does when it doesn't know what else to do — it keeps everything exactly where it was and calls it waiting.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 20);
            await Dialogue.ShowText(this, "The jars, the bag, the drawings on the wall. She kept the spiders. She kept the names.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
            await Dialogue.ShowText(this, "She kept a note she meant to give someone. Everything in here was preserved, and preservation is not the same as keeping someone. But the heart does what it can.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
        }
        finally
        {
            Dialogue.EndInputBlock();
        }


        SceneManager.ChangeScene("res://scenes/world/room_4.tscn");
    }
}
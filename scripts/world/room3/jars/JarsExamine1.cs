using Godot;

public partial class JarsExamine1 : ExamineHandler
{
    private const string NextScenePath = "res://scenes/world/room_4.tscn";
    private const string JourneyScenePath = "res://scenes/world/journey_to_room_4.tscn";

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

        if (GameState.Instance?.IsRoomSealed("Room3") == true)
        {
            GameState.Instance.NotifySealedInteraction();
            GetTree().Root.SetInputAsHandled();
            return;
        }

        if (!RoomExamineTracker.HasRoomBeenFullyExamined("Room3"))
        {
            AudioManager.Play(Sfx.Denied, -8f);
            await Dialogue.ShowText(this,
                "The jars can wait. Everything in this room was kept for a reason.",
                3.5f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
            return;
        }

        AudioManager.Play(Sfx.Examine, -6f);
        RoomExamineTracker.RecordExamine("Room3", "Jars");
        bool firstFragment = GameState.Instance?.CollectShard(2) ?? false;
        GameState.Instance?.MarkRoomCompleted("Room3");

        GetTree().Root.SetInputAsHandled();

        Dialogue.BeginInputBlock();
        try
        {
            await Dialogue.ShowText(this, ExamineText, DialogueDuration, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: DialogueFontSize);

            if (firstFragment)
            {
                await Dialogue.ShowText(this, "At the bottom of the last jar is a folded square of name tape. IDA, in careful block letters. The one label Mara could never throw away.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 22);
                await Dialogue.ShowText(this, "Ida named the spiders so she could say them aloud without flinching. Mara used to repeat the names after her, in the order Ida gave them, until fear sounded smaller than the voice holding it.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 22);
                await Dialogue.ShowText(this, "That is the first new way this memory gives back: Echo — her sister's old courage, turned into listening. From here, remembering has verbs.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 22);
            }

            await Dialogue.ShowText(this, "She kept everything. That's what love does when it doesn't know what else to do — it keeps everything exactly where it was and calls it waiting.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 20);
            await Dialogue.ShowText(this, "The jars, the bag, the drawings on the wall. She kept the spiders. She kept the names.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
            await Dialogue.ShowText(this, "She kept a note she meant to give someone. Everything in here was preserved, and preservation is not the same as keeping someone. But the heart does what it can.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
        }
        finally
        {
            Dialogue.EndInputBlock();
        }


        SceneManager.ChangeScene(firstFragment ? JourneyScenePath : NextScenePath);
    }
}
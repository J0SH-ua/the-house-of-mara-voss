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

        // The last seal is the room's practical gate. Fingernails only
        // worry it; the bent hairpin from Mara's bedroom nightstand is
        // thin enough to lift the rubber without breaking Ida's jar.
        if (GameState.Instance != null && !GameState.Instance.HasFlag("item_hairpin"))
        {
            AudioManager.Play(Sfx.Denied, -8f, 0.92f);
            GetTree().Root.SetInputAsHandled();
            await Dialogue.ShowText(this,
                "The last jar's seal won't give. Not to fingernails. It wants something thin worried under the rubber — the kind of thing a mother leaves bent behind a bedroom clock.",
                5.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 24);
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
                AudioManager.Play(Sfx.LockpickTick, -12f, 1.05f);
                await Dialogue.ShowText(this, "The last seal sticks — until Mum's bent hairpin worries under the rubber and the jar sighs open without breaking.", 5.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 22);
                await Dialogue.ShowText(this, "At the bottom of the last jar is a folded square of name tape. IDA, in careful block letters. The one label Mara could never throw away.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 22);
                await Dialogue.ShowText(this, "Ida named the spiders so she could say them aloud without flinching. Mara used to repeat the names after her, in the order Ida gave them, until fear sounded smaller than the voice holding it.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 22);
                await Dialogue.ShowText(this, "That is the first new way this memory gives back: Echo — her sister's old courage, turned into listening. From here, remembering has verbs.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 22);

                var gs = GameState.Instance;
                if (gs != null && !gs.HasFlag("item_hall_key_circle"))
                {
                    AudioManager.Play(Sfx.KeyType, -10f, 1.05f);
                    await Dialogue.ShowText(this, "Under the folded tape, something cold moves against the glass. A key. Its bow is a perfect circle — no beginning, no end, preserved with everything else she couldn't let go.", 6.0f, GetGlobalPosition().X, GetGlobalPosition().Y, fontSize: 22);
                    if (GodotObject.IsInstanceValid(this))
                    {
                        gs.SetFlag("item_hall_key_circle", save: false);
                        gs.UnlockClue("hall_key_circle");
                        gs.Toast("Circle key — kept in Mara's bag (B).");
                    }
                }
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
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The Porch, at 4:47 — the main-path stop between the garden road and the
/// last room.
///
/// Room 6 traces the way Ida went; the road at its end used to open straight
/// onto Room 7. It bends here instead: the front of the Eldfen house at the
/// minute Mara finally arrived. Three looks (the lit window, the steps, the
/// garden path) write "The porch, at 4:47" into the Journal. The front door
/// will not take Mara inside until all three have actually been faced:
/// this time, she does not rush across the porch without looking. Crossed
/// once, the porch counts as passed, and Room 7 steps back here.
/// </summary>
public partial class Porch447 : MemoryRoomBase
{
    private const string SeenFlag = "porch_seen";
    private const string PassedFlag = "porch_passed";
    private const string Room7ScenePath = "res://scenes/world/room_7.tscn";

    protected override string RoomName => "Porch447";
    protected override string BackgroundPath => "res://assets/images/bgs/Porch_447_BG.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Later entries win overlaps: the grounds first, the door last.
        yield return new Hotspot("GardenPath", "The Garden Path",
            new Rect2(690, 290, 440, 320), new Vector2(905, 455), Sfx.Examine, 0.75f);
        yield return new Hotspot("LitWindow", "The Lit Window",
            new Rect2(345, 100, 195, 310), new Vector2(375, 272), Sfx.WordChime, 0.92f);
        yield return new Hotspot("PorchSteps", "The Porch Steps",
            new Rect2(285, 385, 300, 185), new Vector2(435, 478), Sfx.Examine, 0.68f);
        yield return new Hotspot("FrontDoor", "The Front Door",
            new Rect2(40, 105, 300, 240), new Vector2(210, 268),
            Sfx.DoorCreak, 0.9f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        _ = PlayStoryLessonOnceAsync("story_lesson_porch_447",
            "The porch. Mara has crossed it a thousand times inside the memory. She has never once stopped on it.");
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        if (MemoryVerbSystem.Active != MemoryVerb.None)
        {
            _ = NoVerbHereAsync();
            return;
        }

        switch (hotspot.Id)
        {
            case "LitWindow":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The window's lit the way it was that day. Someone home. Someone thinking the afternoon would just end like afternoons do.",
                    "Mum left lights on for anyone coming home late. This is the last one that ever waited for both her girls.",
                }, "Still lit. In my head it always will be.");
                break;

            case "PorchSteps":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Here. Right here. This is where I stood at 4:47 and called her name until calling stopped meaning come home.",
                    "The steps still hold the shape of me arriving. Nothing answered. Just the swing, finishing its swing.",
                }, "Forty-seven minutes late. The steps keep the number so I don't have to say it.");
                break;

            case "GardenPath":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The path runs down past the apple trees to the gate. From here it looks so short. That's the part the house usually hides from me. How short it was.",
                    "A child's walk. A few minutes end to end. Twelve years long, in the other direction.",
                }, "Shorter than memory allows. It always was.");
                break;

            case "FrontDoor":
                _ = EnterLastRoomAsync();
                break;
        }
    }

    private async Task NoVerbHereAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -11f, 0.92f);
            await Say("The porch does not want a verb. Things only happened here.", 4.5f, 23);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task EnterLastRoomAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            if (!AllRequiredExamined())
            {
                AudioManager.Play(Sfx.Denied, -9f, 0.9f);
                var door = GetHotspot("FrontDoor");
                if (door != null)
                    Hotspots.SpawnRipple(door.Center, new Color(0.78f, 0.72f, 0.64f));
                await Say("My hand stops on the handle. Not yet. I rushed across this porch once without looking. This time I stay until I've seen it.", 5f, 23);
                return;
            }

            var gs = GameState.Instance;
            gs?.SetFlag(SeenFlag);
            gs?.SetFlag(PassedFlag);
            await SaySequence(
                "Mara puts her hand on the door she has opened ten thousand times in her head.",
                "This time she opens it with everything she knows carried in front of her, where she can see it.");

            AudioManager.Play(Sfx.DoorCreak, -7f, 0.88f);
            await WaitSeconds(0.25f);
            SceneManager.ChangeScene(Room7ScenePath);
        }
        finally
        {
            Busy = false;
        }
    }
}

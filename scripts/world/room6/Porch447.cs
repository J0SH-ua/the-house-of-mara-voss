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
/// leads on to the last room whenever Mara is ready — crossed once, the
/// porch counts as passed, and Room 7 steps back here.
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
            new Rect2(40, 105, 300, 240), new Vector2(190, 225), Sfx.WordChime, 0.92f);
        yield return new Hotspot("PorchSteps", "The Porch Steps",
            new Rect2(285, 385, 300, 185), new Vector2(435, 478), Sfx.Examine, 0.68f);
        yield return new Hotspot("FrontDoor", "The Front Door",
            new Rect2(345, 100, 195, 310), new Vector2(442, 255),
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
                    "The window is lit the way it was lit that day — someone home, someone expecting the afternoon to end ordinarily.",
                    "Her mother left lights on for latecomers. This is the last one that ever waited for both of her daughters.",
                }, "Lit, the way it will be lit in her forever.");
                break;

            case "PorchSteps":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Here. Exactly here. This is where Mara stood at 4:47 and called until calling changed its meaning.",
                    "The steps remember the shape of her arriving. Nothing in the garden answered except the swing, finishing its swing.",
                }, "Forty-seven minutes past. The steps keep the number so she doesn't have to say it.");
                break;

            case "GardenPath":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The path goes down past the apple trees to the gate — the way she traced, walked backwards. From the porch it looks so short. That is the cruelty the house usually edits out: how short it was.",
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
            var gs = GameState.Instance;
            gs?.SetFlag(PassedFlag);
            if (AllRequiredExamined())
            {
                gs?.SetFlag(SeenFlag);
                await SaySequence(
                    "Mara puts her hand on the door she has opened ten thousand times in her head.",
                    "This time she opens it with everything she knows carried in front of her, where she can see it.");
            }
            else
            {
                await Say("Through the front door, the way she came at 4:47 — too fast to look at anything.", 4.5f, 23);
            }

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

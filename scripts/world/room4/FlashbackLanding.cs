using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The Landing, That Evening — a flashback inside Room 4's mirror.
///
/// Not a puzzle room: a place the story was missing. The hallway mirror in
/// Room 4 has always shown "a woman sitting on a floor — somewhere else."
/// Stepping closer to it a second time puts Mara inside that evening: the
/// night Ida did not come home, kept by the house in blue held-breath light.
/// Three looks (the last photograph, the wall light, the carpet runner)
/// carry the reveal the main rooms only circle: what that night actually
/// looked like, why Ida's face is half-turned in every photograph the house
/// keeps, and Mara's first clean sight of her sister — an illusion the
/// memory performs out of habit, not haunting. Completing the three looks
/// writes "The landing, that evening" into the Journal's Notes &amp; Clues.
/// The far end of the hall returns to Room 4 at any time.
/// </summary>
public partial class FlashbackLanding : MemoryRoomBase
{
    private const string SeenFlag = "flashback_landing_seen";
    private const string Room4ScenePath = "res://scenes/world/room_4.tscn";

    protected override string RoomName => "Room4Flashback";
    protected override string BackgroundPath => "res://assets/images/bgs/BgRoom4.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Broad floor first; later entries win overlaps.
        yield return new Hotspot("CarpetRunner", "Carpet Runner",
            new Rect2(330, 462, 480, 158), new Vector2(570, 540), Sfx.Examine, 0.7f);
        yield return new Hotspot("LastPhotograph", "The Last Photograph",
            new Rect2(28, 64, 386, 352), new Vector2(220, 240), Sfx.PhotoPickup, 1f);
        yield return new Hotspot("WallLight", "Wall Light",
            new Rect2(698, 104, 118, 96), new Vector2(757, 152), Sfx.WordChime, 0.9f);
        yield return new Hotspot("HallEnd", "The Far End of the Hall",
            new Rect2(492, 186, 196, 258), new Vector2(590, 315),
            Sfx.DoorCreak, 0.9f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        // That evening, remembered in a held breath: the same hallway art,
        // drowned toward blue so it can never be confused with the present.
        SetBackgroundTint(new Color(0.62f, 0.72f, 0.95f));

        _ = PlayStoryLessonOnceAsync("story_lesson_flashback_landing",
            "This is the landing as it was that evening — not the hallway the house shows Mara now. The light is wrong because grief remembers light wrong: everything the colour of a held breath.");
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
            case "LastPhotograph":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "September, that year. Her school photo — new jumper, grinning past the camera at whoever stood behind it. She always grinned at the person. Never at the lens.",
                    "Look down the whole wall. In every photo she's turned half away — over a shoulder, mid-laugh. She wasn't shy. I just can't get her back straight-on any more.",
                    "Something in this house keeps editing her, gently, all the time. I never asked it to. That's the scariest part — it lies the way a mother would. To be kind.",
                }, "Half-turned in every frame. The house does its kindnesses without asking me.");
                break;

            case "WallLight":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Mum left this light on every night we were kids — \"for whoever comes home late.\" That night it burned till morning. She sat under it, on the floor, facing Ida's door.",
                    "Nobody ever told me about that night. I was twenty-two, three towns away, asleep till the phone rang. Everything I know about this landing comes from one sentence Mum said in this hall, years later — to the daughter who was still here to hear it.",
                }, "The latecomers' light. Still on at dawn. So was she.");
                break;

            case "CarpetRunner":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "If I stand very still, the hall remembers her feet. A girl in socks, arms windmilling, sliding the whole length of it — straight through where I'm standing. She doesn't see me.",
                    "She's not a ghost. This isn't a haunting. She's a habit my eyes have. She coasts to the mirror, counts her eleven seconds out loud, and she's gone — mid-laugh, not looking back.",
                    "I don't reach for her. That's new. For twelve years I reached every single time.",
                }, "Eleven seconds, kitchen to mirror. She made everyone count. The runner still does.");
                break;

            case "HallEnd":
                _ = LeaveEveningAsync();
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
            AudioManager.Play(Sfx.Denied, -11f, 0.95f);
            await Say("No way of remembering belongs to this evening. It does not want to be echoed, held, traced, or released. It only wants, at last, to be looked at.", 5f, 23);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LeaveEveningAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            if (AllRequiredExamined())
            {
                gs?.SetFlag(SeenFlag);
                await SaySequence(
                    "At the far end, the girl in socks slides past one last time — and Mara only counts. Eleven seconds. Some numbers stay because they were loved, not because they were wounds.",
                    "Then the evening folds itself away, the way it always has. Only now Mara knows it is there, which is a different thing from being kept from it.");
            }
            else
            {
                await Say("Mara steps back out of the evening, unfinished. The mirror will keep it exactly where she left it. It is very good at that.", 4.5f, 23);
            }

            AudioManager.Play(Sfx.DoorCreak, -8f, 0.92f);
            await WaitSeconds(0.25f);
            SceneManager.ChangeScene(Room4ScenePath);
        }
        finally
        {
            Busy = false;
        }
    }
}

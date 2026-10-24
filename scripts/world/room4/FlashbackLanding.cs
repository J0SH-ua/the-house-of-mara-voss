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
                    "September, that year. A school photograph: Ida in a too-new jumper, grinning past the camera at whoever was standing behind it. She was always grinning at the person, never at the lens.",
                    "Look along the whole wall. In every frame this house keeps, her face is half-turned — profile, over a shoulder, mid-laugh away. It is not that she was shy. It is that the memory can no longer give her back straight-on.",
                    "Something in this house is editing, kindly and constantly, and Mara never asked it to. That might be the most frightening thing in it: not that the house lies, but that it lies the way a mother would.",
                }, "Half-turned in every frame. The house does its mercies without asking permission.");
                break;

            case "WallLight":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Their mother left this light burning every night of their childhood — \"for whoever comes home late.\" That evening it burned until morning, and she sat beneath it on the floor, facing Ida's door.",
                    "Nobody ever described that night to Mara. She was twenty-two, three towns away, asleep until the telephone. Everything she knows of this landing, the house has had to invent from a single sentence her mother said in this hall, years later, to the wrong daughter — the one who was still there to hear it.",
                }, "The light she left for latecomers. It was still on at dawn. So was she.");
                break;

            case "CarpetRunner":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The runner remembers feet. Stand very still and the memory obliges: a girl in socks launches from the kitchen end, arms windmilling, sliding the whole length of the hall — straight through where Mara stands, without seeing her.",
                    "She is not a ghost, and this is not a haunting. She is a habit of the eyes. She coasts to the mirror, counts her eleven seconds aloud to no one, and is gone the way she always goes: mid-laugh, and not looking back.",
                    "Mara does not reach for her. That is new. For twelve years she has reached every single time.",
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

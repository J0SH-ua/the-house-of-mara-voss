using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The Apartment, Now — the present tense, reached through Room 5's rain
/// window once the room has been Anchored.
///
/// Not a puzzle room: the place the story was missing on the other side.
/// Room 5 keeps sliding between the childhood bedroom and this apartment;
/// stepping through the rain puts Mara inside the adult life the slide keeps
/// interrupting — unopened mail, a silent phone, a chair in the corner.
/// Facing all three writes "The life that continued" into the Journal.
/// The entry door returns to Room 5 at any time.
/// </summary>
public partial class ApartmentNow : MemoryRoomBase
{
    private const string SeenFlag = "apartment_seen";
    private const string Room5ScenePath = "res://scenes/world/room_5.tscn";

    protected override string RoomName => "ApartmentNow";
    protected override string BackgroundPath => "res://assets/images/bgs/Apartment_Now_BG.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Later entries win overlaps: furniture first, the door last.
        yield return new Hotspot("SilentPhone", "The Phone Table",
            new Rect2(330, 280, 250, 215), new Vector2(455, 388), Sfx.KeyType, 0.8f);
        yield return new Hotspot("UnopenedMail", "Mail Behind the Door",
            new Rect2(620, 472, 270, 132), new Vector2(755, 538), Sfx.PageTurn, 0.9f);
        yield return new Hotspot("CornerChair", "The Chair in the Corner",
            new Rect2(905, 285, 225, 290), new Vector2(1015, 430), Sfx.Examine, 0.7f);
        yield return new Hotspot("EntryDoor", "The Entry Door",
            new Rect2(640, 105, 215, 355), new Vector2(747, 282),
            Sfx.DoorCreak, 0.95f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        _ = PlayStoryLessonOnceAsync("story_lesson_apartment_now",
            "The present tense. Mara's building, Mara's door — the life that continued while she was elsewhere being haunted.");
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
            case "UnopenedMail":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Mail behind the door, in strata. Bills. A birthday card, two years unopened. At the bottom, a postcard in her mother's hand that Mara already knows by heart — which is not the same as having read it.",
                    "Every envelope is a person mid-sentence. She stopped letting them finish years ago.",
                }, "Unopened is not the same as unsent. She knows that. Knowing is not the same as opening.");
                break;

            case "SilentPhone":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The phone rings for other people in this building. Through the wall, Mara hears lives being answered.",
                    "Hers has been silent long enough that silence started to feel like something she chose. She isn't sure anymore whether she chose it — or whether it chose the shape of her.",
                }, "No messages. That used to feel like peace.");
                break;

            case "CornerChair":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "One chair, turned to the corner like a child being punished. Mara sits here at the bad hours, knees drawn up, waiting for a morning that happened twelve years ago to be over.",
                    "The chair is the truest furniture in the apartment. Everything else is a set from a play about a woman who is fine.",
                }, "The corner of the room. The corner of everything.");
                break;

            case "EntryDoor":
                _ = LeaveApartmentAsync();
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
            await Say("No way of remembering belongs here. This is not a memory. It is Tuesday.", 4.5f, 23);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LeaveApartmentAsync()
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
                    "At the door, Mara looks back once — not at the apartment. At the size of the life she has been keeping it at.",
                    "Then the rain takes her back to the room that will not stay one room.");
            }
            else
            {
                await Say("Mara steps back out of the present tense. It keeps very well without her. That is rather the point of it.", 4.5f, 23);
            }

            AudioManager.Play(Sfx.DoorCreak, -8f, 0.95f);
            await WaitSeconds(0.25f);
            SceneManager.ChangeScene(Room5ScenePath);
        }
        finally
        {
            Busy = false;
        }
    }
}

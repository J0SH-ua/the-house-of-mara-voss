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
/// The entry door returns to Room 5 at any time. One practical thing
/// also waits here — or seemed to: the heavy flashlight in the
/// far-left cupboard, bought for power cuts. It has been dead in
/// that cupboard for years, like most of this apartment's claim on
/// her. The light the old house wants is an older light, kept
/// where a father's hands left it.
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
        // The painted cupboard at the far-left edge. Practical, not one
        // of the three silences this room asks Mara to face.
        yield return new Hotspot("UtilityCupboard", "The Cupboard by the Wall",
            new Rect2(0, 448, 112, 108), new Vector2(56, 500),
            Sfx.DrawerOpen, 0.8f, PointerCursorPath, false);
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

        // A cupboard is opened with hands, whatever verb happens to be armed.
        if (hotspot.Id == "UtilityCupboard")
        {
            _ = OpenUtilityCupboardAsync(hotspot);
            return;
        }

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
                    "Mail piled behind my door. Bills. A birthday card I never opened — two years old now. And at the bottom, a postcard from Mum. I know what it says. That's not the same as reading it.",
                    "Every envelope is someone halfway through talking to me. I stopped letting them finish years ago.",
                }, "Unopened isn't the same as unsent. I know that. Knowing isn't the same as opening.");
                break;

            case "SilentPhone":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Other people's phones ring in this building. Through the wall I can hear them being answered. Lives, going on.",
                    "Mine's been quiet so long it started to feel like my choice. I'm not sure any more if I chose the quiet — or it chose me.",
                }, "No messages. I used to call that peace.");
                break;

            case "CornerChair":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "My corner chair. Turned to the wall like a kid being punished. I sit here at the bad hours, knees up, waiting for a morning from twelve years ago to finally be over.",
                    "It's the most honest thing I own. Everything else in here is props for a play about a woman who's fine.",
                }, "The corner of the room. The corner of everything.");
                break;

            case "EntryDoor":
                _ = LeaveApartmentAsync();
                break;
        }
    }

    private async Task OpenUtilityCupboardAsync(Hotspot cupboard)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            AudioManager.Play(Sfx.DrawerOpen, -8f, 0.8f);

            if (gs != null && gs.HasFlag("item_flashlight"))
            {
                await Say("Batteries, a plunger, one brave little toolkit — and my own dead torch, exactly where I left it. Dad's is the one in my bag. It's doing more good there than mine ever did in here.", 4.5f, 24);
                return;
            }

            // The present tense cannot lend her this. Her own torch
            // died years ago like everything else she stopped feeding;
            // the working light belongs to the house, in a father's
            // keeping. (Older saves already carrying a flashlight keep
            // it — nothing is taken from a player, only found sooner.)
            AudioManager.Play(Sfx.FlashlightClick, -7f, 0.8f);
            await WaitSeconds(0.25f);
            if (!GodotObject.IsInstanceValid(this))
                return;
            AudioManager.Play(Sfx.FlashlightClick, -9f, 0.7f);
            bool remembersCabinet = gs?.HasFlag("blackout_remembered_cabinet") ?? false;
            await SaySequence(
                "The cupboard I never open. Cleaning stuff, a plunger — and the big flashlight I bought the winter the power kept going out. It has weight. It does not have anything else.",
                "Dead for years. The batteries have gone the way of everything in this apartment I stopped feeding. Click. Click. Nothing — not even the shame to flicker.",
                remembersCabinet
                    ? "Not this light, then. Dad's. The kitchen cabinet, under the counter, with the candles. His batteries were never, ever dead."
                    : "If I ever needed real light in the dark, this isn't where I'd find it. Dad kept a torch that always worked. Old houses keep their lights where the old hands left them.");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
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

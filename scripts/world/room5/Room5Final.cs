using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Room 5 — the room that will not stay one room.
///
/// Mara's present-day bedroom keeps sliding under the childhood house. The
/// player first examines the contradictions, then learns Anchor's real use:
/// the bedside lamp is the one object with the same meaning in both years.
/// Anchoring it holds the writing desk still long enough to recover the
/// unsent apology (Fragment V), after which the wardrobe opens on Room 6.
/// </summary>
public partial class Room5Final : MemoryRoomBase
{
    private const string AnchoredFlag = "room5_anchored_lamp";
    private const string DrawerOpenedFlag = "room5_drawer_opened";

    private Label _anchorLabel;
    private bool _wrongAnchorLessonShown;

    protected override string RoomName => "Room5";
    protected override string BackgroundPath => "res://assets/images/bgs/Room_5_BG.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Later entries win overlaps, so broad furniture comes before the
        // smaller objects sitting on or inside it.
        yield return new Hotspot("RainWindow", "Rain Window",
            new Rect2(48, 20, 240, 395), new Vector2(168, 210), Sfx.Examine, 1.2f);
        yield return new Hotspot("Bed", "Bed",
            new Rect2(52, 315, 455, 305), new Vector2(275, 475), Sfx.Examine, 0.72f);
        yield return new Hotspot("Lamp", "Bedside Lamp",
            new Rect2(518, 292, 82, 105), new Vector2(559, 345), Sfx.WordChime, 1.08f);
        yield return new Hotspot("Desk", "Writing Desk",
            new Rect2(610, 345, 338, 215), new Vector2(779, 452), Sfx.PageTurn, 0.95f);
        yield return new Hotspot("DeskDrawerFinal", "Desk Drawer",
            new Rect2(786, 388, 124, 62), new Vector2(848, 419),
            Sfx.DrawerOpen, 1f, PointerCursorPath, false);
        yield return new Hotspot("RotaryPhone", "Rotary Phone",
            new Rect2(848, 320, 96, 74), new Vector2(896, 357), Sfx.KeyType, 0.84f);
        yield return new Hotspot("Wardrobe", "Wardrobe",
            new Rect2(946, 78, 198, 492), new Vector2(1045, 324),
            Sfx.Examine, 0.82f, PointerCursorPath);
        yield return new Hotspot("AlarmClock", "Alarm Clock",
            new Rect2(582, 350, 62, 78), new Vector2(613, 389), Sfx.ClockTick, 1f);
        yield return new Hotspot("FloorPhotographs", "Photographs on the Floor",
            new Rect2(938, 542, 112, 72), new Vector2(994, 578), Sfx.PhotoPickup, 1f);
    }

    protected override void OnRoomReady()
    {
        bool anchored = GameState.Instance?.HasFlag(AnchoredFlag) ?? false;
        _anchorLabel = CreateLabel("the lamp stayed lit",
            new Vector2(452, 250), 18,
            new Color(1f, 0.9f, 0.62f, 0.9f), new Vector2(215, 34));
        _anchorLabel.Visible = anchored;
        if (anchored)
            SetBackgroundTint(new Color(0.9f, 0.94f, 1f));

        var gs = GameState.Instance;
        if (gs == null || !gs.IsShardRecovered(4))
        {
            _ = PlayStoryLessonOnceAsync("story_lesson_room5_anchor_room",
                "This is the room Mara lives in now, laid over the room she grew up in until neither year can keep its furniture. Do not try to force it still yet.",
                "First, look normally. Click each held thing once and let it contradict itself; hold Tab if the room hides one, or press H and Mara will nudge you toward it. When every contradiction has been faced, Anchor will have something true to hold.",
                "Then choose Anchor (A, or its Memory-bar button) and click the one thing whose meaning is the same in childhood and adulthood. If you choose wrong, Anchor stays in her hands and the object will show why it slides.");
        }
    }

    protected override async Task OnRequiredExaminesCompletedAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.IsShardRecovered(4) || gs.HasFlag(AnchoredFlag))
            return;

        await SayStoryLessonOnceAsync("story_lesson_room5_all_faced",
            "Now Mara has looked at every contradiction once. Looking again will only fray them; the room is ready to be remembered another way.",
            "Choose Anchor — press A, or click Anchor in the Memory bar. The verb will stay in Mara's hands. Then click the one thing that was left on, in both years, for whoever came home late.");
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        var gs = GameState.Instance;
        var activeVerb = MemoryVerbSystem.Active;

        if (activeVerb == MemoryVerb.Anchor)
        {
            if (hotspot.Id == "Lamp")
                _ = AnchorLampAsync(hotspot);
            else
                _ = WrongAnchorAsync(hotspot);
            return;
        }

        if (activeVerb != MemoryVerb.None)
        {
            _ = WrongVerbAsync(activeVerb, hotspot);
            return;
        }

        if (hotspot.Id == "DeskDrawerFinal")
        {
            _ = OpenDeskDrawerAsync(hotspot);
            return;
        }

        if (hotspot.Id == "Wardrobe" && gs != null && gs.IsShardRecovered(4))
        {
            _ = ExitToGardenAsync();
            return;
        }

        switch (hotspot.Id)
        {
            case "RainWindow":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Rain on glass. Beyond it, apartment windows and a city that was never outside the Eldfen house.",
                    "The childhood walls remember the view wrong because Mara has not opened these curtains in months. The present keeps finding the seams.",
                }, "The city is still there when she refuses to look at it.");
                break;

            case "Bed":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "One side of the bed is slept in. The other has been made so carefully it looks like evidence.",
                    "A child's bed in outline, an adult's insomnia in the sheets. Both Maras have waited here for morning.",
                }, "No one has slept on the untouched side. That is the point of it.");
                break;

            case "Lamp":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The lamp is warm. Not remembered warm — actually warm, left burning through every version of this room.",
                    "Her mother left a light on for whoever came home late. Mara still does. In childhood and adulthood, its meaning never changed sides.",
                }, "Steady, ordinary light. The room cannot decide what year it is around it.");
                break;

            case "Desk":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Paper, envelopes, a pen worn smooth. Every sheet begins with the same careful handwriting and stops before it can become a letter anyone could answer.",
                    "\"I should have been there at four.\" The sentence is everywhere. Only its endings keep failing.",
                }, "The desk has been beginning the same apology for twelve years.");
                break;

            case "RotaryPhone":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The phone is disconnected. Its dial still remembers one number better than all the others: the house where somebody should have called by four.",
                    "No ringing. Only the small mechanical patience of a thing built to carry voices between rooms.",
                }, "The line is silent. It was silent then, too, until it wasn't.");
                break;

            case "Wardrobe":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Adult coats, dark and heavy. Between them hangs a child's yellow raincoat, dry as paper, still small enough to be lifted with one hand.",
                    "Mara kept it where she would see it only when looking for something else. Memory is good at arranging ambushes.",
                }, "The yellow coat does not belong to the woman who owns this room. That is why it is here.");
                break;

            case "AlarmClock":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The alarm clock has stopped at four. Its bell is turned down, as if the hour might still startle somebody.",
                    "Four was not when Ida died. Four was when she stopped expecting Mara to be the person who came.",
                }, "Four o'clock, kept under glass.");
                break;

            case "FloorPhotographs":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The photographs have fallen face down. On their backs, in pencil: kitchen, chairs, jars, hall, garden. Places instead of people.",
                    "Mara can name every room she is avoiding. Naming the person takes longer.",
                }, "Face down is still a way of keeping them.");
                break;
        }
    }

    private async Task AnchorLampAsync(Hotspot lamp)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            bool firstLook = RecordExamine(lamp);
            var gs = GameState.Instance;
            AudioManager.Play(Sfx.WordChime, -6f, 1.12f);

            if (gs != null && gs.HasFlag(AnchoredFlag))
            {
                MemoryVerbSystem.ConsumeActive(MemoryVerb.Anchor);
                await Say("The lamp is already holding. Around it, the room has one year at a time.");
                return;
            }

            gs?.SetFlag(AnchoredFlag);
            MemoryVerbSystem.ConsumeActive(MemoryVerb.Anchor);
            AudioManager.Play(Sfx.ClarifyReveal, -5f, 0.94f);
            Hotspots.SpawnRipple(lamp.Center, new Color(1f, 0.88f, 0.55f));
            SetBackgroundTint(new Color(0.9f, 0.94f, 1f));
            if (_anchorLabel != null)
                _anchorLabel.Visible = true;
            QueueRedraw();

            if (firstLook)
            {
                await SaySequence(
                    "The lamp is warm. Not remembered warm — actually warm, left burning through every version of this room.",
                    "You hold to that. A light for whoever comes home late. The wallpaper stops being two wallpapers. The bed chooses an age. The desk stays a desk.",
                    "Anchor does not make the room true. It gives the truth one still place to stand.");
            }
            else
            {
                await SaySequence(
                    "You hold to the lamp — the one thing that means the same in both years.",
                    "The room settles around its light. For the first time, the writing-desk drawer belongs entirely to the present tense.");
            }

            await Say("Anchor is spent because it worked. While the room holds, use ordinary hands: click the writing-desk drawer.");
        }
        finally
        {
            Busy = false;
            QueueRedraw();
        }
    }

    private async Task WrongAnchorAsync(Hotspot hotspot)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -8f, 0.88f);
            string line = hotspot.Id switch
            {
                "RainWindow" => "The window will not stay. Eldfen fields and city lights keep trading places behind the rain.",
                "Bed" => "The bed changes size and side and sleeper. Nothing that restless can hold the room.",
                "Desk" or "DeskDrawerFinal" => "The desk flickers between homework and apology. Anchor needs the thing around which it can settle.",
                "RotaryPhone" => "The phone belongs to a voice that may or may not have called. Its silence moves.",
                "Wardrobe" => "The wardrobe is all thresholds and other people's sizes. It leads; it does not stay.",
                "AlarmClock" => "The clock says four in one year and no hour anyone survived in the other. It cannot be trusted to hold.",
                "FloorPhotographs" => "Face-down photographs change whenever Mara is ready to see a different one. They are the room sliding, not its still point.",
                _ => "It will not stay. Anchor needs the one thing whose meaning never changed.",
            };
            await Say(line, 4f, 23);
            if (!_wrongAnchorLessonShown)
            {
                _wrongAnchorLessonShown = true;
                await Say("Anchor is still in Mara's hands — a wrong choice did not spend it. Try the thing whose meaning never changed, or right-click to let Anchor go.", 4f, 23);
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task WrongVerbAsync(MemoryVerb verb, Hotspot hotspot)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -9f, 0.95f);
            string line = verb switch
            {
                MemoryVerb.Echo => "Echo finds rain, a clock, and an unfinished sentence all speaking at once. This room needs one thing held still first.",
                MemoryVerb.Trace => "There is no path to trace while the furniture keeps changing years.",
                MemoryVerb.Release => "There is nothing complete here to release. Only an apology still pretending it might be sent.",
                _ => "That way of remembering does not settle this room.",
            };
            await Say(line, 4f, 23);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task OpenDeskDrawerAsync(Hotspot drawer)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs != null && gs.IsShardRecovered(4))
            {
                AudioManager.Play(Sfx.DrawerOpen, -7f);
                await Say("The drawer is empty now except for dust and the pale rectangles where envelopes used to lie.");
                return;
            }

            if (!AllRequiredExamined())
            {
                GetProgress(out int done, out int total);
                AudioManager.Play(Sfx.Denied, -7f);
                await Say($"The drawer sticks between one room and another. Face everything this place is keeping first. ({done}/{total})");
                return;
            }

            if (gs == null || !gs.HasFlag(AnchoredFlag))
            {
                AudioManager.Play(Sfx.DrawerClose, -7f);
                await SaySequence(
                    "For a blink the drawer belongs to a child's desk: blunt pencils, a name tape, nothing addressed to anyone.",
                    "Then it is the adult desk again, and shut. Press A — Anchor — then click the bedside lamp, the light left on in both years for whoever came home late. When the room settles around it, this drawer may stay shut in only one year.");
                return;
            }

            AudioManager.Play(Sfx.DrawerOpen, -5f);
            bool first = gs.CollectShard(4);
            gs.SetFlag(DrawerOpenedFlag);
            gs.MarkRoomCompleted("Room5");
            Hotspots.SpawnRipple(drawer.Center, new Color(1f, 0.88f, 0.55f));

            if (first)
            {
                await SaySequence(
                    "Inside are twelve envelopes, one for every October since Ida died. Each is sealed. None has an address.",
                    "\"I should have been there at four. I remember thinking there would be time to finish what I was doing. I remember choosing it.\"",
                    "The last letter is unfinished. Not because Mara forgave herself — because an unsent apology can still be answered by the house.",
                    "An apology is not only the minute Mara missed. It is that minute and every minute changed by it. Holding one true thing still has taught her how the others join.",
                    "That joining is Trace. In the garden beyond the wardrobe, examine each stop normally first. When all four have been faced, press T — or click Trace — and click the same stops again in the order they caused one another.");
            }
            else
            {
                await Say("The envelopes are gone into memory now. The drawer can simply be a drawer.");
            }
        }
        finally
        {
            Busy = false;
            QueueRedraw();
        }
    }

    private async Task ExitToGardenAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.DoorCreak, -6f);
            await SaySequence(
                "Behind the adult coats there is no back panel. Only October light, smelling of apples and wet leaves.",
                "The yellow raincoat moves once in a wind from a garden Mara has spent twelve years not entering.");
            SceneManager.ChangeScene("res://scenes/world/room_6.tscn");
        }
        finally
        {
            Busy = false;
        }
    }

    protected override void DrawRoom()
    {
        if (GameState.Instance == null || !GameState.Instance.HasFlag(AnchoredFlag))
            return;

        var lamp = GetHotspot("Lamp");
        if (lamp == null)
            return;

        var gold = new Color(1f, 0.88f, 0.55f, 0.82f);
        var faintGold = new Color(1f, 0.88f, 0.55f, 0.22f);
        DrawArc(lamp.Center, 57f, 0f, Mathf.Tau, 48, gold, 2f, true);
        DrawArc(lamp.Center, 41f, 0f, Mathf.Tau, 40, faintGold, 1.4f, true);
        DrawLine(lamp.Center + new Vector2(0, -58), lamp.Center + new Vector2(0, 152),
            faintGold, 2f, true);
    }
}

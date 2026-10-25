using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Room 5 — the room that will not stay one room.
///
/// Mara's present-day bedroom keeps sliding under the childhood house —
/// and it shows. Everything Mara inspects starts to shift in appearance,
/// sliding between its childhood and adult selves (double-exposed, torn,
/// tinted by two years at once). Everything except the bedside lamp, which
/// simply stays itself — and which will not be touched until everything
/// else has been. Reach for it early and Mara stops herself, out loud: the
/// sliding things first, all of them; that is apparently the rule. Only
/// once every shifting thing has been faced can she actually touch it —
/// and in the touching, she notices it herself, the way a person would:
/// "that lamp is the only thing in here that isn't weird." What to do
/// about a still thing stays the player's to put together. Anchoring it
/// settles every shifted object back into one year, holds the writing desk
/// still long enough to recover the unsent apology, and once the letters
/// are Mara's, the inside of the wardrobe — past the coats and the yellow
/// raincoat — stops being a wardrobe and becomes the garden, waiting to be
/// clicked through.
///
/// The room also keeps something practical. Her father's proper lockpick
/// set is earned earlier, in the hallway drawer's keeping; beneath the
/// apology letters here is either that same set (for a Mara who reached
/// this room before she ever saw inside the hall drawer, including older
/// saves) or the finer tension wrench that makes the set kinder in a
/// hard lock. Low in the wall beneath the writing desk is a latched
/// ventilation grille. Together with a light from Mara's present-day
/// apartment, they open a way under the house that the main remembering
/// never required her to find.
///
/// Then the house runs out of light. Once the letters are Mara's and
/// her father's tools are back in her hands, the room's one still
/// thing fails like everything else: the lamp flickers and dies, the
/// garden stops showing through the coats, and the old rooms stand
/// dark behind her. What follows is childhood procedure, remembered
/// with her whole body — the blackout games she and Ida played in
/// the pitch dark, her father's big torch from a kitchen cabinet
/// this house never showed her on the way in, and the one door in
/// the house she cannot picture at all: the basement's. The way
/// down she can find is the way the house breathes.
/// </summary>
public partial class Room5Final : MemoryRoomBase
{
    private const string AnchoredFlag = "room5_anchored_lamp";
    private const string DrawerOpenedFlag = "room5_drawer_opened";
    private const string LampNoticeFlag = "story_monologue_room5_lamp_still";
    private const string LockpickScenePath = "res://scenes/world/lockpick_puzzle.tscn";
    private const string VentCrawlScenePath = "res://scenes/world/vent_crawl.tscn";
    private static readonly Rect2 VentRect = new(812, 438, 76, 52);

    private Label _anchorLabel;
    private Label _gardenLabel;
    private Texture2D _gardenTexture;
    private Tween _wardrobeZoomTween;
    private Tween _wardrobeStepTween;

    /// <summary>Everything that starts sliding once inspected. The lamp is
    /// deliberately absent: it is the only thing in the room that does not
    /// change. Its stillness is the clue — pointed at, eventually, only by
    /// Mara noticing it herself.</summary>
    private static readonly string[] ShiftingIds =
    [
        "RainWindow", "Bed", "Desk", "RotaryPhone", "Wardrobe", "AlarmClock", "FloorPhotographs",
    ];

    /// <summary>Reached for too soon, the lamp is refused — by the room, by
    /// Mara; here the difference is academic. She says why out loud,
    /// plainly, the way a person talks herself out of a door she is not
    /// ready to open. Nothing is recorded, nothing frays: it is not time.</summary>
    private static readonly string[] LampRefusalLines =
    [
        "I reach for it — and stop, like my hand knows something. Not yet. Everything else in here is still sliding. It all has to be looked at first. That's… apparently the rule.",
        "No. Not that one. Not while the rest of the room won't sit still. My hand just… won't.",
        "It stays lit. Warm. Ordinary. And somehow out of reach until I've faced the rest of this room. Everything else first.",
        "Not yet. I don't know why I know that. I just do. The sliding things first. All of them.",
    ];

    /// <summary>The individual face-down photographs painted on the floor
    /// in front of the desk, in 1152×648 screen space. They are one logical
    /// examinable, but each painted photograph gets its own rect so hover,
    /// Tab rings and the double-exposed shift land on photographs rather
    /// than on one broad patch of empty floor.</summary>
    private static readonly Rect2[] FloorPhotographRects =
    [
        // The same six painted centres as before, with a modest pad on
        // every side. MemoryRoomBase now asks GameUI for the live rects
        // of its actual buttons instead of treating the whole lower
        // room as HUD, so each of these can be hovered and clicked on
        // its own; if one print is awkward, another will answer.
        new Rect2(704, 517, 108, 42),
        new Rect2(518, 551, 114, 46),
        new Rect2(784, 552, 108, 46),
        new Rect2(608, 576, 104, 46),
        new Rect2(693, 576, 108, 48),
        new Rect2(718, 616, 126, 32),
    ];

    private int _lampRefusalCount;

    /// <summary>Every thing in this room that slides has been faced at
    /// least once. Only then will the lamp let itself be touched.</summary>
    private bool AllShiftersTouched()
    {
        foreach (var id in ShiftingIds)
        {
            if (!WasExamined(id))
                return false;
        }
        return true;
    }

    /// <summary>The wardrobe's open interior in the Room 5 painting.</summary>
    private static readonly Rect2 WardrobePortalRect = new(968, 118, 152, 428);

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
        // Low in the wall beneath the desk drawer, stopping at the
        // floor line. It is yielded after the broad writing-desk hotspot
        // so the little grille wins their overlap; the painted wall
        // there gets a code-drawn vent in DrawRoom. Utility, not a
        // required examine.
        yield return new Hotspot("LowVent", "Low Vent",
            VentRect, VentRect.GetCenter(), Sfx.Examine, 0.7f,
            PointerCursorPath, false);
        foreach (var photoRect in FloorPhotographRects)
        {
            yield return new Hotspot("FloorPhotographs", "Photographs on the Floor",
                photoRect, photoRect.GetCenter(), Sfx.PhotoPickup, 1f);
        }
    }

    protected override void OnRoomReady()
    {
        bool anchored = GameState.Instance?.HasFlag(AnchoredFlag) ?? false;
        _anchorLabel = CreateLabel(IsBlackoutOn() ? "the lamp, dark" : "the lamp stayed lit",
            new Vector2(452, 250), 18,
            new Color(1f, 0.9f, 0.62f, 0.9f), new Vector2(215, 34));
        _anchorLabel.Visible = anchored;
        if (anchored)
            SetBackgroundTint(new Color(0.9f, 0.94f, 1f));

        // What waits inside the wardrobe once the letters are Mara's.
        _gardenTexture = GD.Load<Texture2D>("res://assets/images/bgs/Room_6_Final.png");
        _gardenLabel = CreateLabel("the garden, through the coats",
            new Vector2(876, 42), 18,
            new Color(1f, 0.9f, 0.62f, 0.92f), new Vector2(268, 34));
        _gardenLabel.Visible = (GameState.Instance?.IsShardRecovered(4) ?? false) && !IsBlackoutOn();

        var gs = GameState.Instance;
        if (gs == null || !gs.IsShardRecovered(4))
        {
            _ = PlayStoryLessonOnceAsync("story_lesson_room5_anchor_room",
                "This is the room Mara lives in now, laid over the room she grew up in, until neither year can keep its furniture.");
        }

        // The blackout can already be due the moment Mara walks back in
        // (an older save that took the letters and the tools before the
        // house learned to go dark), and her errands resume where they
        // stopped: power to greet, or a vent still to notice by torchlight.
        if (gs != null)
        {
            MaybeBeginBlackout();
            if (gs.HasFlag("backup_power_on") && !gs.HasFlag("room5_power_restored_seen") &&
                gs.HasFlag("blackout_began"))
                _ = PowerRestoredBeatAsync();
            else if (IsBlackoutOn() && gs.HasFlag("item_flashlight") &&
                !gs.HasFlag("blackout_vent_noticed"))
                _ = AutoNoticeVentAsync();
        }
    }

    protected override async Task OnRequiredExaminesCompletedAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.IsShardRecovered(4) || gs.HasFlag(AnchoredFlag))
            return;

        await SayStoryLessonOnceAsync("story_monologue_room5_two_years",
            "Before anything is held still, hear what this room has been saying in two voices. The girl who slept in this bed believed her big sister could do anything. The woman who cannot sleep in it has spent twelve years being sorry in her direction.",
            "Mara left Eldfen at twenty-two and built a careful, quiet life she shows almost no one. But every night she comes back here to wait — the way Ida waited, in a garden, for someone who does not come. She has become the one on the swing. She only counts in years instead of minutes.");
        await SayStoryLessonOnceAsync("story_lesson_room5_all_faced",
            "Now Mara has looked at every contradiction once. The room is still sliding — but she has heard all of it now.");
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        var gs = GameState.Instance;
        var activeVerb = MemoryVerbSystem.Active;

        // The lamp will not be touched — looked at closely or Anchored —
        // until everything that slides has been faced. The room has an
        // order, and the still thing comes last.
        if (hotspot.Id == "Lamp" && !(gs?.HasFlag(AnchoredFlag) ?? false) && !AllShiftersTouched())
        {
            _ = LampNotYetAsync(hotspot);
            return;
        }

        // In a blackout the still thing is only a dead thing. Handle it
        // before everything else so no verb goes looking for light here.
        if (hotspot.Id == "Lamp" && IsBlackoutOn())
        {
            _ = BlackoutLampAsync(hotspot);
            return;
        }

        // The vent is a practical thing, not a memory to address with a
        // verb. Handle it before verb routing so it stays reachable even
        // if a way of remembering happens to be armed.
        if (hotspot.Id == "LowVent")
        {
            _ = UseVentAsync(hotspot);
            return;
        }

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
            // No light, no garden. The October behind the coats went out
            // with the rest of the house and will not be walked into blind.
            if (IsBlackoutOn())
                _ = BlackoutWardrobeAsync(hotspot);
            else
                _ = ExitToGardenAsync();
            return;
        }

        // Once the room holds still in the present tense, the rain window
        // stops being a view and becomes a threshold: her own building,
        // her own window, the life that continued.
        if (hotspot.Id == "RainWindow" && gs != null && gs.HasFlag(AnchoredFlag)
            && WasExamined("RainWindow"))
        {
            _ = EnterApartmentAsync();
            return;
        }

        switch (hotspot.Id)
        {
            case "RainWindow":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "Rain on the glass. Past it — apartment windows. A city. There was never a city outside our house in Eldfen.",
                    "I haven't opened my real curtains in months. That's why this wall gets the view wrong. The present keeps coming through the cracks.",
                }, "The city's still there, even when I won't look at it.");
                break;

            case "Bed":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "One side of the bed is slept in. The other's made up so neat it looks like evidence.",
                    "A kid's bed, with a grown woman's not-sleeping in the sheets. Both of me have waited here for morning.",
                }, "Nobody sleeps on the neat side. That's the point of it.");
                break;

            case "Lamp":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "It's warm. Not remember-it warm. Actually warm. It's been left on in every version of this room.",
                    "Mum left a light on for whoever came home late. I still do.",
                    "Wait. Everything else I've touched in here started sliding — one year, then the other. This hasn't. That lamp is the only thing in here that isn't weird. It doesn't slide. It doesn't change. It just… stays.",
                }, "Steady light. Ordinary. The room can't make up its mind around it.");
                break;

            case "Desk":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "Paper. Envelopes. A pen worn smooth. Every sheet starts in my best handwriting — and stops before it turns into a letter anyone could answer.",
                    "\"I should have been there at four.\" The sentence is everywhere. Only its endings keep failing.",
                }, "This desk has been starting the same apology for twelve years.");
                break;

            case "RotaryPhone":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "It's not even plugged in. But the dial still knows one number best. The house. Where somebody should have called by four.",
                    "No ringing. Just sitting there, patient. A thing made to carry voices from room to room.",
                }, "The line is silent. It was silent then, too, until it wasn't.");
                break;

            case "Wardrobe":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "My coats. Dark, heavy, grown-up. And between them — a kid's yellow raincoat. Dry as paper. Still small enough to lift with one hand.",
                    "I kept it where I'd only see it when I was looking for something else. Memory's good at setting traps like that.",
                }, "The yellow coat isn't mine. That's exactly why it's here.");
                break;

            case "AlarmClock":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "The clock stopped at four. Even its bell is turned down — like the hour might still scare somebody.",
                    "Four isn't when she died. Four is when she stopped expecting me to be the one who came.",
                }, "Four o'clock, kept under glass.");
                break;

            case "FloorPhotographs":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "Photos, all face down. Pencil on the backs: kitchen, chairs, jars, hall, garden. Places. Not people.",
                    "I can name every room I'm avoiding. Naming the person takes longer.",
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
                var lines = new List<string>
                {
                    "It's warm. Not remember-it warm. Actually warm. It's been left on in every version of this room.",
                };
                // If she never examined it first, the noticing still happens
                // — here, in the touching, the way the room insisted.
                if (gs != null && !gs.HasFlag(LampNoticeFlag))
                {
                    gs.SetFlag(LampNoticeFlag);
                    lines.Add("Everything else I touched in here started sliding — one year, then the other. This never did. That lamp is the only thing in here that isn't weird. It just… stays.");
                }
                lines.Add("You hold to that. A light for whoever comes home late. The wallpaper stops being two wallpapers. The bed chooses an age. The desk stays a desk.");
                lines.Add("Anchor does not make the room true. It gives the truth one still place to stand.");
                await SaySequence(lines.ToArray());
            }
            else
            {
                await SaySequence(
                    "You hold to the lamp — the one thing that means the same in both years.",
                    "The room settles around its light. For the first time, the writing-desk drawer belongs entirely to the present tense.");
            }

            await Say("While the room holds, it holds in the present tense. Through the rain window, the city has stopped pretending to be fields.");
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
                _ => "It will not stay.",
            };
            await Say(line, 4f, 23);
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
                // Saves that took the letters before the picks existed can
                // still find what lay underneath them.
                await TakeLockpicksAsync(drawer, foundUnderLetters: false);
                MaybeBeginBlackout();
                return;
            }

            if (!AllRequiredExamined())
            {
                AudioManager.Play(Sfx.Denied, -7f);
                await Say("The drawer sticks between one room and another.");
                return;
            }

            if (gs == null || !gs.HasFlag(AnchoredFlag))
            {
                AudioManager.Play(Sfx.DrawerClose, -7f);
                await SaySequence(
                    "For a blink the drawer belongs to a child's desk: blunt pencils, a name tape, nothing addressed to anyone.",
                    "Then it is the adult desk again, and shut. Nothing in this room will stay one year long enough to open.");
                return;
            }

            AudioManager.Play(Sfx.DrawerOpen, -5f);
            bool first = gs.CollectShard(4);
            gs.SetFlag(DrawerOpenedFlag);
            gs.MarkRoomCompleted("Room5");
            Hotspots.SpawnRipple(drawer.Center, new Color(1f, 0.88f, 0.55f));

            // The letters are Mara's — and across the room, past the coats
            // and the yellow raincoat, the wardrobe's inside is a garden now.
            if (first)
            {
                if (_gardenLabel != null)
                    _gardenLabel.Visible = true;
                var wardrobe = GetHotspot("Wardrobe");
                if (wardrobe != null)
                    Hotspots.SpawnRipple(wardrobe.Center, new Color(1f, 0.88f, 0.55f));
                AudioManager.Play(Sfx.DoorCreak, -13f, 0.7f);
                QueueRedraw();
            }

            if (first)
            {
                await SaySequence(
                    "Inside are twelve envelopes, one for every October since Ida died. Each is sealed. None has an address.",
                    "\"I should have been there at four. I remember thinking there would be time to finish what I was doing. I remember choosing it.\"",
                    "The last letter is unfinished. Not because Mara forgave herself — because an unsent apology can still be answered by the house.",
                    "An apology is not only the minute Mara missed. It is that minute and every minute changed by it. Holding one true thing still has taught her how the others join.",
                    "That joining has a name now. Trace.");
                await TakeLockpicksAsync(drawer, foundUnderLetters: true);
                // Letters in one hand, her father's tools in the other.
                // The house chooses this exact moment to lose its light.
                MaybeBeginBlackout();
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

    /// <summary>The practical thing under the apology. The main road now
    /// earns Father's oilcloth roll in Room 4's hall drawer, so this desk
    /// has two honest jobs: it is still the fallback grant for a save (old
    /// or new) that somehow reached the letters without the set, and for
    /// everyone who already carries the set it holds the one tool the
    /// roll was missing — a finer tension wrench for Mara's hands.</summary>
    private async Task TakeLockpicksAsync(Hotspot drawer, bool foundUnderLetters)
    {
        var gs = GameState.Instance;
        if (gs == null)
            return;

        bool needsSet = !gs.HasFlag("item_lockpick_set");
        bool needsFineWrench = !needsSet && !gs.HasFlag("item_fine_tension_wrench");
        if (!needsSet && !needsFineWrench)
            return;

        AudioManager.Play(Sfx.PhotoPickup, -9f, 0.92f);
        AudioManager.Play(Sfx.KeyType, -13f, 1.25f);
        if (needsSet)
        {
            if (foundUnderLetters)
            {
                await SaySequence(
                    "Under the last envelope is an oilcloth roll, tied twice. My father's handwriting on the cloth: MARA DO NOT — and then, in smaller letters, — lose these.",
                    "Lockpicks. A whole roll of them, oiled and clean. I watched him open our neighbours' doors with these when they locked themselves out, one knee on the step, telling me to listen for the little sounds.");
            }
            else
            {
                await SaySequence(
                    "One pale rectangle is longer than an envelope. At the back of the drawer, an oilcloth roll is wedged where the letters used to hide it.",
                    "My father's lockpicks. Tied twice, the way he tied everything. I held the lamp for him often enough to know what each one is for.");
            }
            if (!GodotObject.IsInstanceValid(this))
                return;

            gs.SetFlag("item_lockpick_set", save: false);
            gs.UnlockClue("father_lockpicks");
            Hotspots.SpawnRipple(drawer.Center, new Color(1f, 0.85f, 0.5f));
            gs.Toast("Father's lockpick set — kept in Mara's bag (B). The Journal (J) remembers how he taught her to listen.");
            return;
        }

        if (foundUnderLetters)
        {
            await SaySequence(
                "Under the last envelope, wrapped in tissue gone soft as cloth, is one flat strip of spring steel — thinner than anything in Dad's roll. His pencil on the tissue: FOR MARA HANDS.",
                "A fine tension wrench. He must have filed it down the summer he started letting me hold more than the lamp. The picks will tell the truth a little sooner with this under them — and forgive a little more when my wrist gets eager.");
        }
        else
        {
            await SaySequence(
                "One pale rectangle is longer than an envelope. At the back of the drawer, tissue gone soft as cloth wraps a single flat strip of spring steel.",
                "FOR MARA HANDS, in Dad's pencil. A fine tension wrench, filed down for mine. It belongs in the roll with the rest of his patience.");
        }
        if (!GodotObject.IsInstanceValid(this))
            return;

        gs.SetFlag("item_fine_tension_wrench", save: false);
        gs.UnlockClue("fine_tension_wrench");
        Hotspots.SpawnRipple(drawer.Center, new Color(1f, 0.85f, 0.5f));
        gs.Toast("Fine tension wrench — added to Father's lockpick set in Mara's bag (B). Deep pins will read a touch clearer, and binds will bite a touch less.");
    }

    /// <summary>The low vent: a latched grille, a proper tool earned in
    /// another room, and a dark that also wants the apartment's
    /// flashlight.</summary>
    private async Task UseVentAsync(Hotspot vent)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs == null)
                return;

            bool firstFound = gs.SetFlag("room5_vent_found", save: false);
            if (firstFound)
                gs.UnlockClue("under_house_vent");

            if (!gs.HasFlag(AnchoredFlag))
            {
                AudioManager.Play(Sfx.Denied, -10f, 0.72f);
                await Say(firstFound
                    ? "Low in the wall — a vent grille. Only, half the time the wall there is my childhood one and the grille is somewhere else entirely. Cold air, though. Stone-cold. That part doesn't slide."
                    : "The grille won't stay on one wall long enough to work at. Not while the room is still two rooms.", 4.5f, 24);
                return;
            }

            // During the blackout, the vent is found twice: once by a
            // hand that cannot see it, and once by torchlight, properly.
            // Only the second finding becomes a way down.
            if (IsBlackoutOn() && !gs.HasFlag("blackout_vent_noticed"))
            {
                if (!gs.HasFlag("item_flashlight"))
                {
                    AudioManager.Play(Sfx.Denied, -10f, 0.7f);
                    await Say("Low in the dark my hand finds cold slats — and stops. I can't work a latch I cannot see. Light first: the kitchen, the cabinet under the counter. The kitchen provides.", 4.5f, 24);
                }
                else
                {
                    await NoticeVentBeatAsync();
                }
                return;
            }

            if (gs.HasFlag("room5_vent_open"))
            {
                if (!gs.HasFlag("item_flashlight"))
                {
                    AudioManager.Play(Sfx.Denied, -9f, 0.8f);
                    await Say("The duct beyond the grille is dark all the way down. I am not crawling into the walls of this house blind. Dad kept a flashlight in the kitchen — the cabinet under the counter. That's the light this dark wants.", 5f, 24);
                    return;
                }

                AudioManager.Play(Sfx.FlashlightClick, -6f);
                await Say("Flashlight on. Knees, elbows, and a duct built by somebody who had never met a grown woman. Deep breath.", 4.5f, 24);
                if (GodotObject.IsInstanceValid(this))
                    SceneManager.ChangeScene(VentCrawlScenePath);
                return;
            }

            if (!gs.HasFlag("item_lockpick_set"))
            {
                AudioManager.Play(Sfx.DrawerClose, -7f, 0.65f);
                await SaySequence(
                    "A vent grille, low in the baseboard. No screws — a little latch, the cheap kind, meant to be worked from inside by someone who knew locks.",
                    "My father fitted latches like this. Two slender tools and patience would do it. Fingernails won't.");
                return;
            }

            await Say("The latch is my father's work. His tools are in my bag. All right, Dad. Let's see if I was listening.", 4.5f, 24);
            if (!GodotObject.IsInstanceValid(this))
                return;
            gs.SetFlag("lockpick_target_vent", save: false);
            gs.SetFlag("lockpick_origin_room5", save: false);
            gs.ClearFlag("lockpick_target_trunk", save: false);
            gs.ClearFlag("lockpick_origin_basement", save: false);
            gs.SaveGame("lockpick vent armed");
            SceneManager.ChangeScene(LockpickScenePath);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    // ----------------------------------------------------------------
    // The blackout. Letters read, her father's tools in hand — and the
    // house loses its light the way it used to when weather got into
    // the wires. Everything after is childhood procedure.
    // ----------------------------------------------------------------

    private static bool IsBlackoutOn() =>
        GameState.Instance?.HasFlag("blackout_active") ?? false;

    private void MaybeBeginBlackout()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.HasFlag("blackout_began") || gs.HasFlag("backup_power_on"))
            return;
        if (!gs.IsShardRecovered(4) || !gs.HasFlag("item_lockpick_set"))
            return;
        if (gs.IsShardRecovered(5))
            return;
        _ = BeginBlackoutAsync();
    }

    private async Task WaitForHandsFreeAsync()
    {
        while ((Busy || Dialogue.IsShowing) &&
            GodotObject.IsInstanceValid(this) && IsInsideTree())
            await WaitSeconds(0.25f);
    }

    private async Task BeginBlackoutAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.HasFlag("blackout_began"))
            return;

        await WaitForHandsFreeAsync();
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;
        if (gs.HasFlag("blackout_began"))
            return;

        Busy = true;
        try
        {
            // The lamp does not simply go out. Nothing she loves ever
            // simply went out: it argues first.
            gs.SetFlag("blackout_began", save: false);
            gs.SetFlag("blackout_flickering", save: false);
            for (int i = 0; i < 5; i++)
            {
                AudioManager.Play(Sfx.UiHover, -16f, 1.7f);
                await WaitSeconds((float)GD.RandfRange(0.09, 0.30));
                if (!GodotObject.IsInstanceValid(this))
                    return;
            }
            AudioManager.Play(Sfx.LockOpen, -11f, 0.42f);
            gs.ClearFlag("blackout_flickering", save: false);
            gs.SetFlag("blackout_active", save: false);
            AudioManager.Instance?.PlayCutsceneAmbience(
                AudioManager.AmbienceRoomToneLoopPath, -22f);
            if (_gardenLabel != null)
                _gardenLabel.Visible = false;
            if (_anchorLabel != null)
                _anchorLabel.Text = "the lamp, dark";
            QueueRedraw();

            await WaitSeconds(0.5f);
            if (!GodotObject.IsInstanceValid(this))
                return;

            await SaySequence(
                "—and there goes the light. The lamp stuttered, argued, and gave it up. Dark. The whole house at once: the hall, the stairs, every little sound a house makes thinking nobody's listening. All switched off together.",
                "The wardrobe's stopped being a garden, too. Coats again. Just coats, hanging in the black. Whatever was waiting past them can wait in the dark like everything else.",
                "I'm not scared. That's the strange part. We LOVED a blackout, Ida and me. Sneaking room to room in the pitch dark, scaring each other half to death, shrieking fit to get told off in two postcodes. The dark was a game with strict rules. Rule one: the shrieker loses.",
                "Then Dad would come through with the big torch from the kitchen — the cabinet under the counter, where the spare candles lived and the batteries were never, ever dead. \"Right, you two. Ghosts to bed. The kitchen provides.\"",
                "Fine. The kitchen provides. Even now. Even here.");
            if (!GodotObject.IsInstanceValid(this))
                return;

            gs.SetFlag("blackout_remembered_cabinet", save: false);
            gs.UnlockClue("blackout_games");
            gs.Toast("The house has lost power. The kitchen provides: a flashlight, from a cabinet Mara hasn't remembered in years (J). Mind the dark — and the way back is the top of the screen.");
            gs.SaveGame("blackout began");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    private async Task BlackoutLampAsync(Hotspot lamp)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -11f, 0.7f);
            await SaySequence(
                "Dead. Still warm at the neck, but dead. The one thing that never changed in this room — and the dark just changed it like it was nothing.",
                "It's not the bulb. It's everything. The whole house, holding its breath. Light has to be fetched the old way now.");
            GameState.Instance?.Toast("The kitchen cabinet. Flashlight. Batteries that were never dead.");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    private async Task BlackoutWardrobeAsync(Hotspot wardrobe)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Examine, -9f, 0.75f);
            await SaySequence(
                "Coats. Just coats, in the black. My hand finds the back panel where October was.",
                "The garden isn't gone. It's dark. There's a difference, and the difference is power. Dad always brought the light back from underneath the house — every blackout we ever had ended with a switch thrown below.");
            GameState.Instance?.Toast("The garden waits for the light. Backup power sleeps under the house.");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    private async Task AutoNoticeVentAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.HasFlag("blackout_vent_noticed"))
            return;
        await WaitForHandsFreeAsync();
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !IsBlackoutOn())
            return;
        if (gs.HasFlag("blackout_vent_noticed") || !gs.HasFlag("item_flashlight"))
            return;

        Busy = true;
        try
        {
            await NoticeVentBeatAsync();
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    /// <summary>Torchlight back in the room — and the one door Mara
    /// cannot picture. The migraine is the memory refusing; the vent
    /// is the house offering its other mouth. Caller owns Busy.</summary>
    private async Task NoticeVentBeatAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.HasFlag("blackout_vent_noticed"))
            return;

        AudioManager.Play(Sfx.FlashlightClick, -7f);
        await SaySequence(
            "Light. Look at that. The whole room back, in one small honest circle. Dad's torch, and the batteries are fine. Of course they are. In this house they were never, ever dead.",
            "Now. The basement. When the power went, Dad went down and threw the emergency switch — backup power, for exactly this. So: basement door. Basement door, Mara. It's... it's...");
        if (!GodotObject.IsInstanceValid(this))
            return;

        AudioManager.Play(Sfx.ScreenCrack2, -11f, 0.8f);
        await SaySequence(
            "—ow. There it is. Right behind the eye. The door won't come. I know this house has a basement the way I know my own name, and I cannot picture its door. Whatever I buried down the years, I buried the way down with it.",
            "Slow down, then. Breathe. Light low, the way he carried it looking for the draught... wait. There. Low in the wall, under the desk. A vent — and cold coming off it in a thin, steady tongue.",
            "The vents. The whole house breathes through them, and every duct runs down to one cold heart. The basement doesn't need its door remembered. It has other mouths. And one of them has a latch my father fitted.");
        if (!GodotObject.IsInstanceValid(this))
            return;

        gs.SetFlag("blackout_vent_noticed", save: false);
        gs.UnlockClue("vent_route");
        gs.Toast("The low vent. Her father's latch, her father's tools (J).");
        gs.SaveGame("vent noticed in blackout");
        Hotspots.SpawnRipple(VentRect.GetCenter(), new Color(1f, 0.85f, 0.5f));
    }

    private async Task PowerRestoredBeatAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.HasFlag("room5_power_restored_seen"))
            return;
        await WaitForHandsFreeAsync();
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
            return;
        if (gs.HasFlag("room5_power_restored_seen"))
            return;

        Busy = true;
        try
        {
            gs.SetFlag("room5_power_restored_seen", save: false);
            AudioManager.Play(Sfx.WordChime, -11f, 0.95f);
            if (_anchorLabel != null)
                _anchorLabel.Text = "the lamp stayed lit";
            await SaySequence(
                "The lamp comes back the way it left — without being asked. Warm. Lit. The one true thing, true again. Somewhere underneath me, a switch I threw is holding.",
                "And past the coats — light that isn't electric. October. The garden kept its side of the bargain. It only ever needed the house lit to be findable.");
            gs.SaveGame("room5 power restored greeted");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    private async Task EnterApartmentAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.ClarifyReveal, -9f, 0.85f);
            await SaySequence(
                "Through the rain, the city resolves — her building, her window, the present tense, kept at arm's length for months.",
                "Mara steps closer to her own life than the house usually allows.");
            SceneManager.ChangeScene("res://scenes/world/apartment_now.tscn");
        }
        finally
        {
            Busy = false;
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
            if (!GodotObject.IsInstanceValid(this))
                return;
            await ZoomIntoWardrobeAsync();
            if (!GodotObject.IsInstanceValid(this))
                return;
            SceneManager.ChangeScene("res://scenes/world/room_6.tscn");
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Do not cut from the wardrobe's mouth to the garden. Mara
    /// crosses her own floor and goes through it: the whole authored room
    /// (painting, portal, coats and labels together) leans toward the open
    /// wardrobe until October fills the doorway, and only then does Room 6
    /// take over.</summary>
    private async Task ZoomIntoWardrobeAsync()
    {
        Vector2 screenCenter = new(576f, 324f);
        Vector2 focus = WardrobePortalRect.GetCenter();
        Position = Vector2.Zero;
        Scale = Vector2.One;

        if (Settings.ReduceMotionOrDefault())
        {
            const float gentleScale = 1.25f;
            Scale = Vector2.One * gentleScale;
            Position = screenCenter - focus * gentleScale;
            QueueRedraw();
            await WaitSeconds(0.35f);
            return;
        }

        _wardrobeZoomTween?.Kill();
        _wardrobeStepTween?.Kill();

        // Three soft steps from the rug toward the boards while the room
        // moves; the garden itself takes over the soundscape on arrival.
        AudioManager.PlayVaried(Sfx.FootstepCarpetA, -11.5f, 0.95f, 0.03f);
        _wardrobeStepTween = CreateTween();
        _wardrobeStepTween.TweenInterval(0.62f);
        _wardrobeStepTween.TweenCallback(Callable.From(
            () => AudioManager.PlayVaried(Sfx.FootstepCarpetB, -11f, 1f, 0.03f)));
        _wardrobeStepTween.TweenInterval(0.62f);
        _wardrobeStepTween.TweenCallback(Callable.From(
            () => AudioManager.PlayVaried(Sfx.FootstepCarpetA, -10.5f, 1.05f, 0.03f)));
        _wardrobeStepTween.TweenInterval(0.62f);
        _wardrobeStepTween.TweenCallback(Callable.From(
            () => AudioManager.PlayVaried(Sfx.FootstepCarpetB, -10f, 1.08f, 0.03f)));

        const float finalScale = 4.6f;
        Vector2 finalPosition = screenCenter - focus * finalScale;
        _wardrobeZoomTween = CreateTween();
        _wardrobeZoomTween.SetParallel(true);
        _wardrobeZoomTween.TweenProperty(this, "scale", Vector2.One * finalScale, 2.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        _wardrobeZoomTween.TweenProperty(this, "position", finalPosition, 2.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        await ToSignal(_wardrobeZoomTween, Tween.SignalName.Finished);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        var gs = GameState.Instance;
        if (gs == null)
            return;

        if (_gardenLabel != null && gs.IsShardRecovered(4) && !_gardenLabel.Visible && !IsBlackoutOn())
            _gardenLabel.Visible = true;

        if (Settings.ReduceMotionOrDefault())
            return;

        // The sliding and the garden light are alive; keep redrawing while
        // either is on screen.
        if (gs.IsShardRecovered(4))
        {
            QueueRedraw();
            return;
        }
        if (!gs.HasFlag(AnchoredFlag))
        {
            foreach (var id in ShiftingIds)
            {
                if (WasExamined(id))
                {
                    QueueRedraw();
                    return;
                }
            }
        }
    }

    /// <summary>Ordinary examine, plus the room's answer: whatever Mara has
    /// just inspected starts sliding between years — except the lamp.</summary>
    private async Task ExamineShiftingAsync(Hotspot hotspot, string[] firstLines, string repeatLine)
    {
        bool wasExamined = WasExamined(hotspot.Id);
        await ExamineObjectAsync(hotspot, firstLines, repeatLine);
        if (!GodotObject.IsInstanceValid(this) || wasExamined)
            return;

        var gs = GameState.Instance;
        if (hotspot.Id != "Lamp" && gs != null && !gs.HasFlag(AnchoredFlag))
        {
            AudioManager.Play(Sfx.CorruptionGlitch, -9f, 0.85f);
            // The photographs are one memory scattered across several
            // painted pieces; when they begin to slide, every piece answers.
            var shiftingHotspots = GetHotspots(hotspot.Id);
            if (shiftingHotspots.Count == 0)
                shiftingHotspots.Add(hotspot);
            foreach (var shiftingHotspot in shiftingHotspots)
                Hotspots.SpawnRipple(shiftingHotspot.Center, new Color(0.65f, 0.8f, 1f));
            QueueRedraw();
        }

        if (hotspot.Id == "Lamp")
        {
            // The noticing lives in the lamp's own first lines now; this
            // only remembers that she has said it.
            gs?.SetFlag(LampNoticeFlag);
        }
    }

    /// <summary>The lamp, reached for before its time. Mara stops her own
    /// hand and says so; the room keeps its order.</summary>
    private async Task LampNotYetAsync(Hotspot lamp)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -10f, 0.94f);
            Hotspots.SpawnRipple(lamp.Center, new Color(0.75f, 0.72f, 0.68f));
            string line = LampRefusalLines[_lampRefusalCount % LampRefusalLines.Length];
            _lampRefusalCount++;
            await Say(line, 4.5f, 24);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private Rect2 SourceRectFor(Rect2 screenRect, Texture2D texture)
    {
        Vector2 scale = texture.GetSize() / new Vector2(1152f, 648f);
        return new Rect2(screenRect.Position * scale, screenRect.Size * scale);
    }

    /// <summary>One inspected thing, unable to stay one thing: its piece of
    /// the painting redrawn double-exposed — a warm childhood copy and a
    /// cold adult copy sliding against each other, the middle band torn.
    /// Small kept objects (the phone, the clock) also ghost mirror-flipped.</summary>
    private void DrawShifted(Hotspot hotspot, float phase)
    {
        var texture = BackgroundTexture;
        if (texture == null || hotspot == null)
            return;

        Rect2 rect = hotspot.Rect;
        Rect2 src = SourceRectFor(rect, texture);
        Vector2 texel = texture.GetSize() / new Vector2(1152f, 648f);

        bool still = Settings.ReduceMotionOrDefault();
        float t = still
            ? 0.5f
            : 0.5f + 0.5f * (float)System.Math.Sin(Time.GetTicksMsec() / 640.0 + phase);

        // Every few seconds the room hiccups: the two years jump hard apart
        // for a blink before settling back into the slow slide. Between the
        // travel, the colour split and the doubled outline, a shifted thing
        // should be impossible to miss — that is the point of it.
        float hiccup = 0f;
        if (!still)
        {
            double cycle = Time.GetTicksMsec() / 4300.0 + phase;
            if (cycle - System.Math.Floor(cycle) < 0.055)
                hiccup = 9f;
        }
        float slide = still ? 8f : Mathf.Lerp(-9f, 9f, t) + hiccup;
        float drop = slide * 0.35f;

        var warm = new Color(1.34f, 0.94f, 0.56f, 0.52f + 0.34f * t);
        var cold = new Color(0.48f, 0.72f, 1.38f, 0.86f - 0.34f * t);

        // Childhood copy, slid one way and sitting slightly higher.
        DrawTextureRectRegion(texture, rect,
            new Rect2(src.Position + new Vector2(-slide * texel.X, -drop * texel.Y), src.Size), warm);

        // Adult copy, slid the other, in three bands so the middle tears.
        float bandH = rect.Size.Y / 3f;
        for (int band = 0; band < 3; band++)
        {
            float extra = band == 1 ? 12f + hiccup : 0f;
            var dest = new Rect2(rect.Position + new Vector2(0f, band * bandH),
                new Vector2(rect.Size.X, bandH + 1f));
            var bandSrc = new Rect2(
                src.Position + new Vector2((slide + extra) * texel.X, (band * bandH + drop) * texel.Y),
                new Vector2(src.Size.X, src.Size.Y / 3f));
            DrawTextureRectRegion(texture, dest, bandSrc, cold);
        }

        if (hotspot.Id is "RotaryPhone" or "AlarmClock")
        {
            // Mirror-flipped ghost. This Godot version's C# binding for
            // DrawTextureRectRegion takes no flip flags, so mirror the draw
            // transform around the rect's centre instead, then restore it.
            Vector2 centre = rect.GetCenter();
            DrawSetTransform(centre, 0f, new Vector2(-1f, 1f));
            DrawTextureRectRegion(texture, new Rect2(-rect.Size / 2f, rect.Size), src,
                new Color(1f, 1f, 1f, 0.3f + 0.22f * t));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        // Both silhouettes at once: the shape of the thing in two places,
        // warm where it was, cold where it is, never agreeing.
        DrawRect(new Rect2(rect.Position + new Vector2(-slide, -drop), rect.Size),
            new Color(1f, 0.78f, 0.46f, 0.2f + 0.18f * t), false, 1.5f);
        DrawRect(new Rect2(rect.Position + new Vector2(slide, drop), rect.Size),
            new Color(0.6f, 0.8f, 1f, 0.38f - 0.18f * t), false, 1.5f);
        DrawRect(rect, new Color(0.75f, 0.85f, 1f, 0.28f + 0.22f * t), false, 2f);
    }

    /// <summary>After the letters: the wardrobe's inside is October. The
    /// garden (Room 6's own painting) shows through the open doors, with
    /// the coats and Ida's yellow raincoat still hanging in front of it,
    /// and its light spilling onto Mara's floor.</summary>
    private void DrawWardrobePortal()
    {
        if (_gardenTexture == null)
            return;

        float pulse = Settings.ReduceMotionOrDefault()
            ? 0.5f
            : 0.5f + 0.5f * (float)System.Math.Sin(Time.GetTicksMsec() / 620.0);

        Vector2 gardenSize = _gardenTexture.GetSize();
        float srcWidth = gardenSize.Y * (WardrobePortalRect.Size.X / WardrobePortalRect.Size.Y);
        var gardenSrc = new Rect2((gardenSize.X - srcWidth) / 2f, 0f, srcWidth, gardenSize.Y);
        DrawTextureRectRegion(_gardenTexture, WardrobePortalRect, gardenSrc,
            new Color(1.06f, 1f, 0.9f, 1f));

        // The wardrobe's own contents hang in front of the view: coats along
        // the top, and the yellow raincoat down the right-hand side.
        var background = BackgroundTexture;
        if (background != null)
        {
            var coats = new Rect2(968, 118, 152, 104);
            DrawTextureRectRegion(background, coats, SourceRectFor(coats, background),
                new Color(1f, 1f, 1f, 0.92f));
            var raincoat = new Rect2(1070, 268, 54, 196);
            DrawTextureRectRegion(background, raincoat, SourceRectFor(raincoat, background),
                Colors.White);
        }

        Vector2[] spill =
        [
            new Vector2(984, 546), new Vector2(1106, 546),
            new Vector2(1146, 632), new Vector2(944, 632),
        ];
        DrawColoredPolygon(spill, new Color(1f, 0.85f, 0.5f, 0.07f + 0.05f * pulse));

        DrawRect(WardrobePortalRect, new Color(1f, 0.88f, 0.55f, 0.3f + 0.3f * pulse), false, 2f);
        DrawRect(WardrobePortalRect.Grow(-6f), new Color(1f, 0.88f, 0.55f, 0.14f), false, 1f);
    }

    /// <summary>The vent was not in the Room 5 painting, so it is drawn
    /// onto the baseboard in chunky bands at hotspot scale. Closed, it is
    /// a slatted grille with a brass latch; once picked, the duct mouth
    /// goes dark and the grille leans against the wall beside it.</summary>
    private void DrawVent()
    {
        bool open = GameState.Instance?.HasFlag("room5_vent_open") ?? false;
        var outer = VentRect;
        var inner = outer.Grow(-5f);

        DrawRect(outer, new Color(0.09f, 0.075f, 0.06f, 0.96f), true);
        DrawRect(outer, new Color(0.42f, 0.36f, 0.27f, 0.9f), false, 2f);

        if (open)
        {
            DrawRect(inner, new Color(0.015f, 0.015f, 0.02f, 1f), true);
            // A faint cold breath from below — two pale chips, no more.
            DrawRect(new Rect2(inner.Position + new Vector2(9, 12), new Vector2(16, 3)),
                new Color(0.45f, 0.55f, 0.62f, 0.35f), true);
            DrawRect(new Rect2(inner.Position + new Vector2(38, 36), new Vector2(20, 3)),
                new Color(0.45f, 0.55f, 0.62f, 0.24f), true);

            var leaning = new Rect2(outer.Position + new Vector2(52, 8), new Vector2(28, 42));
            DrawRect(leaning, new Color(0.16f, 0.15f, 0.13f, 0.96f), true);
            DrawRect(leaning, new Color(0.5f, 0.47f, 0.4f, 0.95f), false, 2f);
            for (int i = 0; i < 4; i++)
            {
                float y = leaning.Position.Y + 7f + i * 9f;
                DrawLine(new Vector2(leaning.Position.X + 4, y),
                    new Vector2(leaning.End.X - 4, y - 3),
                    new Color(0.55f, 0.53f, 0.46f, 0.9f), 2f);
            }
            return;
        }

        DrawRect(inner, new Color(0.2f, 0.19f, 0.17f, 1f), true);
        for (int i = 0; i < 4; i++)
        {
            float y = inner.Position.Y + 6f + i * 9f;
            DrawRect(new Rect2(inner.Position.X + 5, y, inner.Size.X - 10, 3),
                new Color(0.04f, 0.04f, 0.045f, 1f), true);
            DrawRect(new Rect2(inner.Position.X + 5, y + 3, inner.Size.X - 10, 2),
                new Color(0.48f, 0.45f, 0.39f, 0.9f), true);
        }
        // The little brass latch her father fitted, at the right edge.
        DrawRect(new Rect2(inner.End.X - 10, inner.GetCenter().Y - 8, 7, 16),
            new Color(0.72f, 0.55f, 0.25f, 1f), true);
        DrawRect(new Rect2(inner.End.X - 8, inner.GetCenter().Y - 2, 3, 4),
            new Color(0.1f, 0.07f, 0.03f, 1f), true);
    }

    protected override void DrawRoom()
    {
        var gs = GameState.Instance;
        if (gs == null)
            return;

        bool anchored = gs.HasFlag(AnchoredFlag);

        // Until the lamp holds it, everything inspected slides — the lamp
        // pointedly does not. After the anchor, the sliding simply stops:
        // one year at a time.
        if (!anchored)
        {
            for (int i = 0; i < ShiftingIds.Length; i++)
            {
                if (!WasExamined(ShiftingIds[i]))
                    continue;

                // Usually one rect per object; all six floor photographs
                // share an ID and each painted piece shifts in place.
                var hotspots = GetHotspots(ShiftingIds[i]);
                for (int j = 0; j < hotspots.Count; j++)
                    DrawShifted(hotspots[j], i * 1.37f + j * 0.43f);
            }
        }

        // In the blackout the wardrobe is only a wardrobe again.
        if (gs.IsShardRecovered(4) && !IsBlackoutOn())
            DrawWardrobePortal();

        DrawVent();

        if (!anchored || IsBlackoutOn())
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

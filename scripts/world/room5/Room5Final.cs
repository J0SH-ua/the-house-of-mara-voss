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
/// simply stays itself. Nobody says so; the player is meant to notice, and
/// to understand that the still thing is the one Anchor wants. Anchoring it
/// settles every shifted object back into one year, holds the writing desk
/// still long enough to recover the unsent apology, and once the letters
/// are Mara's, the inside of the wardrobe — past the coats and the yellow
/// raincoat — stops being a wardrobe and becomes the garden, waiting to be
/// clicked through.
/// </summary>
public partial class Room5Final : MemoryRoomBase
{
    private const string AnchoredFlag = "room5_anchored_lamp";
    private const string DrawerOpenedFlag = "room5_drawer_opened";

    private Label _anchorLabel;
    private Label _gardenLabel;
    private Texture2D _gardenTexture;

    /// <summary>Everything that starts sliding once inspected. The lamp is
    /// deliberately absent: it is the only thing in the room that does not
    /// change, and that stillness — not any line of dialogue — is the clue.</summary>
    private static readonly string[] ShiftingIds =
    [
        "RainWindow", "Bed", "Desk", "RotaryPhone", "Wardrobe", "AlarmClock", "FloorPhotographs",
    ];

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

        // What waits inside the wardrobe once the letters are Mara's.
        _gardenTexture = GD.Load<Texture2D>("res://assets/images/bgs/Room_6_Final.png");
        _gardenLabel = CreateLabel("the garden, through the coats",
            new Vector2(876, 42), 18,
            new Color(1f, 0.9f, 0.62f, 0.92f), new Vector2(268, 34));
        _gardenLabel.Visible = GameState.Instance?.IsShardRecovered(4) ?? false;

        var gs = GameState.Instance;
        if (gs == null || !gs.IsShardRecovered(4))
        {
            _ = PlayStoryLessonOnceAsync("story_lesson_room5_anchor_room",
                "This is the room Mara lives in now, laid over the room she grew up in, until neither year can keep its furniture.");
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
                    "Rain on glass. Beyond it, apartment windows and a city that was never outside the Eldfen house.",
                    "The childhood walls remember the view wrong because Mara has not opened these curtains in months. The present keeps finding the seams.",
                }, "The city is still there when she refuses to look at it.");
                break;

            case "Bed":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "One side of the bed is slept in. The other has been made so carefully it looks like evidence.",
                    "A child's bed in outline, an adult's insomnia in the sheets. Both Maras have waited here for morning.",
                }, "No one has slept on the untouched side. That is the point of it.");
                break;

            case "Lamp":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "The lamp is warm. Not remembered warm — actually warm, left burning through every version of this room.",
                    "Her mother left a light on for whoever came home late. Mara still does.",
                }, "Steady, ordinary light. The room cannot decide what year it is around it.");
                break;

            case "Desk":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "Paper, envelopes, a pen worn smooth. Every sheet begins with the same careful handwriting and stops before it can become a letter anyone could answer.",
                    "\"I should have been there at four.\" The sentence is everywhere. Only its endings keep failing.",
                }, "The desk has been beginning the same apology for twelve years.");
                break;

            case "RotaryPhone":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "The phone is disconnected. Its dial still remembers one number better than all the others: the house where somebody should have called by four.",
                    "No ringing. Only the small mechanical patience of a thing built to carry voices between rooms.",
                }, "The line is silent. It was silent then, too, until it wasn't.");
                break;

            case "Wardrobe":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "Adult coats, dark and heavy. Between them hangs a child's yellow raincoat, dry as paper, still small enough to be lifted with one hand.",
                    "Mara kept it where she would see it only when looking for something else. Memory is good at arranging ambushes.",
                }, "The yellow coat does not belong to the woman who owns this room. That is why it is here.");
                break;

            case "AlarmClock":
                _ = ExamineShiftingAsync(hotspot, new[]
                {
                    "The alarm clock has stopped at four. Its bell is turned down, as if the hour might still startle somebody.",
                    "Four was not when Ida died. Four was when she stopped expecting Mara to be the person who came.",
                }, "Four o'clock, kept under glass.");
                break;

            case "FloorPhotographs":
                _ = ExamineShiftingAsync(hotspot, new[]
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
            SceneManager.ChangeScene("res://scenes/world/room_6.tscn");
        }
        finally
        {
            Busy = false;
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        var gs = GameState.Instance;
        if (gs == null)
            return;

        if (_gardenLabel != null && gs.IsShardRecovered(4) && !_gardenLabel.Visible)
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
            Hotspots.SpawnRipple(hotspot.Center, new Color(0.65f, 0.8f, 1f));
            QueueRedraw();
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
                if (WasExamined(ShiftingIds[i]))
                    DrawShifted(GetHotspot(ShiftingIds[i]), i * 1.37f);
            }
        }

        if (gs.IsShardRecovered(4))
            DrawWardrobePortal();

        if (!anchored)
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

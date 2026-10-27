using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Under the house — reached only by putting three rooms' practical
/// things together: her father's lockpicks from the hall drawer, his
/// flashlight from the kitchen cabinet, and the low vent between them.
/// Since the blackout it is also the way back to light: the breaker
/// box on the wall is the little heart her father always went down
/// to throw, and it wants setting the way he set it — by his rule.
///
/// This is not a seventh fragment room. It is the house's kept-under
/// place: a cold furnace, preserves, a crate of outgrown toys, a locked
/// trunk that yields to the harder lockpick sitting, and a little tool
/// cabinet whose iron key is hidden by the garden gate. The flashlight
/// follows the mouse directly, with no click or hold; outside its beam
/// the painting is genuinely hard
/// to read, and clicking at unseen darkness still counts as a loose
/// click against MEMORY. The cabinet keeps her father's hammer and
/// a twist of bridge nails — no use down here at all, and the only
/// thing that will do, later, where the empty road crosses water.
/// </summary>
public partial class Basement : MemoryRoomBase
{
    private const string Room5ScenePath = "res://scenes/world/room_5.tscn";
    private const string LockpickScenePath = "res://scenes/world/lockpick_puzzle.tscn";
    private const string BreakerScenePath = "res://scenes/world/breaker_box.tscn";
    private static readonly Rect2 BreakerRect = new(452, 62, 196, 128);

    private Vector2 _beamPosition = new(576, 324);
    private bool _beamReady;

    protected override string RoomName => "Basement";
    protected override string BackgroundPath => "res://assets/images/bgs/Basement_BG.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Later entries win overlaps: the trunk therefore comes after
        // the toy crate it stands in front of, and the vent after the
        // furnace pipework beside it.
        yield return new Hotspot("Furnace", "The Cold Furnace",
            new Rect2(28, 55, 250, 545), new Vector2(153, 328), Sfx.Examine, 0.62f);
        yield return new Hotspot("PreserveShelves", "Preserve Shelves",
            new Rect2(318, 205, 370, 285), new Vector2(503, 348), Sfx.PhotoPickup, 0.9f);
        yield return new Hotspot("PreserveShelves", "Preserve Shelves",
            new Rect2(985, 115, 160, 285), new Vector2(1065, 258), Sfx.PhotoPickup, 0.9f);
        yield return new Hotspot("ToyCrate", "The Toy Crate",
            new Rect2(725, 345, 225, 185), new Vector2(838, 438), Sfx.Examine, 0.86f);
        yield return new Hotspot("ToolCabinet", "The Little Tool Cabinet",
            new Rect2(698, 212, 132, 112), new Vector2(764, 268),
            Sfx.KeyType, 0.78f, PointerCursorPath, false);
        yield return new Hotspot("VentDuct", "The Vent Overhead",
            new Rect2(248, 96, 158, 82), new Vector2(327, 137),
            Sfx.Examine, 0.72f, PointerCursorPath, false);
        // The reason her father came down here in every blackout. It
        // was not in the painting either; drawn on the wall like the
        // cabinet, in chunky bands. Utility, not a required examine.
        yield return new Hotspot("BreakerBox", "The Breaker Box",
            BreakerRect, BreakerRect.GetCenter(),
            Sfx.KeyType, 0.6f, PointerCursorPath, false);
        yield return new Hotspot("LockedTrunk", "The Locked Trunk",
            new Rect2(880, 410, 265, 205), new Vector2(1012, 512),
            Sfx.DrawerClose, 0.78f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        var gs = GameState.Instance;
        if (gs != null && !gs.HasFlag("item_flashlight"))
        {
            _ = RefuseTheDarkAsync();
            return;
        }

        AudioManager.Play(Sfx.FlashlightClick, -7f);
        _ = PlayStoryLessonOnceAsync("story_lesson_basement",
            "No one took photographs down here. This is the part of the house that kept the rest of it warm, fed and mended — and like most keeping, it was never thanked in the family album.");
    }

    public override void _Process(double delta)
    {
        UpdateBeam((float)delta);
        base._Process(delta);
        // The mouse is Mara hand on the flashlight. The room must
        // follow it every frame, with no button involved at all.
        QueueRedraw();
    }

    private void UpdateBeam(float dt)
    {
        Vector2 mouse = GetGlobalMousePosition();
        mouse = new Vector2(
            Mathf.Clamp(mouse.X, 0f, 1152f),
            Mathf.Clamp(mouse.Y, 0f, 648f));
        if (!_beamReady)
        {
            _beamPosition = mouse;
            _beamReady = true;
            return;
        }

        float weight = 1f - Mathf.Exp(-20f * Mathf.Max(dt, 0f));
        _beamPosition = _beamPosition.Lerp(mouse, Mathf.Clamp(weight, 0f, 1f));
    }

    protected override async Task OnRequiredExaminesCompletedAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.HasFlag("basement_seen"))
            return;

        AudioManager.Play(Sfx.RoomComplete, -8f, 0.9f);
        await SaySequence(
            "Furnace. Preserves. Toys that were packed away instead of thrown away. The underside of a family's life, all of it kept.",
            "Nothing under this house is a monster either. It's just everything too useful, too sad, or too outgrown to live upstairs.");
        if (!GodotObject.IsInstanceValid(this))
            return;

        gs.SetFlag("basement_seen", save: false);
        gs.MarkRoomCompleted("Basement");
        gs.SaveGame("basement faced");
        gs.Toast("The beam has rested on what waits below. Locked things keep their own counsel a while longer.");
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        // Practical things stay practical even if a Memory Verb is armed.
        switch (hotspot.Id)
        {
            case "LockedTrunk":
                _ = UseTrunkAsync(hotspot);
                return;
            case "ToolCabinet":
                _ = UseToolCabinetAsync(hotspot);
                return;
            case "BreakerBox":
                _ = UseBreakerBoxAsync(hotspot);
                return;
            case "VentDuct":
                _ = ClimbBackAsync();
                return;
        }

        var activeVerb = MemoryVerbSystem.Active;
        if (activeVerb != MemoryVerb.None)
        {
            _ = WrongVerbAsync(activeVerb);
            return;
        }

        switch (hotspot.Id)
        {
            case "Furnace":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The furnace is cold all the way through. As a kid I thought it was a sleeping dragon and Dad fed it coal so it wouldn't wake up hungry.",
                    "Open the little door and there's only ash, grey as old snow. Nothing here has burned in twelve years. The house upstairs keeps itself warm some other way.",
                }, "Cold iron. Ash. A dragon that retired.");
                break;

            case "PreserveShelves":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Jars. Rows of them. Mum's handwriting on every label: apples, plums, beans — October after October, put by against a winter she meant us all to be there for.",
                    "One jar near the front is smaller than the rest. No fruit in it. Marbles. Somebody preserved a summer in here, too, in the only language the shelves spoke.",
                }, "Sealed jars, in the dark, still keeping.");
                break;

            case "ToyCrate":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "A crate of things nobody could throw away. Wooden animals. Kite ribs. One small shoe, because pairs apparently part in this family the way everything does.",
                    "They packed her outgrown things carefully. Not hidden. Not displayed. Just… kept, the way you keep a chair at a table without ever saying whose it is.",
                }, "Outgrown, kept. Both, on purpose.");
                break;
        }
    }

    private async Task RefuseTheDarkAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            await Say("No. There is a dark down here with rooms in it, and I am not meeting it blind.");
            if (GodotObject.IsInstanceValid(this))
                SceneManager.ChangeScene(Room5ScenePath);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private async Task UseTrunkAsync(Hotspot trunk)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs == null)
                return;

            if (gs.HasFlag("basement_negatives_taken"))
            {
                AudioManager.Play(Sfx.DrawerOpen, -8f, 0.82f);
                await Say("Blankets, now, and the smell of cedar. The important thing in it is already against my heart in a tin.");
                return;
            }

            if (gs.HasFlag("basement_trunk_open"))
            {
                await TakeNegativesAsync(trunk);
                return;
            }

            AudioManager.Play(Sfx.DrawerClose, -6f, 0.78f);
            if (!gs.HasFlag("item_lockpick_set"))
            {
                await Say("Locked. A real lock, not a latch — pins and patience. Which I do not currently have in my hands.");
                return;
            }

            await Say("A trunk lock, older than the vent's latch and less forgiving. Dad called these honest. Let's hear what it has to say.");
            if (!GodotObject.IsInstanceValid(this))
                return;

            gs.SetFlag("lockpick_target_trunk", save: false);
            gs.SetFlag("lockpick_origin_basement", save: false);
            gs.ClearFlag("lockpick_target_vent", save: false);
            gs.ClearFlag("lockpick_origin_room5", save: false);
            gs.SaveGame("lockpick trunk armed");
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

    private async Task TakeNegativesAsync(Hotspot trunk)
    {
        var gs = GameState.Instance;
        AudioManager.Play(Sfx.DrawerOpen, -6f, 0.86f);
        await SaySequence(
            "Blankets. Cedar. And at the bottom, a flat tin labelled in Mum's hand: NOT PRINTED. As if printing were the dangerous part.",
            "Negatives. Sleeve after sleeve. Hold one up into the flashlight beam and the world reverses: dark face, bright eyes—",
            "She's looking straight at the camera. In every single one. Not half-turned. Not softened the way the house keeps doing to her. Straight at whoever held the camera. Straight at me.",
            "I can't keep her like that. Nobody gets to keep anyone like that. But the straight-on face was real once. Knowing it was real — that's mine now.");
        if (!GodotObject.IsInstanceValid(this) || gs == null)
            return;

        AudioManager.Play(Sfx.PhotoPickup, -8f, 0.95f);
        gs.SetFlag("basement_negatives_taken", save: false);
        gs.UnlockClue("basement_negatives");
        gs.AddCorruption(-3);
        bool refilled = gs.RefillClarity(1);
        Hotspots.SpawnRipple(trunk.Center, new Color(1f, 0.88f, 0.58f));
        gs.Toast(refilled
            ? "Tin of photograph negatives — kept in Mara's bag (B). A steadier breath: MEMORY eased, Clarity +1. Hold them to a light from the bag: some frames remember rooms better than she does."
            : "Tin of photograph negatives — kept in Mara's bag (B). A steadier breath: MEMORY eased. Hold them to a light from the bag: some frames remember rooms better than she does.");
        QueueRedraw();
    }

    private async Task UseToolCabinetAsync(Hotspot cabinet)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs == null)
                return;

            if (gs.HasFlag("item_hammer_nails") || gs.HasFlag("basement_battery_taken"))
            {
                AudioManager.Play(Sfx.DrawerOpen, -9f, 0.85f);
                await Say("Screwdrivers in order of size, because of course. The hammer's place on top is empty now. He'd approve of it travelling.");
                return;
            }

            if (!gs.HasFlag("item_iron_cellar_key"))
            {
                AudioManager.Play(Sfx.DrawerClose, -7f, 0.8f);
                await Say("A little wall cabinet, locked with a proper iron keyhole. Dad kept the good tools away from small mechanics. There will be a key somewhere he trusted more than a hook.");
                return;
            }

            AudioManager.Play(Sfx.KeyType, -9f, 0.82f);
            await WaitSeconds(0.22f);
            AudioManager.Play(Sfx.DoorUnlock, -7f, 0.94f);
            AudioManager.Play(Sfx.DrawerOpen, -8f, 0.86f);

            // The cabinet opens for the iron key — but knowing where
            // in it the mending tools actually live is a different
            // kind of key, and it is printed on nothing. It sleeps
            // in the trunk's unprinted frames.
            if (!gs.HasFlag("negatives_developed"))
            {
                await SaySequence(
                    "The iron key turns like it was oiled yesterday. Inside: screwdrivers by size, a coil of wire, pencils cut short. Orderly as a held breath.",
                    "But nothing for mending. Nothing heavy. A man who oiled half the street's locks out of kindness kept more than this — and a cabinet this shallow shouldn't have a back this dark. Where would you put the real work, Dad?",
                    "Somewhere there's a picture of this cellar as it was. There has to be. Mum photographed everything except what she couldn't look at straight.");
                if (!GodotObject.IsInstanceValid(this))
                    return;
                gs.Toast("The cabinet is open, but the mending tools aren't where a hand falls. Something remembers this cellar better than Mara does.");
                return;
            }

            await SaySequence(
                "The iron key turns like it was oiled yesterday. Screwdrivers by size, a coil of wire, pencils cut short — and this time I know where to look. The frame Mum never printed: Dad, hanging his claw hammer on the inside of the cabinet door. The inside. Twelve years of looking past the insides of doors.",
                "Here. The hammer, and a twist of paper holding bridge nails older than I am. He kept the mending tools below stairs because that's where things broke quietly. Crossings. Railings. Anything the weather worries at. …All right, Dad. Whatever's broken between here and morning, I'll bring these.");
            if (!GodotObject.IsInstanceValid(this))
                return;

            AudioManager.Play(Sfx.HammerHit, -8f, 0.9f);
            gs.SetFlag("item_hammer_nails", save: false);
            gs.AddCorruption(-2);
            bool refilled = gs.RefillClarity(1);
            gs.SaveGame("hammer and nails taken");
            Hotspots.SpawnRipple(cabinet.Center, new Color(1f, 0.9f, 0.62f));
            gs.Toast(refilled
                ? "Father's hammer & nails — kept in Mara's bag (B). MEMORY eased, Clarity +1."
                : "Father's hammer & nails — kept in Mara's bag (B). MEMORY eased.");
            QueueRedraw();
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

    /// <summary>The little heart. Up close it is its own scene: six
    /// small switches, his pencilled rule, and one big handle.</summary>
    private async Task UseBreakerBoxAsync(Hotspot breaker)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            if (gs == null)
                return;

            if (gs.HasFlag("backup_power_on"))
            {
                AudioManager.Play(Sfx.WordChime, -13f, 0.7f);
                await Say("Humming. Three small green eyes open in the dark, exactly as set. The house has its little heart back, and it's holding.", 4.5f, 24);
                return;
            }

            AudioManager.Play(Sfx.DrawerOpen, -8f, 0.6f);
            await Say("There it is. The box he always came down to, torch in his teeth, us shrieking for him from the top of a stair I can't picture. All right, Dad. Your rule. Your handwriting. Show me.", 5f, 24);
            if (!GodotObject.IsInstanceValid(this))
                return;
            SceneManager.ChangeScene(BreakerScenePath);
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

    private async Task ClimbBackAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.VentCrawl, -11f, 0.9f);
            await Say("Up through the duct, backwards, like a rumour leaving a wall. The bedroom can have me back.");
            if (GodotObject.IsInstanceValid(this))
                SceneManager.ChangeScene(Room5ScenePath);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private async Task WrongVerbAsync(MemoryVerb verb)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -10f, 0.88f);
            string line = verb switch
            {
                MemoryVerb.Echo => "Echo down here only finds drips answering drips. The things below were kept by hand, and hands are what they answer to.",
                MemoryVerb.Anchor => "Nothing under the house is sliding. It all stayed exactly where grief put it. That is rather the problem.",
                MemoryVerb.Trace => "There is no path to trace in a room built for keeping. A shelf is where a story was set down, not where it went.",
                MemoryVerb.Release => "Whatever waits below is not whole yet. Release has no work in a store-room.",
                _ => "That way of remembering has no work below the house.",
            };
            await Say(line, 4.5f, 23);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    // ------------------------------------------------------------ drawing

    protected override void DrawRoom()
    {
        DrawStateOverlays();
        DrawFlashlight();
    }

    private void DrawStateOverlays()
    {
        var gs = GameState.Instance;
        if (gs == null)
            return;

        DrawCabinetState(gs.HasFlag("item_hammer_nails") || gs.HasFlag("basement_battery_taken"));
        DrawBreakerBox(gs.HasFlag("backup_power_on"));
        if (gs.HasFlag("basement_trunk_open"))
            DrawOpenTrunk(gs.HasFlag("basement_negatives_taken"));
    }

    /// <summary>Not in the painting: a grey steel box on conduit, drawn
    /// over the wall like the tool cabinet. Dead, its one small lamp is
    /// dark; set right, three green eyes stay open down here.</summary>
    private void DrawBreakerBox(bool powerOn)
    {
        var rect = BreakerRect;
        // Conduit running up into the dark and down out of sight.
        DrawRect(new Rect2(rect.GetCenter().X - 7, 0, 14, rect.Position.Y),
            new Color(0.16f, 0.16f, 0.17f, 0.98f), true);
        DrawRect(new Rect2(rect.GetCenter().X + 26, rect.End.Y, 10, 120),
            new Color(0.14f, 0.14f, 0.15f, 0.98f), true);

        DrawRect(rect, new Color(0.21f, 0.22f, 0.23f, 0.99f), true);
        DrawRect(rect, new Color(0.52f, 0.53f, 0.5f, 0.95f), false, 3f);
        var door = rect.Grow(-9f);
        DrawRect(door, new Color(0.17f, 0.18f, 0.19f, 1f), true);
        DrawRect(door, new Color(0.4f, 0.41f, 0.38f, 0.8f), false, 2f);

        // A stencilled warning stripe, half worn away.
        DrawRect(new Rect2(door.Position + new Vector2(12, 12), new Vector2(door.Size.X - 24, 10)),
            new Color(0.62f, 0.42f, 0.14f, 0.75f), true);

        // Three little lamps in a row.
        for (int i = 0; i < 3; i++)
        {
            Vector2 lamp = new(door.Position.X + 30f + i * 26f, door.Position.Y + 44f);
            if (powerOn)
            {
                DrawCircle(lamp, 10f, new Color(0.35f, 0.9f, 0.4f, 0.22f));
                DrawCircle(lamp, 5f, new Color(0.45f, 0.95f, 0.48f, 1f));
            }
            else
            {
                DrawCircle(lamp, 5f, new Color(0.16f, 0.2f, 0.17f, 1f));
                DrawArc(lamp, 7f, 0f, Mathf.Tau, 20, new Color(0.45f, 0.46f, 0.42f, 0.8f), 1.5f, true);
            }
        }
        // And the big handle, down or up.
        Vector2 pivot = new(door.End.X - 34f, door.GetCenter().Y + 8f);
        Vector2 grip = pivot + (powerOn ? new Vector2(10, -34) : new Vector2(10, 34));
        DrawLine(pivot, grip, new Color(0.55f, 0.55f, 0.52f, 1f), 6f, true);
        DrawCircle(grip, 8f, new Color(0.6f, 0.2f, 0.14f, 1f));
        DrawCircle(pivot, 6f, new Color(0.35f, 0.35f, 0.34f, 1f));
    }

    /// <summary>The painting shows the cabinet with one door ajar, so its
    /// locked and opened states are drawn over it in chunky bands.</summary>
    private void DrawCabinetState(bool opened)
    {
        var rect = new Rect2(698, 212, 132, 112);
        DrawRect(rect, new Color(0.08f, 0.065f, 0.05f, 0.98f), true);
        DrawRect(rect, new Color(0.48f, 0.4f, 0.3f, 0.95f), false, 3f);

        if (opened)
        {
            var inner = rect.Grow(-9f);
            DrawRect(inner, new Color(0.025f, 0.025f, 0.03f, 1f), true);
            DrawRect(new Rect2(inner.Position + new Vector2(8, 44), new Vector2(inner.Size.X - 16, 5)),
                new Color(0.45f, 0.36f, 0.25f, 0.95f), true);
            // Tool silhouettes, in father's size order.
            for (int i = 0; i < 4; i++)
            {
                float x = inner.Position.X + 16f + i * 25f;
                DrawRect(new Rect2(x, inner.Position.Y + 12, 6, 27),
                    new Color(0.62f, 0.62f, 0.58f, 0.95f), true);
                DrawRect(new Rect2(x - 2, inner.Position.Y + 34, 10, 8),
                    new Color(0.48f, 0.25f, 0.16f, 0.95f), true);
            }
            var swungDoor = new Rect2(rect.End.X - 4, rect.Position.Y + 7, 28, rect.Size.Y - 14);
            DrawRect(swungDoor, new Color(0.2f, 0.16f, 0.12f, 0.98f), true);
            DrawRect(swungDoor, new Color(0.54f, 0.44f, 0.32f, 0.95f), false, 2f);
            return;
        }

        var door = rect.Grow(-7f);
        DrawRect(door, new Color(0.23f, 0.18f, 0.13f, 1f), true);
        DrawRect(new Rect2(door.GetCenter().X - 2, door.Position.Y + 5, 4, door.Size.Y - 10),
            new Color(0.06f, 0.05f, 0.04f, 1f), true);
        DrawRect(door.Grow(-8f), new Color(0.5f, 0.42f, 0.3f, 0.75f), false, 2f);
        Vector2 keyhole = new(door.GetCenter().X + 13, door.GetCenter().Y + 3);
        DrawCircle(keyhole, 6f, new Color(0.02f, 0.02f, 0.02f));
        DrawRect(new Rect2(keyhole + new Vector2(-2, 2), new Vector2(4, 10)),
            new Color(0.02f, 0.02f, 0.02f), true);
        DrawCircle(keyhole, 9f, new Color(0.66f, 0.52f, 0.27f, 0.4f));
    }

    private void DrawOpenTrunk(bool negativesTaken)
    {
        var mouth = new Rect2(896, 410, 232, 46);
        DrawRect(mouth, new Color(0.015f, 0.014f, 0.016f, 1f), true);
        DrawRect(mouth, new Color(0.48f, 0.38f, 0.26f, 0.95f), false, 3f);
        var lid = new Rect2(902, 378, 220, 28);
        DrawRect(lid, new Color(0.2f, 0.15f, 0.11f, 0.98f), true);
        DrawRect(lid, new Color(0.55f, 0.43f, 0.28f, 0.95f), false, 2f);
        if (!negativesTaken)
        {
            // The tin, catching whatever light is brave enough.
            var tin = new Rect2(976, 424, 62, 16);
            DrawRect(tin, new Color(0.68f, 0.64f, 0.55f, 0.98f), true);
            DrawRect(tin, new Color(0.2f, 0.18f, 0.15f, 0.95f), false, 2f);
            DrawRect(new Rect2(tin.Position + new Vector2(8, 4), new Vector2(34, 3)),
                new Color(0.92f, 0.88f, 0.72f, 0.95f), true);
        }
    }

    private void DrawFlashlight()
    {
        var gs = GameState.Instance;
        if (gs == null || !gs.HasFlag("item_flashlight"))
        {
            DrawRect(new Rect2(0, 0, 1152, 648), new Color(0, 0, 0, 0.97f), true);
            return;
        }

        Vector2 beam = _beamPosition;
        float radius = gs.HasFlag("basement_battery_taken") ? 208f : 158f;
        float falloffScale = 1f;
        if (gs.DevToolsEnabled)
        {
            radius *= gs.DevFlashlightScale;
            falloffScale = gs.DevFlashlightScale;
        }

        // Two square-edged darkness passes: a broad penumbra, then the
        // true dark outside the beam itself. Chunky strips, no shader —
        // the same visual language as the rest of the code-drawn rooms.
        DrawDarkOutside(beam, radius + 58f * falloffScale, new Color(0.002f, 0.002f, 0.004f, 0.42f));
        DrawDarkOutside(beam, radius, new Color(0.001f, 0.001f, 0.003f, 0.74f));

        DrawArc(beam, radius * 0.97f, 0f, Mathf.Tau, 56,
            new Color(1f, 0.83f, 0.45f, 0.09f), 13f, true);
        DrawArc(beam, radius * 0.68f, 0f, Mathf.Tau, 48,
            new Color(1f, 0.88f, 0.55f, 0.05f), 20f, true);
        DrawCircle(beam, 4f, new Color(1f, 0.9f, 0.62f, 0.28f));
    }

    private void DrawDarkOutside(Vector2 center, float radius, Color dark)
    {
        float top = Mathf.Clamp(center.Y - radius, 0f, 648f);
        float bottom = Mathf.Clamp(center.Y + radius, 0f, 648f);
        if (top > 0f)
            DrawRect(new Rect2(0, 0, 1152, top), dark, true);
        if (bottom < 648f)
            DrawRect(new Rect2(0, bottom, 1152, 648f - bottom), dark, true);

        const float stripHeight = 9f;
        for (float y = top; y < bottom; y += stripHeight)
        {
            float height = Mathf.Min(stripHeight + 1f, bottom - y);
            float centreY = y + height * 0.5f;
            float dy = centreY - center.Y;
            float half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            float leftEdge = Mathf.Clamp(center.X - half, 0f, 1152f);
            float rightEdge = Mathf.Clamp(center.X + half, 0f, 1152f);
            if (leftEdge > 0f)
                DrawRect(new Rect2(0, y, leftEdge, height), dark, true);
            if (rightEdge < 1152f)
                DrawRect(new Rect2(rightEdge, y, 1152f - rightEdge, height), dark, true);
        }
    }
}

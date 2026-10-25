using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The prologue: Mara's childhood bedroom, first thing in the morning.
///
/// The original post-Play memory cutscene plays over this room first; then
/// a New Game wakes here — first person, eyelids and all — staring at a
/// ceiling she has not seen in twenty years. The bedroom is also the live
/// tutorial: all five ordinary things must be examined before Mara will
/// open the door. Beyond it she expects the hallway; the house gives her
/// the kitchen instead. Room 1 then offers the existing tutorial cards.
///
/// One-way by design: there is no Step Back from the kitchen, because the
/// house has already moved the hallway. Reaching the kitchen sets
/// "prologue_from_bedroom", which completes (and seals) this room and arms
/// GameUI's "…The kitchen?" beat on arrival.
/// </summary>
public partial class BedroomPrologue : MemoryRoomBase
{
    private const string AwakeFlag = "prologue_awake";
    private const string FromBedroomFlag = "prologue_from_bedroom";
    private const string KitchenScenePath = "res://scenes/world/room_1.tscn";
    private const string CeilingPath = "res://assets/images/bgs/Bedroom_Ceiling_FP.png";

    private TextureRect _ceiling;
    private ColorRect _lidTop;
    private ColorRect _lidBottom;

    protected override string RoomName => "Bedroom";
    protected override string BackgroundPath => "res://assets/images/bgs/Bedroom_Prologue_BG.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Later entries win overlaps: the bed first, the door last.
        yield return new Hotspot("Bed", "The Bed",
            new Rect2(110, 320, 530, 220), new Vector2(380, 440), Sfx.Examine, 0.8f);
        yield return new Hotspot("Window", "The Window",
            new Rect2(350, 85, 210, 300), new Vector2(455, 235), Sfx.WordChime, 0.92f);
        yield return new Hotspot("Nightstand", "The Nightstand",
            new Rect2(4, 395, 150, 245), new Vector2(78, 500), Sfx.Examine, 0.9f);
        yield return new Hotspot("Drawing", "A Child's Drawing",
            new Rect2(618, 195, 155, 115), new Vector2(695, 252), Sfx.Examine, 1.1f);
        yield return new Hotspot("Wardrobe", "The Wardrobe",
            new Rect2(782, 135, 185, 400), new Vector2(875, 340), Sfx.Examine, 0.75f);
        yield return new Hotspot("Door", "The Door",
            new Rect2(998, 60, 140, 565), new Vector2(1068, 350),
            Sfx.DoorCreak, 0.9f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        var gs = GameState.Instance;
        if (gs != null && !gs.HasFlag(AwakeFlag))
            _ = PlayWakingAsync();
    }

    protected override async Task OnRequiredExaminesCompletedAsync()
    {
        AudioManager.Play(Sfx.RoomComplete, -8f);
        await Say("There. Bed, window, clock, drawing, wardrobe. I've looked at the room I woke in. Now I can face whatever put me here.", 5.5f, 24);
        GameState.Instance?.Toast("Everything in the bedroom has been explored. The door is ready.");
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        switch (hotspot.Id)
        {
            case "Bed":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Still warm on my side. So I really slept here.",
                    "It's a child's bed. My feet would hang off the end of it now. In here — wherever here is — they don't.",
                }, "Warm. Small. Mine. All three impossible.");
                break;

            case "Window":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Morning over the garden. The apple trees. The fence. The whole of it, exactly—",
                    "It's too still. Like the garden is a photograph of itself, waiting for someone to say it's allowed to move.",
                }, "Still morning. Still too still.");
                break;

            case "Nightstand":
                _ = ExamineNightstandAsync(hotspot);
                break;

            case "Drawing":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "…I made that. Two girls and a house with too many windows.",
                    "The tall one in pink is meant to be me. The little one holds my hand in every drawing I ever did. I don't remember making this one. My hand does.",
                }, "Two girls. A house. Too many windows.");
                break;

            case "Wardrobe":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "My wardrobe. Behind that door are jumpers small enough to break my heart.",
                    "Not yet. One impossible thing at a time.",
                }, "Not yet.");
                break;

            case "Door":
                _ = LeaveThroughDoorAsync();
                break;
        }
    }

    /// <summary>The nightstand is one of the five objects Mara must face
    /// before the door works. It gives her nothing to carry — the house
    /// has not started asking for practical things yet. Mum's bent
    /// hairpin waits further in, where her mother's hands actually left
    /// it: in the kitchen drawer, beside the keys.</summary>
    private async Task ExamineNightstandAsync(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        Busy = true;
        try
        {
            bool first = RecordExamine(hotspot);
            bool completedRequiredNow = first && AllRequiredExamined();
            AudioManager.PlayVaried(hotspot.SfxId, -5f, hotspot.SfxPitch, 0.04f);

            if (!first)
            {
                GameState.Instance?.RegisterRepeatExaminePenalty();
                await Say("Stopped. Everything here is stopped except me.", 3.5f, 24);
                return;
            }

            await SaySequence(new[]
            {
                "My alarm clock. The hands have stopped, and honestly? Fair enough.",
                "It used to ring me awake for school. She'd already be in the doorway talking before I'd even sat up.",
            });
            if (completedRequiredNow && StillHere())
                await OnRequiredExaminesCompletedAsync();
        }
        finally
        {
            if (StillHere())
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    // ------------------------------------------------------------ waking

    /// <summary>First person, waking: black, a few blinks, a known ceiling,
    /// then the slow tilt of a head taking in a room it outgrew.</summary>
    private async Task PlayWakingAsync()
    {
        Busy = true;
        try
        {
            // The original memory intro has first claim on a New Game. It
            // consumes CutscenePending and pauses the tree; Mara's eyelids
            // wait until both the pending flag and that pause are gone.
            while (GameState.Instance?.CutscenePending == true || GetTree().Paused)
            {
                await WaitSeconds(0.1f);
                if (!StillHere())
                    return;
            }

            BuildWakingOverlay();

            if (Settings.ReduceMotionOrDefault())
            {
                await BlinkTo(1f, 0.9f);
            }
            else
            {
                await WaitSeconds(0.7f);
                if (!StillHere())
                    return;
                await BlinkTo(0.25f, 0.28f);
                await WaitSeconds(0.18f);
                await BlinkTo(0.02f, 0.22f);
                await WaitSeconds(0.25f);
                await BlinkTo(0.55f, 0.3f);
                await WaitSeconds(0.2f);
                await BlinkTo(0.1f, 0.2f);
                await WaitSeconds(0.3f);
                if (!StillHere())
                    return;
                await BlinkTo(1f, 0.45f);
            }

            if (!StillHere())
                return;
            await Say("…Ceiling. Okay. Ceiling's where I left it.", 3.6f, 26);
            if (!StillHere())
                return;
            await Say("Wait. That crack — I know that crack. It wanders like a river. I used to follow it with my eyes when I couldn't sleep.", 5.5f, 24);
            if (!StillHere())
                return;

            // Sitting up: the ceiling slides away upward (with a small
            // sideways drift, the way a head turns), and the room is there.
            AudioManager.Play(Sfx.PhotoPickup, -16f, 0.8f);
            var tilt = CreateTween();
            tilt.TweenProperty(_ceiling, "position", new Vector2(-46f, -648f),
                    Settings.ReduceMotionOrDefault() ? 0.4f : 2.3f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            await ToSignal(tilt, Tween.SignalName.Finished);
            if (!StillHere())
                return;

            await Say("My room. My old room. The bed, the window, the wardrobe — everything small. Everything where it was.", 5.5f, 24);
            if (!StillHere())
                return;
            await Say("I rent one room in the city now, with a ceiling I've never looked at once. So this is a dream, or it's… Breathe, Mara. Look around first. Answers after.", 6f, 24);
            if (!StillHere())
                return;
            await Say("Start where I am. Bed, window, clock, drawing, wardrobe — look at all of it properly. Then the door.", 5.5f, 24);
            if (!StillHere())
                return;

            GameState.Instance?.SetFlag(AwakeFlag);
            GameState.Instance?.Toast("Click each object to examine it. Hold Tab to reveal what still waits; the objective keeps count.");
        }
        finally
        {
            FreeWakingOverlay();
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();

    private void BuildWakingOverlay()
    {
        _ceiling = new TextureRect
        {
            Texture = GD.Load<Texture2D>(CeilingPath),
            Position = Vector2.Zero,
            Size = new Vector2(1152f, 648f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 4000,
        };
        AddChild(_ceiling);

        _lidTop = new ColorRect
        {
            Color = Colors.Black,
            Position = Vector2.Zero,
            Size = new Vector2(1152f, 324f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 4001,
        };
        AddChild(_lidTop);

        _lidBottom = new ColorRect
        {
            Color = Colors.Black,
            Position = new Vector2(0f, 324f),
            Size = new Vector2(1152f, 324f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 4001,
        };
        AddChild(_lidBottom);
    }

    /// <summary>Open (or close) the eyes to a fraction of the screen height:
    /// 0 = shut, 1 = fully open. The ceiling waits behind the lids.</summary>
    private async Task BlinkTo(float openFraction, float seconds)
    {
        if (!StillHere() || _lidTop == null || _lidBottom == null)
            return;
        float lid = (1f - Mathf.Clamp(openFraction, 0f, 1f)) * 324f;
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_lidTop, "size:y", lid, seconds);
        tween.TweenProperty(_lidBottom, "size:y", lid, seconds);
        tween.TweenProperty(_lidBottom, "position:y", 648f - lid, seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private void FreeWakingOverlay()
    {
        if (_ceiling != null && GodotObject.IsInstanceValid(_ceiling))
            _ceiling.QueueFree();
        if (_lidTop != null && GodotObject.IsInstanceValid(_lidTop))
            _lidTop.QueueFree();
        if (_lidBottom != null && GodotObject.IsInstanceValid(_lidBottom))
            _lidBottom.QueueFree();
        _ceiling = null;
        _lidTop = null;
        _lidBottom = null;
    }

    // --------------------------------------------------------------- door

    private async Task LeaveThroughDoorAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            if (!AllRequiredExamined())
            {
                RoomExamineTracker.GetRoomProgress(RoomName, out int done, out int total, out var remaining);
                AudioManager.Play(Sfx.LockRattle, -8f, 0.9f);
                await Say("No. Not before I've even looked at where I woke up. If I start by running from one room, I'll run from all of them.", 5.5f, 24);
                if (!StillHere())
                    return;
                await Say($"Still waiting for an honest look: {string.Join(", ", remaining.Select(BedroomObjectName))}. ({done}/{total} explored.)", 4.5f, 23);
                GameState.Instance?.Toast("Explore every bedroom object first — hold Tab to reveal what still waits.");
                return;
            }

            await Say("Hallway, then. Stairs down. Kitchen. Mum at the stove saying I'd sleep through my own funeral—", 5f, 24);
            if (!StillHere())
                return;

            // The kitchen-confusion beat is GameUI's, on arrival; this flag
            // both arms it and marks the bedroom done (RoomFullyDone).
            GameState.Instance?.SetFlag(FromBedroomFlag);
            AudioManager.Play(Sfx.DoorCreak, -7f, 0.9f);
            await WaitSeconds(0.3f);
            SceneManager.ChangeScene(KitchenScenePath);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private static string BedroomObjectName(string id) => id switch
    {
        "Bed" => "the bed",
        "Window" => "the window",
        "Nightstand" => "the nightstand clock",
        "Drawing" => "the drawing",
        "Wardrobe" => "the wardrobe",
        _ => "the " + (id ?? "room").ToLowerInvariant(),
    };
}

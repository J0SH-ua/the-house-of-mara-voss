using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Room 6 — the last afternoon.
///
/// The garden is not solved by finding a hidden object. After each stop has
/// been faced, Trace links them as causes in the order they happened:
/// apple basket (waiting) → swing (counting) → broken gate (leaving) →
/// empty road (Mara never appeared). Completing the line recovers
/// Fragment VI and teaches Release through GameState's shard progression.
/// </summary>
public partial class Room6Final : MemoryRoomBase
{
    private const string TracedFlag = "room6_traced";

    private static readonly string[] TraceSequence =
    [
        "AppleBasket",
        "Swing",
        "BrokenGate",
        "EmptyRoad",
    ];

    private static readonly float[] TracePitches = [1f, 1.189f, 1.498f, 1.782f];

    private readonly List<Vector2> _tracePoints = new();
    private int _traceIndex;
    private bool _traceUseRegistered;
    private Label _traceLabel;

    protected override string RoomName => "Room6";
    protected override string BackgroundPath => "res://assets/images/bgs/Room_6_Final.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        yield return new Hotspot("AppleBasket", "Apple Basket",
            new Rect2(105, 425, 100, 145), new Vector2(155, 497), Sfx.Examine, 0.92f);
        yield return new Hotspot("Swing", "Swing",
            new Rect2(430, 170, 95, 210), new Vector2(480, 330), Sfx.Examine, 0.78f);
        yield return new Hotspot("BrokenGate", "Broken Garden Gate",
            new Rect2(735, 335, 175, 220), new Vector2(815, 455),
            Sfx.DoorCreak, 0.88f, PointerCursorPath);
        yield return new Hotspot("EmptyRoad", "Empty Road",
            new Rect2(930, 255, 210, 145), new Vector2(1035, 320),
            Sfx.Examine, 0.64f, PointerCursorPath);
    }

    protected override void OnRoomReady()
    {
        _traceLabel = CreateLabel("the way she went",
            new Vector2(405, 82), 19,
            new Color(1f, 0.9f, 0.62f, 0.92f), new Vector2(342, 34));
        _traceLabel.Visible = false;

        var gs = GameState.Instance;
        if (gs == null || (!gs.HasFlag(TracedFlag) && !gs.IsShardRecovered(5)))
        {
            if (gs != null && !gs.IsShardRecovered(5))
            {
                _ = PlayStoryLessonOnceAsync("story_lesson_room6_trace_garden",
                    "The garden is not hiding one more object for Mara to collect. It is keeping an afternoon in separate places because joined together, they become the way Ida went.");
            }
            return;
        }

        gs.SetFlag(TracedFlag, save: false);
        _traceIndex = TraceSequence.Length;
        _traceUseRegistered = true;
        foreach (var id in TraceSequence)
        {
            var hotspot = GetHotspot(id);
            if (hotspot != null)
                _tracePoints.Add(hotspot.Center);
        }
        SetBackgroundTint(new Color(0.78f, 0.72f, 0.9f));
        _traceLabel.Visible = true;
    }

    protected override async Task OnRequiredExaminesCompletedAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.IsShardRecovered(5))
            return;

        await SayStoryLessonOnceAsync("story_monologue_room6_afternoon",
            "Now hear it whole, the way Mara has never once let herself hear it. Nothing monstrous happened in this garden. That is the unbearable part — there is no villain hiding in it for her to finally blame instead of herself.",
            "A fourteen-year-old waited to be fetched by the person she trusted most in the world. The minutes went somewhere ordinary: a book, a friend's kitchen, a clock her sister simply did not look at. Ordinary minutes. Mara has been tried for them every night for twelve years, by the only court that sits.",
            "Trace will not undo a single one of them. Trace is Mara finally walking the afternoon in the direction it actually went, instead of standing at the end of it, screaming backward.");
        await SayStoryLessonOnceAsync("story_lesson_room6_all_faced",
            "Now the garden has told four separate truths. Separately, Mara can survive them. Joined, they are the afternoon.");
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        var activeVerb = MemoryVerbSystem.Active;
        if (activeVerb == MemoryVerb.Trace)
        {
            HandleTraceClick(hotspot);
            return;
        }

        if (activeVerb != MemoryVerb.None)
        {
            _ = WrongVerbAsync(activeVerb);
            return;
        }

        var gs = GameState.Instance;
        if (gs != null && gs.IsShardRecovered(5) && hotspot.Id == "EmptyRoad")
        {
            _ = ExitToLastRoomAsync();
            return;
        }

        ExamineOrdinary(hotspot);
    }

    private void ExamineOrdinary(Hotspot hotspot)
    {
        switch (hotspot.Id)
        {
            case "AppleBasket":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Her apple basket, down by the roots. Packed careful — enough for two people and a long afternoon.",
                    "One apple's got a bite out of it. A kid's bite. She didn't leave hungry. She left tired of being the only one still waiting.",
                }, "The bitten apple hasn't even gone brown. Waiting keeps better than fruit here.");
                break;

            case "Swing":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The swing's nearly still. The ropes have worn pale grooves in the branch. Years of counting did that.",
                    "She counted swings when clocks were too slow. One for every minute she meant not to mind. She got to a number I've never been able to forget. Then she stopped counting.",
                }, "It moves the tiniest bit. Like someone only just stepped off.");
                break;

            case "BrokenGate":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The latch is broken from this side. Not kicked in. Worried loose, over and over, by a hand that knew it. Hers.",
                    "Under it, one small shoe print, pointing out. There's no print coming back.",
                }, "It hangs open. Waiting makes its own decisions, in the end.");
                break;

            case "EmptyRoad":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The road bends off to the little bridge and out of sight. Just gravel. Just distance. That's the worst part — it never needed to be monstrous.",
                    "At 4:47 I came through that gate calling her name. All the road gave me back was the swing chain, creaking behind me.",
                }, "No car. No figure on the bridge. Just the place where being late stopped being temporary.");
                break;
        }
    }

    private void HandleTraceClick(Hotspot hotspot)
    {
        var gs = GameState.Instance;

        // If the afternoon is already understood, the road is the way onward
        // even if the player still has Trace armed from habit.
        if (gs != null && gs.IsShardRecovered(5))
        {
            if (hotspot.Id == "EmptyRoad")
            {
                MemoryVerbSystem.Cancel(silent: true);
                _ = ExitToLastRoomAsync();
            }
            else
            {
                AudioManager.Play(Sfx.UiHover, -10f, 0.9f);
                gs.Toast("The line is already drawn. Follow the empty road.");
            }
            return;
        }

        // Trace can be armed before every stop is known; in that case the
        // click still performs the ordinary first look and Trace stays armed.
        if (!WasExamined(hotspot.Id))
        {
            ExamineOrdinary(hotspot);
            return;
        }

        if (!AllRequiredExamined())
        {
            AudioManager.Play(Sfx.Denied, -8f);
            gs?.Toast("The line will not hold where she has not looked.");
            return;
        }

        if (_traceIndex >= TraceSequence.Length)
            return;

        // Clicking the most recent correct stop again is harmless.
        if (_traceIndex > 0 && hotspot.Id == TraceSequence[_traceIndex - 1])
        {
            AudioManager.Play(Sfx.UiHover, -11f, 1.05f);
            gs?.Toast($"Held: {hotspot.DisplayName}.");
            return;
        }

        string expectedId = TraceSequence[_traceIndex];
        if (hotspot.Id != expectedId)
        {
            AudioManager.Play(Sfx.Denied, -7f, 0.82f);
            if (_traceIndex > 0)
            {
                _traceIndex = 0;
                _tracePoints.Clear();
                QueueRedraw();
                gs?.Toast("The line breaks.");
            }
            else
            {
                gs?.Toast("Trace begins where Ida waited.");
            }
            return;
        }

        if (!_traceUseRegistered)
        {
            _traceUseRegistered = true;
            MemoryVerbSystem.RegisterUse(MemoryVerb.Trace);
        }

        float pitch = TracePitches[Mathf.Min(_traceIndex, TracePitches.Length - 1)];
        AudioManager.Play(Sfx.WordChime, -6f, pitch);
        Hotspots.SpawnRipple(hotspot.Center, new Color(1f, 0.88f, 0.55f));
        _tracePoints.Add(hotspot.Center);
        _traceIndex++;
        QueueRedraw();

        if (_traceIndex >= TraceSequence.Length)
        {
            _ = CompleteTraceAsync(hotspot);
        }
        else
        {
            gs?.Toast($"Traced: {hotspot.DisplayName} ({_traceIndex}/{TraceSequence.Length}).");
        }
    }

    private async Task CompleteTraceAsync(Hotspot finalHotspot)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var gs = GameState.Instance;
            gs?.SetFlag(TracedFlag);
            gs?.UnlockClue("last_afternoon_trace");
            MemoryVerbSystem.Cancel(silent: true);

            AudioManager.Play(Sfx.PuzzleSolve, -6f);
            AudioManager.Play(Sfx.ClarifyReveal, -8f, 1.05f);
            SetBackgroundTint(new Color(0.78f, 0.72f, 0.9f));
            if (_traceLabel != null)
                _traceLabel.Visible = true;
            QueueRedraw();

            await WaitSeconds(0.35f);
            if (!GodotObject.IsInstanceValid(this))
                return;

            bool first = gs != null && gs.CollectShard(5);
            gs?.MarkRoomCompleted("Room6");
            Hotspots.SpawnRipple(finalHotspot.Center, new Color(1f, 0.88f, 0.55f));

            if (first)
            {
                await SaySequence(
                    "Basket. Swing. Gate. Road.",
                    "She waited with the apples. She counted on the swing until counting became another way to be alone. Then she opened the broken gate herself.",
                    "I was twenty-two, and so sure there'd be time to finish what I was doing. I remembered at 4:47. The swing was still moving. The road was already empty.",
                    "This is the afternoon under every room. No monster. No mystery with clean edges. Just a sister who waited — and a sister who didn't come.",
                    "Now that the line can be followed without looking away, the last way of remembering wakes: Release. It will not arrange the truth kindly.");
            }
            else
            {
                await Say("Basket, swing, gate, road. The order does not soften, but it no longer hides.");
            }
        }
        finally
        {
            Busy = false;
            QueueRedraw();
        }
    }

    private async Task WrongVerbAsync(MemoryVerb verb)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.Denied, -9f, 0.92f);
            string line = verb switch
            {
                MemoryVerb.Echo => "Echo hears leaves, rope, and a name called too late. The garden's truth is not a sentence.",
                MemoryVerb.Anchor => "Nothing here can be held in one year. The garden only becomes true by letting its moments lead to one another.",
                MemoryVerb.Release => "There is nothing to release until Mara knows the way Ida went.",
                _ => "That way of remembering does not cross this garden.",
            };
            await Say(line, 4f, 23);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ExitToLastRoomAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.DoorCreak, -8f, 0.82f);
            await SaySequence(
                "You take the road Ida took, but in the opposite tense: not leaving a person behind, only carrying what is known back toward her sister.",
                "At the bend, the fields give way to the front of the house. The porch. The light is on.");
            SceneManager.ChangeScene("res://scenes/world/porch_447.tscn");
        }
        finally
        {
            Busy = false;
        }
    }

    protected override void DrawRoom()
    {
        if (_tracePoints.Count == 0)
            return;

        bool solved = _traceIndex >= TraceSequence.Length;
        var line = solved
            ? new Color(1f, 0.88f, 0.55f, 0.92f)
            : new Color(1f, 0.9f, 0.62f, 0.76f);
        var shadow = new Color(0.08f, 0.05f, 0.02f, 0.5f);

        for (int i = 1; i < _tracePoints.Count; i++)
        {
            DrawLine(_tracePoints[i - 1] + new Vector2(2, 2), _tracePoints[i] + new Vector2(2, 2),
                shadow, 5f, true);
            DrawLine(_tracePoints[i - 1], _tracePoints[i], line, 2.5f, true);
        }

        if (!solved && MemoryVerbSystem.IsArmed(MemoryVerb.Trace) && !Busy &&
            !Dialogue.IsInputBlocked && !GetTree().Paused)
        {
            DrawLine(_tracePoints[^1], GetGlobalMousePosition(),
                new Color(1f, 0.9f, 0.62f, 0.28f), 1.5f, true);
        }

        foreach (var point in _tracePoints)
        {
            DrawArc(point, 19f, 0f, Mathf.Tau, 40, line, 2.2f, true);
            DrawArc(point, 10f, 0f, Mathf.Tau, 32,
                new Color(line.R, line.G, line.B, 0.4f), 1.2f, true);
            DrawCircle(point, 3f, line);
        }
    }
}

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
                    "The garden is not hiding one more object for Mara to collect. It is keeping an afternoon in separate places because joined together, they become the way Ida went.",
                    "First click each stop normally and hear what happened there. The basket, the swing, the gate and the road will tell you whether Ida was waiting, counting, leaving, or already beyond anyone's call.",
                    "When all four have been faced, Trace can draw the line: press T — or click Trace — then click those same stops again in causal order. This second touching is not examining. It is joining.");
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

        string traceHand = MemoryVerbSystem.IsArmed(MemoryVerb.Trace)
            ? "Trace is already in Mara's hands. Now click the stops again — not to examine them, but to join waiting to counting, counting to leaving, leaving to the road."
            : "Now choose Trace — press T, or click Trace in the Memory bar — and click the stops again in that order: waiting, counting, leaving, road.";
        await SayStoryLessonOnceAsync("story_lesson_room6_all_faced",
            "Now the garden has told four separate truths. Separately, Mara can survive them. Joined, they are the afternoon.",
            traceHand,
            "A rising note and a gold line mean a cause has found the next moment. If the line breaks, begin where Ida waited. Mara's Journal (J) keeps the arrangement under Notes & Clues.");
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
                    "A woven basket of apples waits by the tree roots, packed carefully enough for two people and a long afternoon.",
                    "One apple has a child's bite taken out of it. Ida did not leave hungry. She left tired of being the only one still waiting.",
                }, "The bitten apple has not browned. In this garden, waiting is preserved better than fruit.");
                break;

            case "Swing":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The swing hangs almost still. Its ropes have bitten pale grooves into the branch from years of counting.",
                    "Ida counted swings when clocks were too slow: one for every minute she meant not to mind. She reached a number Mara has never been able to forget, then stopped counting.",
                }, "The swing moves the smallest possible distance, as if someone has only just stepped off it.");
                break;

            case "BrokenGate":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The latch is broken from the garden side. Not kicked in by a stranger — worried loose, over and over, by a familiar hand.",
                    "Below it is one small shoe print pointed outward. No matching print comes back.",
                }, "The gate hangs open on the decision waiting eventually made.");
                break;

            case "EmptyRoad":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "The road bends toward the little bridge and out of sight. It is ordinary gravel and pale distance. That is the worst part: it never needed to be monstrous.",
                    "At 4:47 Mara came through the gate calling Ida's name. The road gave her back only the sound of the swing chain behind her.",
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
            GetProgress(out int done, out int total);
            AudioManager.Play(Sfx.Denied, -8f);
            gs?.Toast($"Trace can only connect stops Mara has faced. Click the remaining places normally first ({done}/{total}); Trace will stay in her hands.");
            return;
        }

        if (_traceIndex >= TraceSequence.Length)
            return;

        // Clicking the most recent correct stop again is harmless.
        if (_traceIndex > 0 && hotspot.Id == TraceSequence[_traceIndex - 1])
        {
            AudioManager.Play(Sfx.UiHover, -11f, 1.05f);
            gs?.Toast($"Held: {hotspot.DisplayName}. Next: {GetHotspot(TraceSequence[_traceIndex])?.DisplayName}.");
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
                gs?.Toast("The line breaks. Causes have an order — begin where the waiting began: the apple basket.");
            }
            else
            {
                gs?.Toast("Trace begins where Ida waited: the apple basket.");
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
            string nextName = GetHotspot(TraceSequence[_traceIndex])?.DisplayName ?? "the next stop";
            gs?.Toast($"Traced: {hotspot.DisplayName} ({_traceIndex}/{TraceSequence.Length}) · next: {nextName}.");
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
                    "Mara was twenty-two, and certain there would be time to finish what she was doing. She remembered at 4:47. The swing was still moving. The road was already empty.",
                    "This is the afternoon under every room: not a monster, not a mystery with clean edges. Only a sister who waited, and a sister who did not come.",
                    "Now that the line can be followed without looking away, the last way of remembering wakes: Release. It will not examine anything for Mara, and it will not arrange the truth kindly.",
                    "In the last room, return the six fragments in the order she lived them first. Only then choose Release (R, or its Memory-bar button) and click the assembled memory — that click is Mara deciding what the truth is for.");
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
                MemoryVerb.Echo => "Echo hears leaves, rope, and a name called too late. The garden's truth is not a sentence; after every stop is faced, Trace (T) must join its sequence.",
                MemoryVerb.Anchor => "Nothing here can be held in one year. After every stop is faced, Trace (T) lets the garden become true by allowing its moments to lead to one another.",
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
                "At the bend, the fields give way to the last room of the house.");
            SceneManager.ChangeScene("res://scenes/world/room_7.tscn");
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

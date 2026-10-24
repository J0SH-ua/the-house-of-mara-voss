using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Room 4 close-up — the hallway door's last knock.
///
/// Room 4 proper teaches Echo by turning ten photographs into a sentence.
/// This close-up gives Echo one final, smaller use before Anchor takes over
/// in Room 5: knock each brass picture and then the keyhole beneath it,
/// crossing the bridge, opening the little door, and closing the circle.
/// Ordinary clicks examine the six brass pieces and teach that pairing;
/// only Echo-armed clicks count as knocks.
/// </summary>
public partial class Room4Puzzle : MemoryRoomBase
{
    private const string Room5ScenePath = "res://scenes/world/room_5.tscn";
    private const string SentenceFlag = "room4_sentence_complete";
    private const string OpenedFlag = "room4_door_opened";

    // DoorCloseup.png is a 1122x1402 portrait. This 1122x631 band has the
    // same 16:9 proportions as the room viewport and contains the three
    // brass plates plus the three keyholes without squashing the door.
    private static readonly Rect2 DoorSourceRect = new(0, 410, 1122, 631);

    private static readonly string[] KnockSequence =
    [
        "BridgePlate",
        "LeftKeyhole",
        "DoorPlate",
        "CenterKeyhole",
        "CirclePlate",
        "RightKeyhole",
    ];

    private static readonly float[] KnockPitches = [0.82f, 0.94f, 1.0f, 1.12f, 1.19f, 1.34f];

    private Texture2D _doorTexture;
    private Label _statusLabel;
    private int _knockProgress;
    private bool _opened;

    protected override string RoomName => "Room4";
    protected override string BackgroundPath => "res://assets/images/bgs/DoorCloseup.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Coordinates map the DoorSourceRect crop above onto 1152x648.
        // The bridge plate starts at x=312 so the legacy BackArrow's large
        // 220x220 click shape (x 85..305) can never steal its left edge.
        yield return new Hotspot("BridgePlate", "Bridge Plate",
            new Rect2(312, 105, 145, 210), new Vector2(382, 212),
            Sfx.DoorCreak, 0.9f, PointerCursorPath, false);
        yield return new Hotspot("DoorPlate", "Little Door Plate",
            new Rect2(492, 105, 172, 210), new Vector2(578, 212),
            Sfx.DoorCreak, 1f, PointerCursorPath, false);
        yield return new Hotspot("CirclePlate", "Circle Plate",
            new Rect2(697, 105, 167, 210), new Vector2(780, 212),
            Sfx.DoorCreak, 1.1f, PointerCursorPath, false);
        yield return new Hotspot("LeftKeyhole", "Bridge Keyhole",
            new Rect2(327, 390, 100, 150), new Vector2(377, 465),
            Sfx.LockRattle, 0.9f, PointerCursorPath, false);
        yield return new Hotspot("CenterKeyhole", "Little Door Keyhole",
            new Rect2(522, 390, 105, 150), new Vector2(575, 465),
            Sfx.LockRattle, 1f, PointerCursorPath, false);
        yield return new Hotspot("RightKeyhole", "Circle Keyhole",
            new Rect2(723, 390, 104, 150), new Vector2(775, 465),
            Sfx.LockRattle, 1.1f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        _doorTexture = GD.Load<Texture2D>(BackgroundPath);

        // The authored scene still carries its old full-door Sprite2D. The
        // root now draws the correctly cropped door so the knock rings in
        // _Draw sit above the artwork instead of underneath that sprite.
        GetNodeOrNull<CanvasItem>("DoorCloseup2")?.Hide();

        _opened = GameState.Instance?.HasFlag(OpenedFlag) ?? false;
        _knockProgress = _opened ? KnockSequence.Length : 0;

        _statusLabel = CreateLabel(string.Empty,
            new Vector2(276, 38), 21,
            new Color(1f, 0.9f, 0.62f, 0.92f), new Vector2(600, 36));
        UpdateStatusLabel();
    }

    public override void _Draw()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        if (_doorTexture != null)
            DrawTextureRectRegion(_doorTexture, screen, DoorSourceRect, Colors.White);
        else
            DrawRect(screen, new Color(0.03f, 0.025f, 0.02f));

        var answered = new Color(1f, 0.88f, 0.52f, 0.92f);
        var answeredSoft = new Color(1f, 0.88f, 0.52f, 0.28f);
        Vector2 previous = Vector2.Zero;
        bool hasPrevious = false;

        for (int i = 0; i < _knockProgress && i < KnockSequence.Length; i++)
        {
            var hotspot = GetHotspot(KnockSequence[i]);
            if (hotspot == null)
                continue;

            if (hasPrevious)
            {
                DrawLine(previous + new Vector2(2, 2), hotspot.Center + new Vector2(2, 2),
                    new Color(0.03f, 0.02f, 0.01f, 0.45f), 5f, true);
                DrawLine(previous, hotspot.Center, answeredSoft, 2.2f, true);
            }

            float radius = hotspot.Id.EndsWith("Keyhole") ? 38f : 52f;
            DrawArc(hotspot.Center, radius, 0f, Mathf.Tau, 48, answered, 2.4f, true);
            DrawArc(hotspot.Center, radius - 7f, 0f, Mathf.Tau, 48, answeredSoft, 1.2f, true);
            DrawCircle(hotspot.Center, 3f, answered);

            previous = hotspot.Center;
            hasPrevious = true;
        }
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        if (_opened)
        {
            _ = ProceedThroughDoorAsync();
            return;
        }

        var activeVerb = MemoryVerbSystem.Active;
        if (activeVerb == MemoryVerb.Echo)
        {
            if (!SentenceIsComplete())
                _ = LockedDoorAsync();
            else
                HandleEchoKnock(hotspot);
            return;
        }

        if (activeVerb != MemoryVerb.None)
        {
            _ = WrongVerbAsync(activeVerb);
            return;
        }

        _ = ExamineDoorPartAsync(hotspot);
    }

    private bool SentenceIsComplete()
    {
        var gs = GameState.Instance;
        return gs != null && (gs.HasFlag(SentenceFlag) || gs.IsShardRecovered(3));
    }

    private void HandleEchoKnock(Hotspot hotspot)
    {
        if (_knockProgress >= KnockSequence.Length)
            return;

        string expected = KnockSequence[_knockProgress];
        if (hotspot.Id == expected)
        {
            PlayKnock(hotspot, _knockProgress);
            _knockProgress++;
            UpdateStatusLabel();
            QueueRedraw();

            if (_knockProgress >= KnockSequence.Length)
                _ = CompleteDoorAsync();
            return;
        }

        // A knock on the bridge always starts the pattern again, so a wrong
        // note mid-sequence does not require an extra throwaway click.
        if (hotspot.Id == KnockSequence[0])
        {
            _knockProgress = 0;
            PlayKnock(hotspot, 0);
            _knockProgress = 1;
            UpdateStatusLabel();
            QueueRedraw();
            GameState.Instance?.Toast("The knock begins again at the bridge.");
            return;
        }

        _ = WrongKnockAsync();
    }

    private void PlayKnock(Hotspot hotspot, int sequenceIndex)
    {
        float pitch = KnockPitches[sequenceIndex];
        AudioManager.PlayVaried(Sfx.WordChime, -6f, pitch, 0.015f);
        if (hotspot.Id.EndsWith("Keyhole"))
            AudioManager.Play(Sfx.LockRattle, -13f, pitch);
        else
            AudioManager.Play(Sfx.DoorCreak, -16f, 0.75f + sequenceIndex * 0.06f);

        MemoryVerbSystem.RegisterUse(MemoryVerb.Echo);
        Hotspots.SpawnRipple(hotspot.Center, new Color(1f, 0.88f, 0.55f));
    }

    private async Task ExamineDoorPartAsync(Hotspot hotspot)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            bool first = RecordExamine(hotspot);
            AudioManager.PlayVaried(hotspot.SfxId, -6f, hotspot.SfxPitch, 0.04f);
            if (first)
                await SaySequence(FirstLines(hotspot.Id));
            else
                await Say(RepeatLine(hotspot.Id), 3.5f, 23);
        }
        finally
        {
            Busy = false;
            QueueRedraw();
        }
    }

    private static string[] FirstLines(string id) => id switch
    {
        "BridgePlate" =>
        [
            "A bridge stamped in brass, water moving underneath whether anyone crosses or not.",
            "Directly below it waits a keyhole. The pairing feels deliberate: cross first, then turn what waits under the crossing.",
        ],
        "LeftKeyhole" =>
        [
            "The keyhole beneath the bridge. Its brass is polished brighter than the others, as if this one is always turned first.",
            "It belongs to the picture above it. Bridge, then bridge-key.",
        ],
        "DoorPlate" =>
        [
            "A little door set into the door, complete with its own impossible knob.",
            "After a crossing comes an entrance. Below the little door waits the middle keyhole.",
        ],
        "CenterKeyhole" =>
        [
            "The middle keyhole sits exactly beneath the little door. From inside comes the faintest smell of rain on a bedroom curtain.",
            "Little door, then its key. The door is teaching pairs, not a single row.",
        ],
        "CirclePlate" =>
        [
            "An empty brass circle. No face, no landscape, no way through except around again.",
            "What is crossed and entered has to be closed. One keyhole remains beneath the circle.",
        ],
        "RightKeyhole" =>
        [
            "The final keyhole, under the circle. Something behind it shifts — not a lock giving way, but a room deciding which year to be.",
            "Circle, then circle-key. That would close the knock.",
        ],
        _ => ["cold brass, patient under her fingers."],
    };

    private static string RepeatLine(string id) => id switch
    {
        "BridgePlate" => "Bridge first.",
        "LeftKeyhole" => "Then the keyhole beneath the bridge.",
        "DoorPlate" => "Then the little door.",
        "CenterKeyhole" => "Then the middle keyhole.",
        "CirclePlate" => "Then close the circle.",
        "RightKeyhole" => "Then the final keyhole.",
        _ => "cold brass.",
    };

    private async Task WrongKnockAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            bool hadProgress = _knockProgress > 0;
            _knockProgress = 0;
            UpdateStatusLabel();
            QueueRedraw();
            AudioManager.Play(Sfx.Denied, -8f, 0.86f);
            await Say(hadProgress
                ? "The note falls out of the pattern and the whole knock loosens. Begin again where crossing begins: the bridge."
                : "No answer. A knock has to begin somewhere — and this door begins with a crossing.", 4f, 23);
        }
        finally
        {
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
            AudioManager.Play(Sfx.Denied, -9f, 0.92f);
            string line = verb switch
            {
                MemoryVerb.Anchor => "You could hold this door still forever and it would only become a better-kept wall. A knock is movement, in order. It asks for Echo.",
                MemoryVerb.Trace => "There is no chain of causes to trace in the brass — only six answers waiting to sound. Echo knows this language.",
                MemoryVerb.Release => "There is nothing whole enough here to release. First let the door hear its knock.",
                _ => "That way of remembering does not fit a knock. Echo does.",
            };
            await Say(line, 4f, 23);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LockedDoorAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            AudioManager.Play(Sfx.LockRattle, -7f, 0.82f);
            await SaySequence(
                "The brass takes the sound and gives nothing back.",
                "This door will not listen until the hallway behind Mara has finished its sentence. Step back and Echo the photographs in order.");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task CompleteDoorAsync()
    {
        if (Busy || _opened)
            return;
        Busy = true;
        try
        {
            _opened = true;
            _knockProgress = KnockSequence.Length;
            GameState.Instance?.SetFlag(OpenedFlag);
            MemoryVerbSystem.ConsumeActive(MemoryVerb.Echo);

            AudioManager.Play(Sfx.DoorUnlock, -5f);
            AudioManager.Play(Sfx.PuzzleSolve, -8f, 1.06f);
            UpdateStatusLabel();
            QueueRedraw();

            await SaySequence(
                "Bridge. Key. Little door. Key. Circle. Key.",
                "Something on the other side has been knocking back, one answer at a time. At the final note, all three locks turn together — and beyond them, a bedroom cannot decide which year it is.");

            AudioManager.Play(Sfx.DoorCreak, -5f, 0.94f);
            await WaitSeconds(0.35f);
            SceneManager.ChangeScene(Room5ScenePath);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ProceedThroughDoorAsync()
    {
        if (Busy)
            return;
        Busy = true;
        AudioManager.Play(Sfx.DoorCreak, -6f, 0.98f);
        await WaitSeconds(0.2f);
        SceneManager.ChangeScene(Room5ScenePath);
        Busy = false;
    }

    private void UpdateStatusLabel()
    {
        if (_statusLabel == null)
            return;

        _statusLabel.Text = _opened
            ? "the way through stands open"
            : _knockProgress == 0
                ? "the door remembers a knock"
                : $"the knock holds — {_knockProgress} of {KnockSequence.Length}";
    }
}

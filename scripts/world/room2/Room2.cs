using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The sitting room's root layer.
///
/// Room furniture lives on its own ExamineHandler layers; this root
/// only carries the old arrow/photo reveal once the photograph is
/// Mara's — and one new piece of weather. When the photograph has
/// been flipped and studied in the drawer close-up, Photo1Reward
/// leaves a "difference_wind_pending" flag and sends her back here,
/// to the real room, standing in front of the real fire. The wind
/// beat belongs to THIS room's point of view: the gust, the fire
/// going out of the world, the dark taking the whole painting —
/// and then the other room, relit, with something off.
/// </summary>
public partial class Room2 : ExamineHandler
{
    private const string WindPendingFlag = "difference_wind_pending";
    private const string FireOutFlag = "difference_fire_out";
    private const string DifferenceScenePath = "res://scenes/world/difference_investigation.tscn";

    // The little door key, set down on the table once the other room
    // has been remembered right. It lives HERE, as the room root's
    // last child, on purpose: its first home was a child sprite of the
    // globe's Examine1 layer, and the examine machinery hides that
    // layer (children inherit the alpha) and frees it on restore — the
    // key was being unmade by the very system that let Mara look at
    // the globe. Its second home, a bare sprite heard only by this
    // root, kept it visible but left its clicks to be claimed by the
    // table layer underneath (_Input runs leaves-first, root last).
    // LittleDoorKeyPickup owns the click itself now; see its summary.
    private const string SolvedFlag = "difference_room2_solved";
    private const string DoorKeyFlag = "item_hall_key_door";
    private const string DoorKeyIconPath = "res://assets/images/core/items/door_key.png";

    // Four 16 px grid steps right and two up from the letter's old
    // spot (606, 456), as placed in playtesting: squarely on the
    // little table between the chairs, beside the globe.
    public static readonly Vector2 DoorKeyTablePosition = new Vector2(670, 424);
    public const float DoorKeyClickHalfExtent = 76f;
    public static Rect2 DoorKeyClickRect => new Rect2(
        DoorKeyTablePosition - Vector2.One * DoorKeyClickHalfExtent,
        Vector2.One * DoorKeyClickHalfExtent * 2f);

    private LittleDoorKeyPickup _doorKeyPickup;
    private bool _takingDoorKey;

    private static bool DoorKeyWaitingNow =>
        (GameState.Instance?.HasFlag(SolvedFlag) ?? false) &&
        !(GameState.Instance?.HasFlag(DoorKeyFlag) ?? false);

    private bool DoorKeyWaiting => DoorKeyWaitingNow;

    /// <summary>True while the waiting key owns this point of the
    /// sitting room. The examine layers under it check this and let
    /// the click pass untouched, so neither an examine nor the
    /// table's drawer shortcut can swallow the pickup.</summary>
    public static bool ShouldYieldToDoorKey(Vector2 globalPoint) =>
        SceneManager.Instance?.CurrentScene is Room2 &&
        DoorKeyWaitingNow &&
        DoorKeyClickRect.HasPoint(globalPoint);

    private bool _windPlaying;

    // The wind, made visible: pale streaks that tear left-to-right
    // across the real room while the gust lives. The animated fire
    // dies the instant the gust lands (see GameUI's fire-out gate) —
    // the flames are the first thing the wind takes, before the
    // dark has time to close over anything else.
    private readonly List<ColorRect> _windStreaks = new();
    private readonly RandomNumberGenerator _rng = new();
    private float _gustLeft;

    public override void _Ready()
    {
        // This root never calls base._Ready (it is not one of the
        // room's examine layers), so say out loud that the key's
        // click must reach _Input here.
        SetProcessInput(true);
        _rng.Randomize();
        BuildWindStreaks();
        // The wind beat first, before any legacy reveal bookkeeping:
        // a scene edit must never be able to eat the weather. (For
        // a long while this script was not attached to the scene at
        // all, and its Photo lived at "UI/Photo" besides — either
        // fault alone was enough to swallow the wind whole.)
        if (GameState.Instance?.HasFlag(WindPendingFlag) == true)
            _ = PlayWindBeatAsync();

        if (DoorKeyWaiting)
            LayDoorKeyOnTable();

        if (Globals.Instance != null && Globals.SHARDS_COLLECTED[1])
        {
            var photo = GetNodeOrNull<Sprite2D>("UI/Inventory/Photo");
            if (photo != null)
            {
                var photoColor = photo.Modulate;
                photoColor.A = 1.0f;
                photo.Modulate = photoColor;
            }

            // The room's old painted arrows stay folded away. They
            // never had a script to carry Mara anywhere; leaving is
            // the top-of-screen Leave button's work now (see GameUI).
        }
    }

    private async Task PlayWindBeatAsync()
    {
        if (_windPlaying)
            return;
        _windPlaying = true;

        var gs = GameState.Instance;
        gs?.ClearFlag(WindPendingFlag, save: false);

        Dialogue.BeginInputBlock();
        try
        {
            // A breath of the ordinary room first — that's the point.
            await WaitSeconds(0.85f);
            if (!StillHere())
                return;

            await Dialogue.ShowText(this,
                "The sitting room. Fire low, everything where it— wait. The curtains just moved. There is no window open in this house. There hasn't been for—",
                5.5f, 576f, 324f, fontSize: 24);
            if (!StillHere())
                return;

            // The narration says it first. Then the room does it —
            // sound, streaks, the bubble itself shivering — and the
            // flames are the first thing it takes.
            _ = FireGustAsync();
            await Dialogue.ShowText(this, "WHOOSH—", 2.0f, 576f, 324f, fontSize: 40);
            if (!StillHere())
                return;

            // Nothing said about the fire. The eyes have said it:
            // the fire is simply out, the way it happens in life —
            // no line stands between the wind and the dark. A held
            // breath in a fireless room… and then it comes again.
            await WaitSeconds(0.85f);
            if (!StillHere())
                return;
            AudioManager.Play(Sfx.WindGust, -4f, 0.85f);
            Dialogue.Gust(1.2f, 1.05f);
            _gustLeft = 1.7f;
            foreach (var streak in _windStreaks)
            {
                if (GodotObject.IsInstanceValid(streak))
                {
                    streak.Position = new Vector2(
                        _rng.RandfRange(-260f, 500f), _rng.RandfRange(36f, 580f));
                    streak.Visible = true;
                }
            }
            await WaitSeconds(0.45f);
            if (!StillHere())
                return;

            // Straight to black this time — the second gust takes
            // the rest of the light with it, all at once.
            var dark = new ColorRect
            {
                Color = new Color(0, 0, 0, 0),
                Position = Vector2.Zero,
                Size = new Vector2(1152, 648),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 4000,
            };
            AddChild(dark);
            var fade = CreateTween();
            fade.TweenProperty(dark, "color", new Color(0, 0, 0, 1),
                Settings.ReduceMotionOrDefault() ? 0.2f : 0.14f);
            await ToSignal(fade, Tween.SignalName.Finished);
            if (!StillHere())
                return;
            await WaitSeconds(0.5f);
            if (!StillHere())
                return;

            SceneManager.ChangeScene(DifferenceScenePath);
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    /// <summary>The gust itself, fired a breath after its name is
    /// written: sound first, the fire snuffed in the same instant,
    /// the wind made visible as streaks, and the speech bubble
    /// shaken like a thing held up into the weather.</summary>
    private async Task FireGustAsync()
    {
        await WaitSeconds(0.35f);
        if (!StillHere())
            return;

        // The flames go first — visibly before anything else can.
        GameState.Instance?.SetFlag(FireOutFlag, save: false);
        AudioManager.Play(Sfx.WindGust, -4f);
        AudioManager.Play(Sfx.FireWhoomph, -8f, 0.8f);
        Dialogue.Gust(2.3f, 1.15f);
        _gustLeft = 2.6f;
        foreach (var streak in _windStreaks)
        {
            if (GodotObject.IsInstanceValid(streak))
            {
                streak.Position = new Vector2(
                    _rng.RandfRange(-260f, 500f), _rng.RandfRange(36f, 580f));
                streak.Visible = true;
            }
        }
    }

    private void BuildWindStreaks()
    {
        for (int i = 0; i < 16; i++)
        {
            var streak = new ColorRect
            {
                Name = $"WindStreak{i}",
                Color = new Color(0.82f, 0.88f, 1f, 0f),
                Size = new Vector2(_rng.RandfRange(80f, 210f), _rng.RandfRange(2f, 4f)),
                Position = new Vector2(-300f, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 3990,
                Visible = false,
            };
            AddChild(streak);
            _windStreaks.Add(streak);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        UpdateDoorKeyProp();
        if (_gustLeft <= 0f)
            return;

        float dt = (float)delta;
        _gustLeft = Mathf.Max(0f, _gustLeft - dt);
        float envelope = Settings.ReduceMotionOrDefault()
            ? 0f
            : Mathf.Clamp(_gustLeft / 1.9f, 0f, 1f);
        foreach (var streak in _windStreaks)
        {
            if (!GodotObject.IsInstanceValid(streak))
                continue;
            streak.Visible = envelope > 0.01f;
            if (!streak.Visible)
                continue;
            streak.Position += new Vector2(640f * dt, 26f * dt);
            if (streak.Position.X > 1160f)
            {
                streak.Position = new Vector2(
                    -streak.Size.X - 12f, _rng.RandfRange(36f, 580f));
            }
            var color = streak.Color;
            color.A = 0.17f * envelope;
            streak.Color = color;
        }
    }

    // ------------------------------------------------------------------
    // The little door key on the table (see the constants above for
    // why it lives on the room root and nowhere near a layer).
    // ------------------------------------------------------------------

    private void LayDoorKeyOnTable()
    {
        var texture = GD.Load<Texture2D>(DoorKeyIconPath);
        if (texture == null)
            return;

        _doorKeyPickup = new LittleDoorKeyPickup
        {
            Name = "LittleDoorKeyPickup",
            // Over every painted layer in the room, under the HUD.
            ZIndex = 12,
        };
        // Aim for a key about 84 px tall on screen whatever the
        // nesting says (the icon sheet is 512 px).
        Vector2 parentScale = GlobalTransform.Scale;
        float s = 84f / 512f / Mathf.Max(0.01f, Mathf.Abs(parentScale.X));
        _doorKeyPickup.Configure(texture, Vector2.One * s);
        // Added last, on purpose — see LittleDoorKeyPickup: as this
        // room's final branch it hears _Input before the examine
        // layers painted underneath the key.
        AddChild(_doorKeyPickup);
        _doorKeyPickup.GlobalPosition = DoorKeyTablePosition;
    }

    private void UpdateDoorKeyProp()
    {
        if (_doorKeyPickup == null)
            return;
        if (!DoorKeyWaiting && !_takingDoorKey)
        {
            _doorKeyPickup.QueueFree();
            _doorKeyPickup = null;
        }
    }

    /// <summary>The one taking-gate, shared by the pickup's own
    /// leaves-first _Input and this root's fallback below.</summary>
    public bool TryBeginDoorKeyPickup()
    {
        if (_takingDoorKey || !DoorKeyWaiting ||
            Dialogue.IsInputBlocked || Dialogue.IsShowing)
            return false;

        _ = TakeDoorKeyAsync();
        return true;
    }

    public override void _Input(InputEvent @event)
    {
        // Fallback path: the pickup node normally claims this click
        // long before the root is reached. If it ever doesn't, the
        // examine layers have yielded the key's rectangle (see
        // ShouldYieldToDoorKey), so the click still lands here.
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed &&
            mouseEvent.ButtonIndex == MouseButton.Left &&
            _doorKeyPickup != null &&
            _doorKeyPickup.ContainsGlobalPoint(GetGlobalMousePosition()) &&
            TryBeginDoorKeyPickup())
        {
            GetTree().Root.SetInputAsHandled();
            return;
        }

        base._Input(@event);
    }

    private async Task TakeDoorKeyAsync()
    {
        var gs = GameState.Instance;
        if (_takingDoorKey || gs == null || gs.HasFlag(DoorKeyFlag))
            return;
        _takingDoorKey = true;
        try
        {
            Vector2 anchor = _doorKeyPickup?.GlobalPosition ?? DoorKeyTablePosition;
            AudioManager.Play(Sfx.KeyType, -8f, 0.95f);
            await Dialogue.ShowText(this,
                "Wait a minute. This was not here earlier.",
                3.8f, anchor.X, anchor.Y, fontSize: 26);
            if (!StillHere())
                return;
            await Dialogue.ShowText(this,
                "A key. Not a metaphor wearing key-coloured paint. Iron — and its bow is a little door, down to a knob too small for any hand but memory's.",
                5.5f, anchor.X, anchor.Y, fontSize: 24);
            if (!StillHere())
                return;
            await Dialogue.ShowText(this,
                "He taped it behind an evening and trusted I'd remember the room right. …I did, Dad. Just barely, with the ceiling coming down. But I did.",
                5.5f, anchor.X, anchor.Y, fontSize: 24);
            if (!StillHere())
                return;

            gs.SetFlag(DoorKeyFlag, save: false);
            gs.UnlockClue("hall_key_door");
            gs.Toast("Little door key — kept in Mara's bag (B). Somewhere ahead is a door with a little door stamped above its middle keyhole.");
            gs.SaveGame("little door key taken");
            if (_doorKeyPickup != null)
            {
                _doorKeyPickup.MarkTaken();
                _doorKeyPickup.QueueFree();
                _doorKeyPickup = null;
            }
        }
        finally
        {
            _takingDoorKey = false;
        }
    }

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();
}

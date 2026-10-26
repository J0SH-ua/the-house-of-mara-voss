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
        _rng.Randomize();
        BuildWindStreaks();
        // The wind beat first, before any legacy reveal bookkeeping:
        // a scene edit must never be able to eat the weather. (For
        // a long while this script was not attached to the scene at
        // all, and its Photo lived at "UI/Photo" besides — either
        // fault alone was enough to swallow the wind whole.)
        if (GameState.Instance?.HasFlag(WindPendingFlag) == true)
            _ = PlayWindBeatAsync();

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
            await Dialogue.ShowText(this, "WHOOSH—", 2.4f, 576f, 324f, fontSize: 40);
            if (!StillHere())
                return;

            // The words are still trembling when she answers them.
            Dialogue.Gust(1.3f, 0.55f);
            await Dialogue.ShowText(this,
                "—the fire. Gone, like a hand closed over it. The curtains are still swinging and there is no— all right. All right. Wherever this goes, I know what this room is supposed to look like. I have it on paper.",
                5.5f, 576f, 324f, fontSize: 24);
            if (!StillHere())
                return;

            // Only now does the dark get the rest of the room.
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
                Settings.ReduceMotionOrDefault() ? 0.35f : 0.9f);
            await ToSignal(fade, Tween.SignalName.Finished);
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

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();
}

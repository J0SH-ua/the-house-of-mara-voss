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
    private const string DifferenceScenePath = "res://scenes/world/difference_investigation.tscn";

    private bool _windPlaying;

    public override void _Ready()
    {
        if (Globals.Instance != null && Globals.SHARDS_COLLECTED[1])
        {
            var rightArrow = GetNode<Area2D>("UI/RightArrow");
            var leftArrow = GetNode<Area2D>("UI/LeftArrow");
            var photo = GetNode<Sprite2D>("UI/Photo");

            var rightArrowColor = rightArrow.Modulate;
            rightArrowColor.A = 1.0f;
            rightArrow.Modulate = rightArrowColor;

            var leftArrowColor = leftArrow.Modulate;
            leftArrowColor.A = 1.0f;
            leftArrow.Modulate = leftArrowColor;

            var photoColor = photo.Modulate;
            photoColor.A = 1.0f;
            photo.Modulate = photoColor;
        }

        if (GameState.Instance?.HasFlag(WindPendingFlag) == true)
            _ = PlayWindBeatAsync();
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

            AudioManager.Play(Sfx.WindGust, -6f);
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
                Settings.ReduceMotionOrDefault() ? 0.35f : 1.25f);
            await ToSignal(fade, Tween.SignalName.Finished);
            if (!StillHere())
                return;

            await Dialogue.ShowText(this,
                "—the fire. Gone. Like a hand closed over it. All right. All right. Wherever this goes, I know what this room is supposed to look like. I have it on paper.",
                5.5f, 576f, 324f, fontSize: 24);
            if (!StillHere())
                return;

            SceneManager.ChangeScene(DifferenceScenePath);
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();
}

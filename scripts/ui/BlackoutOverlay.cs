using Godot;

/// <summary>
/// The night the house went out of power, drawn over whichever memory
/// Mara is standing in.
///
/// GameUI builds this on canvas layer 1 — above the room paintings (0),
/// below the speech bubble (2) and every scrap of HUD (90) — so the
/// dark can swallow a whole room while dialogue, the Bag and the way
/// back stay readable, the way a voice stays findable in a blackout.
/// It only ever darkens the five remembered rooms themselves: close-ups
/// are lit by whatever she is holding to them, the Apartment's building
/// never lost power, and the duct and basement keep their own dark.
///
/// Without a flashlight the rooms are silhouettes. With her father's,
/// the beam follows the mouse — no click, no hold — in the same chunky
/// strip language as the basement's light. While the transient
/// "blackout_flickering" flag is up (a lamp dying, a migraine arriving)
/// the darkness judders instead of settling.
/// </summary>
public partial class BlackoutOverlay : Control
{
    /// <summary>The rooms that lose their light. Everything else either
    /// has its own darkness or was never wired into this house.</summary>
    private static readonly string[] DarkenableScenes =
    [
        "room_1.tscn", "room_2.tscn", "room_3.tscn", "room_4.tscn", "room_5.tscn",
        // The hallways between them lose their light too — the
        // flashlight trek is walked now, both ways: silhouettes
        // on the way to the kitchen, the beam on the way back.
        "journey_to_room_2.tscn", "journey_to_room_3.tscn", "journey_to_room_4.tscn",
    ];

    private Vector2 _beamPosition = new(576, 324);
    private bool _beamReady;
    private float _darkAlpha;
    private float _flickerLevel = 0.8f;
    private float _flickerTimer;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Name = "BlackoutOverlay";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
        _rng.Randomize();
    }

    /// <summary>The size to darken. A full-rect Control under a
    /// CanvasLayer should resolve to the viewport on its own — but
    /// "should" is not a rendering strategy, so the viewport itself
    /// is the fallback and the truth.</summary>
    private Vector2 ViewSize => Size.X > 10f && Size.Y > 10f ? Size : GetViewportRect().Size;

    private static string CurrentSceneFile()
    {
        string path = SceneManager.CurrentScenePath ?? string.Empty;
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Max((float)delta, 0f);
        var gs = GameState.Instance;
        bool inScope = gs != null &&
            System.Array.IndexOf(DarkenableScenes, CurrentSceneFile()) >= 0;
        bool flickering = inScope && gs.HasFlag("blackout_flickering");
        bool active = inScope && gs.HasFlag("blackout_active");

        if (flickering)
        {
            if (Settings.ReduceMotionOrDefault())
            {
                _flickerLevel = 0.72f;
            }
            else
            {
                _flickerTimer -= dt;
                if (_flickerTimer <= 0f)
                {
                    _flickerTimer = _rng.RandfRange(0.05f, 0.13f);
                    // A dying lamp mostly fails, and now and then almost
                    // catches — brief hopes included in the effect.
                    _flickerLevel = _rng.Randf() < 0.3f
                        ? _rng.RandfRange(0.08f, 0.35f)
                        : _rng.RandfRange(0.6f, 1f);
                }
            }
        }

        float target = flickering ? _flickerLevel : active ? 1f : 0f;
        float rate = flickering ? 22f : 1.5f;
        _darkAlpha = Mathf.MoveToward(_darkAlpha, target, rate * dt);

        // The beam is Mara hand: it follows the mouse directly,
        // smoothed the same way the basement's light is.
        Vector2 mouse = GetGlobalMousePosition();
        mouse = new Vector2(
            Mathf.Clamp(mouse.X, 0f, 1152f),
            Mathf.Clamp(mouse.Y, 0f, 648f));
        if (!_beamReady)
        {
            _beamPosition = mouse;
            _beamReady = true;
        }
        else
        {
            float weight = 1f - Mathf.Exp(-20f * dt);
            _beamPosition = _beamPosition.Lerp(mouse, Mathf.Clamp(weight, 0f, 1f));
        }

        Visible = _darkAlpha > 0.01f;
        if (Visible)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_darkAlpha <= 0.01f)
            return;

        var gs = GameState.Instance;
        bool flickering = gs?.HasFlag("blackout_flickering") ?? false;
        bool hasLight = gs?.HasFlag("item_flashlight") ?? false;

        if (flickering || !hasLight)
        {
            // Silhouettes only. She knows these rooms by heart; the
            // player has the cursor, the HUD, and the way back.
            DrawRect(new Rect2(Vector2.Zero, ViewSize),
                new Color(0.002f, 0.002f, 0.005f, 0.94f * _darkAlpha), true);
            return;
        }

        float radius = (gs?.HasFlag("basement_battery_taken") ?? false) ? 198f : 152f;
        // Three square-edged passes, darkest only far from the beam —
        // the same strips the basement uses, so one flashlight feels
        // like one flashlight everywhere it goes.
        DrawDarkOutside(_beamPosition, radius + 118f,
            new Color(0.002f, 0.002f, 0.004f, 0.66f * _darkAlpha));
        DrawDarkOutside(_beamPosition, radius + 54f,
            new Color(0.002f, 0.002f, 0.004f, 0.62f * _darkAlpha));
        DrawDarkOutside(_beamPosition, radius,
            new Color(0.001f, 0.001f, 0.003f, 0.72f * _darkAlpha));
        DrawArc(_beamPosition, radius * 0.97f, 0f, Mathf.Tau, 56,
            new Color(1f, 0.83f, 0.45f, 0.08f * _darkAlpha), 13f, true);
        DrawCircle(_beamPosition, 4f, new Color(1f, 0.9f, 0.62f, 0.24f * _darkAlpha));
    }

    private void DrawDarkOutside(Vector2 center, float radius, Color dark)
    {
        Vector2 view = ViewSize;
        float width = view.X;
        float height = view.Y;
        float top = Mathf.Clamp(center.Y - radius, 0f, height);
        float bottom = Mathf.Clamp(center.Y + radius, 0f, height);
        if (top > 0f)
            DrawRect(new Rect2(0, 0, width, top), dark, true);
        if (bottom < height)
            DrawRect(new Rect2(0, bottom, width, height - bottom), dark, true);

        const float stripHeight = 9f;
        for (float y = top; y < bottom; y += stripHeight)
        {
            float stripBottom = Mathf.Min(stripHeight + 1f, bottom - y);
            float centreY = y + stripBottom * 0.5f;
            float dy = centreY - center.Y;
            float half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            float leftEdge = Mathf.Clamp(center.X - half, 0f, width);
            float rightEdge = Mathf.Clamp(center.X + half, 0f, width);
            if (leftEdge > 0f)
                DrawRect(new Rect2(0, y, leftEdge, stripBottom), dark, true);
            if (rightEdge < width)
                DrawRect(new Rect2(rightEdge, y, width - rightEdge, stripBottom), dark, true);
        }
    }
}

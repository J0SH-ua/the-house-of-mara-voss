using Godot;

/// <summary>
/// The flashlight, made visible where Mara's hands are.
///
/// During the blackout, whatever she holds up in first person —
/// a fragment in her palm, a practical thing just taken, the
/// negatives lifted to be read — can be lit by the torch once
/// that torch has actually been put in her Bag. Its cone comes
/// off the bottom corner of the screen (or out of the flashlight
/// itself when that is the thing in her hand), and the player
/// aims it with the mouse, exactly as in the dark rooms, the
/// duct and the basement. Code-drawn in the game's chunky
/// language like the rest of its light: a run of widening discs
/// for the cone and a stepped ellipse where it lands. Owned by
/// GameUI's held-pickup showcase; strength fades in and out so
/// the beam never pops.
/// </summary>
public partial class HeldBeam : Control
{
    /// <summary>Where the light should be pointing (screen space).</summary>
    public Vector2 Target = new(576, 324);

    /// <summary>The off-screen corner the torch is held from.</summary>
    public Vector2 Source = new(84, 664);

    /// <summary>1 while the beam should burn, 0 to let it die.</summary>
    public float TargetStrength;

    /// <summary>True while the mouse is Mara's wrist: the target is
    /// wherever the player points, updated every frame.</summary>
    public bool PlayerControlled;

    private Vector2 _aim = new(576, 324);
    private float _strength;

    /// <summary>Where the smoothed beam is actually landing now.
    /// GameUI reads this to brighten the hand and held thing only
    /// by the light the player has put on them.</summary>
    public Vector2 Aim => _aim;

    /// <summary>The beam's present, still-fading strength.</summary>
    public float CurrentStrength => _strength;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    /// <summary>Start already looking at the target — no sweep in
    /// from wherever the beam last died.</summary>
    public void SnapToTarget()
    {
        _aim = Target;
    }

    /// <summary>Put the beam straight under the player's hand on
    /// the mouse, so control is theirs from the first frame.</summary>
    public void SnapToMouse()
    {
        Target = MouseInView();
        _aim = Target;
        QueueRedraw();
    }

    /// <summary>No fade, no residue: a torch that is not yet in
    /// the Bag does not get a dying afterglow either.</summary>
    public void TurnOffImmediately()
    {
        PlayerControlled = false;
        TargetStrength = 0f;
        _strength = 0f;
        Visible = false;
    }

    private Vector2 MouseInView()
    {
        Vector2 view = GetViewportRect().Size;
        Vector2 mouse = GetGlobalMousePosition();
        return new Vector2(
            Mathf.Clamp(mouse.X, 0f, view.X),
            Mathf.Clamp(mouse.Y, 0f, view.Y));
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        if (PlayerControlled)
            Target = MouseInView();
        float dt = (float)delta;
        float weight = 1f - Mathf.Exp(-9f * Mathf.Max(dt, 0f));
        _aim = _aim.Lerp(Target, Mathf.Clamp(weight, 0f, 1f));
        _strength = Mathf.MoveToward(_strength, TargetStrength, dt * 2.4f);
        if (TargetStrength <= 0f && _strength <= 0.001f)
        {
            Visible = false;
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_strength <= 0.001f)
            return;

        float flicker = Settings.ReduceMotionOrDefault()
            ? 1f
            : 0.95f + 0.05f * Mathf.Sin(Time.GetTicksMsec() / 57f);
        float s = _strength * flicker;
        var warm = new Color(1f, 0.88f, 0.6f);

        // The cone between her wrist and the held thing: discs
        // widening and thinning as the light travels.
        const int discs = 26;
        for (int i = 0; i <= discs; i++)
        {
            float t = i / (float)discs;
            Vector2 pos = Source.Lerp(_aim, t);
            float radius = Mathf.Lerp(7f, 118f, t * t * (3f - 2f * t));
            float alpha = Mathf.Lerp(0.16f, 0.045f, t) * s;
            DrawCircle(pos, radius, new Color(warm.R, warm.G, warm.B, alpha));
        }

        // Where it lands: a stepped, slightly squashed pool of
        // light — hottest at the middle, the way his big torch
        // burned through every bulb they owned.
        DrawSetTransform(_aim, 0f, new Vector2(1f, 0.74f));
        DrawCircle(Vector2.Zero, 190f, new Color(warm.R, warm.G, warm.B, 0.10f * s));
        DrawCircle(Vector2.Zero, 150f, new Color(warm.R, warm.G, warm.B, 0.13f * s));
        DrawCircle(Vector2.Zero, 108f, new Color(warm.R, warm.G, warm.B, 0.16f * s));
        DrawCircle(Vector2.Zero, 64f, new Color(1f, 0.93f, 0.72f, 0.20f * s));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // The lamp itself, glinting at the frame's edge.
        DrawCircle(Source, 16f, new Color(1f, 0.94f, 0.74f, 0.5f * s));
        DrawCircle(Source, 8f, new Color(1f, 0.98f, 0.86f, 0.75f * s));
    }
}

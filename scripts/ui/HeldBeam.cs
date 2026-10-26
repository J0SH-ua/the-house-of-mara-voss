using Godot;

/// <summary>
/// The flashlight, made visible where Mara's hands are.
///
/// During the blackout, whatever she holds up in first person —
/// a fragment in her palm, a practical thing just taken, the
/// negatives lifted to be read — is lit by the torch in her other
/// hand: a visible cone coming off the bottom corner of the
/// screen and pooling on her hand and the item in it. The aim
/// chases the held thing as it travels (palm, then held up
/// large), the way a wrist actually follows. Code-drawn in the
/// game's chunky language like the rest of its light: a run of
/// widening discs for the cone and a stepped ellipse where it
/// lands. Owned by GameUI's held-pickup showcase; strength fades
/// in and out so the beam never pops.
/// </summary>
public partial class HeldBeam : Control
{
    /// <summary>Where the light should be pointing (screen space).</summary>
    public Vector2 Target = new(576, 324);

    /// <summary>The off-screen corner the torch is held from.</summary>
    public Vector2 Source = new(84, 664);

    /// <summary>1 while the beam should burn, 0 to let it die.</summary>
    public float TargetStrength;

    private Vector2 _aim = new(576, 324);
    private float _strength;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    /// <summary>Start already looking at the held thing — no sweep
    /// in from wherever the beam last died.</summary>
    public void SnapToTarget()
    {
        _aim = Target;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
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

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Procedural broken-glass overlay for the memory-shatter ending.
/// Cracks are generated once as normalized line segments, then revealed in
/// generation order as <see cref="Fracture"/> goes from 0 to 1. Drawing is
/// resolution-independent and needs no texture that could stretch or clip.
/// </summary>
public partial class ShatterCrackOverlay : Control
{
    private readonly List<(Vector2 From, Vector2 To, float Width)> _segments = new();
    private readonly List<Vector2> _impacts = new();
    private float _fracture;

    public float Fracture => _fracture;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        GenerateCracks();
    }

    public void SetFracture(float value)
    {
        _fracture = Mathf.Clamp(value, 0f, 1f);
        Visible = _fracture > 0.005f;
        QueueRedraw();
    }

    public void ResetCracks()
    {
        SetFracture(0f);
    }

    private void GenerateCracks()
    {
        _segments.Clear();
        _impacts.Clear();

        var rng = new RandomNumberGenerator { Seed = 20261024UL };
        AddImpact(new Vector2(0.54f, 0.43f), 10, 0.62f, rng);
        AddImpact(new Vector2(0.79f, 0.20f), 8, 0.43f, rng);
        AddImpact(new Vector2(0.18f, 0.79f), 8, 0.48f, rng);
        AddImpact(new Vector2(0.27f, 0.14f), 6, 0.30f, rng);
    }

    private void AddImpact(Vector2 center, int rays, float reach, RandomNumberGenerator rng)
    {
        _impacts.Add(center);

        for (int ray = 0; ray < rays; ray++)
        {
            float angle = Mathf.Tau * ray / rays + rng.RandfRange(-0.18f, 0.18f);
            Vector2 point = center;
            int steps = rng.RandiRange(4, 7);
            float width = rng.RandfRange(1.7f, 2.8f);

            for (int step = 0; step < steps; step++)
            {
                angle += rng.RandfRange(-0.22f, 0.22f);
                float distance = reach * rng.RandfRange(0.10f, 0.20f);
                Vector2 next = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                next = new Vector2(Mathf.Clamp(next.X, -0.03f, 1.03f), Mathf.Clamp(next.Y, -0.03f, 1.03f));
                _segments.Add((point, next, Mathf.Max(0.75f, width - step * 0.28f)));

                // A short branch makes the break read as glass rather than a
                // tidy sunburst. Branches are appended here so later fracture
                // stages reveal the finer damage after the main spokes.
                if (step == 2 && rng.Randf() < 0.72f)
                    AddBranch(next, angle + rng.RandfRange(0.55f, 0.95f) * (rng.Randf() < 0.5f ? -1f : 1f), reach, rng);

                point = next;
            }
        }
    }

    private void AddBranch(Vector2 start, float angle, float reach, RandomNumberGenerator rng)
    {
        Vector2 point = start;
        int steps = rng.RandiRange(2, 3);
        for (int step = 0; step < steps; step++)
        {
            angle += rng.RandfRange(-0.18f, 0.18f);
            Vector2 next = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach * rng.RandfRange(0.07f, 0.13f);
            next = new Vector2(Mathf.Clamp(next.X, -0.03f, 1.03f), Mathf.Clamp(next.Y, -0.03f, 1.03f));
            _segments.Add((point, next, Mathf.Max(0.65f, 1.35f - step * 0.25f)));
            point = next;
        }
    }

    public override void _Draw()
    {
        if (_fracture <= 0.005f || Size.X <= 1f || Size.Y <= 1f)
            return;

        int visible = Mathf.CeilToInt(_segments.Count * _fracture);
        var shadow = new Color(0.005f, 0.004f, 0.003f, 0.82f);
        var edge = new Color(0.88f, 0.93f, 1.0f, 0.62f);
        var glint = new Color(1.0f, 0.98f, 0.90f, 0.34f);
        Vector2 offset = new(1.35f, 1.15f);

        for (int i = 0; i < visible; i++)
        {
            var seg = _segments[i];
            Vector2 from = ScalePoint(seg.From);
            Vector2 to = ScalePoint(seg.To);

            DrawLine(from, to, shadow, seg.Width + 2.1f, true);
            DrawLine(from + offset, to + offset, edge, Math.Max(0.75f, seg.Width * 0.52f), true);
            if (i % 5 == 0)
                DrawLine(from - offset * 0.55f, to - offset * 0.55f, glint, 0.65f, true);
        }

        int impactCount = Mathf.CeilToInt(_impacts.Count * Mathf.Clamp(_fracture * 1.35f, 0f, 1f));
        for (int i = 0; i < impactCount; i++)
        {
            Vector2 center = ScalePoint(_impacts[i]);
            DrawCircle(center, 5.5f, shadow);
            DrawArc(center, 8.5f, 0f, Mathf.Tau, 22, edge, 1.1f, true);
        }

        // At the final stage, suggest separated shards with a few closed,
        // angular facets around the main impact.
        if (_fracture > 0.82f && _impacts.Count > 0)
        {
            Vector2 c = ScalePoint(_impacts[0]);
            var facet = new Color(0.82f, 0.90f, 1f, 0.20f);
            Vector2[] ring =
            [
                c + new Vector2(-118f, -42f), c + new Vector2(-54f, -104f),
                c + new Vector2(38f, -82f), c + new Vector2(106f, -18f),
                c + new Vector2(72f, 76f), c + new Vector2(-16f, 102f),
                c + new Vector2(-96f, 54f),
            ];
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 a = ring[i];
                Vector2 b = ring[(i + 1) % ring.Length];
                DrawLine(a, b, facet, 1.0f, true);
                DrawLine(c, a, facet, 0.8f, true);
            }
        }
    }

    private Vector2 ScalePoint(Vector2 normalized)
    {
        return new Vector2(normalized.X * Size.X, normalized.Y * Size.Y);
    }
}

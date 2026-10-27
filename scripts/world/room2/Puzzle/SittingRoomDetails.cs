using Godot;

/// <summary>
/// The four small truths of the sitting room, drawn once and shared.
///
/// The Difference investigation and the photograph in Mara's bag must
/// agree about that evening *exactly* — the same clock, the same red
/// book, the same letter, the same cup — or the memory test stops
/// being fair. So both call here: the photograph holds the room as it
/// was (4:00, book on the left chair, letter on the table, cup
/// upright and steaming), and the crumbling room shows what the house
/// moved (4:47, book changed chairs, letter gone, cup over). All
/// coordinates live in the 1152×648 design space both canvases share.
/// </summary>
public static class SittingRoomDetails
{
    public const string BackgroundPath = "res://assets/images/bgs/Difference_SittingRoom_BG.png";

    /// <summary>The evening as the photograph kept it.</summary>
    public static void DrawCorrectSet(CanvasItem c)
    {
        DrawClockFace(c, blank: false, hour: 4f, minute: 0f);
        DrawBook(c, new Vector2(493, 439));
        DrawLetter(c, new Vector2(578, 438));
        DrawUprightCup(c, new Vector2(671, 448));
    }

    /// <summary>The evening as the house re-edited it.</summary>
    public static void DrawChangedSet(CanvasItem c)
    {
        DrawClockFace(c, blank: false, hour: 4f + 47f / 60f, minute: 47f);
        DrawBook(c, new Vector2(755, 446));
        // The letter is gone. Nothing is drawn on purpose.
        DrawTippedCup(c, new Vector2(671, 450));
    }

    public static void DrawClockFace(CanvasItem c, bool blank, float hour = 4f, float minute = 0f)
    {
        Vector2 center = new(209, 122);
        const float radius = 30f;
        var ink = new Color(0.13f, 0.1f, 0.08f);
        var face = new Color(0.93f, 0.87f, 0.72f);

        c.DrawCircle(center + new Vector2(2, 3), radius + 2f, new Color(0.05f, 0.035f, 0.025f, 0.55f));
        c.DrawCircle(center, radius, face);
        c.DrawArc(center, radius, 0f, Mathf.Tau, 48, new Color(0.34f, 0.22f, 0.13f), 3f, true);
        c.DrawArc(center, radius - 4f, 0f, Mathf.Tau, 44, new Color(0.13f, 0.1f, 0.08f, 0.35f), 1.2f, true);

        if (blank)
        {
            c.DrawCircle(center, 3f, new Color(0.13f, 0.1f, 0.08f, 0.22f));
            return;
        }

        for (int i = 0; i < 12; i++)
        {
            float angle = -Mathf.Pi / 2f + i * Mathf.Tau / 12f;
            Vector2 dir = Vector2.FromAngle(angle);
            float inner = i % 3 == 0 ? 20f : 23f;
            c.DrawLine(center + dir * inner, center + dir * 26f, ink, i % 3 == 0 ? 2.4f : 1.4f, true);
        }

        float hourAngle = -Mathf.Pi / 2f + (hour % 12f) * Mathf.Tau / 12f;
        float minuteAngle = -Mathf.Pi / 2f + (minute % 60f) * Mathf.Tau / 60f;
        c.DrawLine(center, center + Vector2.FromAngle(hourAngle) * 16f, ink, 4.2f, true);
        c.DrawLine(center, center + Vector2.FromAngle(minuteAngle) * 23f, ink, 3f, true);
        c.DrawCircle(center, 3.4f, ink);
    }

    public static void DrawBook(CanvasItem c, Vector2 topLeft)
    {
        var shadow = new Rect2(topLeft + new Vector2(3, 7), new Vector2(62, 15));
        c.DrawRect(shadow, new Color(0.03f, 0.02f, 0.015f, 0.42f), true);

        c.DrawRect(new Rect2(topLeft + new Vector2(4, 12), new Vector2(56, 7)),
            new Color(0.88f, 0.8f, 0.64f), true);
        c.DrawRect(new Rect2(topLeft + new Vector2(0, 5), new Vector2(60, 13)),
            new Color(0.48f, 0.13f, 0.11f), true);

        Vector2[] top =
        [
            topLeft + new Vector2(0, 5),
            topLeft + new Vector2(9, 0),
            topLeft + new Vector2(66, 0),
            topLeft + new Vector2(60, 5),
        ];
        c.DrawColoredPolygon(top, new Color(0.62f, 0.19f, 0.15f));
        c.DrawRect(new Rect2(topLeft + new Vector2(7, 5), new Vector2(3, 13)),
            new Color(0.3f, 0.07f, 0.06f), true);
        c.DrawRect(new Rect2(topLeft + new Vector2(49, 9), new Vector2(7, 5)),
            new Color(0.82f, 0.64f, 0.32f), true);
        c.DrawRect(new Rect2(topLeft + new Vector2(4, 17), new Vector2(56, 2)),
            new Color(0.08f, 0.05f, 0.04f, 0.7f), true);
    }

    public static void DrawLetter(CanvasItem c, Vector2 topLeft)
    {
        Vector2[] paper =
        [
            topLeft + new Vector2(0, 5),
            topLeft + new Vector2(54, 0),
            topLeft + new Vector2(58, 22),
            topLeft + new Vector2(4, 27),
        ];
        Vector2[] shadow =
        [
            paper[0] + new Vector2(2, 3),
            paper[1] + new Vector2(2, 3),
            paper[2] + new Vector2(2, 3),
            paper[3] + new Vector2(2, 3),
        ];
        c.DrawColoredPolygon(shadow, new Color(0.03f, 0.02f, 0.015f, 0.38f));
        c.DrawColoredPolygon(paper, new Color(0.9f, 0.84f, 0.68f));

        var edge = new Color(0.35f, 0.28f, 0.2f, 0.8f);
        for (int i = 0; i < paper.Length; i++)
            c.DrawLine(paper[i], paper[(i + 1) % paper.Length], edge, 1.2f, true);

        var ink = new Color(0.24f, 0.22f, 0.22f, 0.78f);
        c.DrawLine(topLeft + new Vector2(9, 8), topLeft + new Vector2(45, 4), ink, 1.4f, true);
        c.DrawLine(topLeft + new Vector2(10, 14), topLeft + new Vector2(39, 10), ink, 1.2f, true);
        c.DrawLine(topLeft + new Vector2(11, 20), topLeft + new Vector2(48, 16), ink, 1.2f, true);
    }

    public static void DrawUprightCup(CanvasItem c, Vector2 center)
    {
        DrawEllipse(c, center + new Vector2(0, 15), new Vector2(29, 7), new Color(0.28f, 0.2f, 0.15f));
        DrawEllipse(c, center + new Vector2(0, 14), new Vector2(27, 6), new Color(0.88f, 0.82f, 0.7f));

        Vector2[] cup =
        [
            center + new Vector2(-19, -10),
            center + new Vector2(19, -10),
            center + new Vector2(15, 11),
            center + new Vector2(-15, 11),
        ];
        c.DrawColoredPolygon(cup, new Color(0.9f, 0.84f, 0.71f));
        DrawEllipse(c, center + new Vector2(0, -10), new Vector2(19, 4.5f), new Color(0.32f, 0.17f, 0.08f));
        c.DrawArc(center + new Vector2(20, 0), 7f, -Mathf.Pi * 0.45f, Mathf.Pi * 0.45f, 18,
            new Color(0.9f, 0.84f, 0.71f), 3f, true);

        var steam = new Color(1f, 0.96f, 0.86f, 0.42f);
        c.DrawLine(center + new Vector2(-7, -16), center + new Vector2(-4, -24), steam, 1.6f, true);
        c.DrawLine(center + new Vector2(5, -16), center + new Vector2(8, -25), steam, 1.6f, true);
    }

    public static void DrawTippedCup(CanvasItem c, Vector2 center)
    {
        DrawEllipse(c, center + new Vector2(-16, 16), new Vector2(24, 5.5f), new Color(0.84f, 0.78f, 0.65f));
        DrawEllipse(c, center + new Vector2(8, 15), new Vector2(32, 7), new Color(0.3f, 0.15f, 0.065f, 0.92f));
        c.DrawRect(new Rect2(center + new Vector2(22, 14), new Vector2(5, 13)),
            new Color(0.3f, 0.15f, 0.065f, 0.75f), true);

        Vector2[] cup =
        [
            center + new Vector2(-24, -7),
            center + new Vector2(8, -14),
            center + new Vector2(13, 9),
            center + new Vector2(-18, 14),
        ];
        c.DrawColoredPolygon(cup, new Color(0.89f, 0.83f, 0.7f));
        DrawEllipse(c, center + new Vector2(11, -2), new Vector2(6, 12), new Color(0.32f, 0.17f, 0.08f));
        DrawEllipse(c, center + new Vector2(13, -2), new Vector2(3.5f, 8), new Color(0.9f, 0.84f, 0.71f));
        c.DrawArc(center + new Vector2(-23, -8), 7f, Mathf.Pi * 0.7f, Mathf.Pi * 1.55f, 18,
            new Color(0.89f, 0.83f, 0.7f), 3f, true);
    }

    public static void DrawEllipse(CanvasItem c, Vector2 center, Vector2 radii, Color color)
    {
        const int segments = 28;
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.Tau / segments;
            points[i] = center + new Vector2(Mathf.Cos(angle) * radii.X, Mathf.Sin(angle) * radii.Y);
        }
        c.DrawColoredPolygon(points, color);
    }
}

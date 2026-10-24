using System.Collections.Generic;
using Godot;

/// <summary>
/// A soft pulsing ring drawn over an interactive hotspot.
/// Two flavours: a looping pulse (hold-Tab reveal, hints) and a one-shot
/// expanding ripple (examine click feedback). Drawn with _Draw so it needs
/// no textures and works in any room's SubViewport coordinate space.
/// </summary>
public partial class HotspotMarker : Node2D
{
    public bool OneShot;
    public float Lifetime = 0.55f;
    public Color RingColor = new(1f, 0.92f, 0.75f, 0.9f);

    private double _time;

    public override void _Ready()
    {
        ZIndex = 4000;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (OneShot && _time >= Lifetime)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        bool reduce = Settings.ReduceMotionOrDefault();

        if (OneShot)
        {
            float t = Mathf.Clamp((float)(_time / Lifetime), 0f, 1f);
            float radius = Mathf.Lerp(6f, 34f, 1f - (1f - t) * (1f - t));
            var c = RingColor;
            c.A *= 1f - t;
            DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 40, c, 2.0f, true);
            DrawArc(Vector2.Zero, radius * 0.62f, 0, Mathf.Tau, 32, c, 1.2f, true);
            return;
        }

        float pulse = reduce ? 0f : (float)System.Math.Sin(_time * 3.2) * 3.5f;
        float r = 17f + pulse;
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 40, RingColor, 2.0f, true);
        var inner = RingColor;
        inner.A *= 0.45f;
        DrawArc(Vector2.Zero, r * 0.55f, 0, Mathf.Tau, 28, inner, 1.2f, true);
        DrawCircle(Vector2.Zero, 2.2f, RingColor);
    }
}

/// <summary>
/// Finds still-interactive examine hotspots in the current room and marks
/// them. Powers hold-Tab "look" (accessibility / anti-pixel-hunting) and
/// level-3 hints. Markers are parented to the room scene itself so they
/// share its SubViewport coordinates — no viewport mapping maths needed.
/// </summary>
public static class Hotspots
{
    private static readonly List<HotspotMarker> ActiveMarkers = new();
    private static readonly Dictionary<string, HotspotMarker> PersistentMarkers = new(); // "Room|Object"
    private static Node _markerScene;

    public static bool IsRevealing => ActiveMarkers.Count > 0;

    /// <summary>Mark every visible, not-yet-finished examine layer in the room.</summary>
    public static void RevealAll()
    {
        HideAll();

        var scene = SceneManager.Instance?.CurrentScene;
        if (scene == null)
            return;
        _markerScene = scene;

        string room = GameState.CurrentRoomName();
        foreach (var layer in FindExamineLayers(scene))
        {
            if (!IsLayerActive(layer))
                continue;

            // Skip objects already fully examined (Finals stay marked — they're the exit).
            string objectName = layer.GetParent()?.Name.ToString() ?? string.Empty;
            bool isFinal = objectName.EndsWith("Final");
            if (!isFinal && room != null && RoomExamineTracker.HasObjectBeenFullyExamined(room, objectName))
                continue;

            SpawnMarker(scene, LayerCenter(layer), oneShot: false);
        }
    }

    /// <summary>Mark only the named object (level-3 hint).</summary>
    public static void RevealObject(string objectName, float seconds = 3.5f)
    {
        var scene = SceneManager.Instance?.CurrentScene;
        if (scene == null || string.IsNullOrEmpty(objectName))
            return;

        foreach (var layer in FindExamineLayers(scene))
        {
            if (layer.GetParent()?.Name.ToString() != objectName)
                continue;
            if (!IsLayerActive(layer))
                continue;

            var marker = SpawnMarker(scene, LayerCenter(layer), oneShot: false, temporarySeconds: seconds);
            if (marker != null)
                return;
        }
    }

    /// <summary>One-shot ripple at an arbitrary position (examine click feedback).</summary>
    public static void SpawnRipple(Vector2 globalPosition)
    {
        var scene = SceneManager.Instance?.CurrentScene;
        if (scene == null)
            return;

        SpawnMarker(scene, globalPosition, oneShot: true, track: false);
    }

    public static void HideAll()
    {
        foreach (var marker in ActiveMarkers)
        {
            if (GodotObject.IsInstanceValid(marker))
                marker.QueueFree();
        }
        ActiveMarkers.Clear();
        _markerScene = null;
    }

    /// <summary>Drop markers whose room has been unloaded.</summary>
    public static void ValidateScene()
    {
        if (_markerScene != null && !GodotObject.IsInstanceValid(_markerScene))
            HideAll();
        else if (_markerScene != null && _markerScene != SceneManager.Instance?.CurrentScene)
            HideAll();
    }

    /// <summary>Rings left by a Recollection: clarity-blue, they stay until
    /// their object is faced, and reappear when the room is re-entered.
    /// Driven by GameState's persistent reveal set; call on scene change and
    /// whenever an examine lands. (HideAll deliberately leaves these alone.)</summary>
    public static void RefreshPersistent()
    {
        var scene = SceneManager.Instance?.CurrentScene;
        string room = GameState.CurrentRoomName();
        var gs = GameState.Instance;
        if (scene == null || room == null || gs == null)
        {
            ClearPersistentMarkers();
            return;
        }

        var stale = new List<string>();
        foreach (var (key, marker) in PersistentMarkers)
        {
            string obj = key.Substring(key.IndexOf('|') + 1);
            bool wanted = key.StartsWith(room + "|")
                          && GodotObject.IsInstanceValid(marker)
                          && marker.GetParent() == scene
                          && gs.IsRevealedPersistent(room, obj)
                          && RoomExamineTracker.GetClickedLayers(room, obj) == null;
            if (!wanted)
                stale.Add(key);
        }
        foreach (var key in stale)
        {
            if (GodotObject.IsInstanceValid(PersistentMarkers[key]))
                PersistentMarkers[key].QueueFree();
            PersistentMarkers.Remove(key);
        }

        foreach (var layer in FindExamineLayers(scene))
        {
            string obj = layer.GetParent()?.Name.ToString() ?? string.Empty;
            string key = $"{room}|{obj}";
            if (!gs.IsRevealedPersistent(room, obj) || PersistentMarkers.ContainsKey(key))
                continue;
            if (RoomExamineTracker.GetClickedLayers(room, obj) != null)
                continue;
            if (!IsLayerActive(layer))
                continue;

            var marker = SpawnMarker(scene, LayerCenter(layer), oneShot: false, track: false);
            if (marker != null)
            {
                marker.RingColor = new Color(0.72f, 0.88f, 1f, 0.95f);
                PersistentMarkers[key] = marker;
            }
        }
    }

    private static void ClearPersistentMarkers()
    {
        foreach (var marker in PersistentMarkers.Values)
        {
            if (GodotObject.IsInstanceValid(marker))
                marker.QueueFree();
        }
        PersistentMarkers.Clear();
    }

    // ------------------------------------------------------------------ helpers

    private static HotspotMarker SpawnMarker(Node scene, Vector2 globalPos, bool oneShot,
        bool track = true, float temporarySeconds = 0f)
    {
        var marker = new HotspotMarker { OneShot = oneShot };
        scene.AddChild(marker);
        marker.GlobalPosition = globalPos;

        if (!oneShot && track)
            ActiveMarkers.Add(marker);

        if (!oneShot && temporarySeconds > 0f)
        {
            var captured = marker;
            scene.GetTree().CreateTimer(temporarySeconds).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(captured))
                {
                    ActiveMarkers.Remove(captured);
                    captured.QueueFree();
                }
            };
        }

        return marker;
    }

    private static bool IsLayerActive(TileMapLayer layer)
    {
        // Visibility in this project is driven by Modulate/SelfModulate alpha
        // (see ExamineHelper.SetAlpha), plus the plain Visible flag.
        return layer.Visible && layer.Modulate.A > 0.5f && layer.SelfModulate.A > 0.5f;
    }

    private static Vector2 LayerCenter(TileMapLayer layer)
    {
        Rect2I used = layer.GetUsedRect();
        if (used.Size.X <= 0 || used.Size.Y <= 0)
            return layer.GlobalPosition;

        Vector2I centerCell = used.Position + used.Size / 2;
        return layer.ToGlobal(layer.MapToLocal(centerCell));
    }

    private static IEnumerable<ExamineHandler> FindExamineLayers(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is ExamineHandler handler)
                yield return handler;
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
    }
}

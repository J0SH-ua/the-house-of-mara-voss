using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Layer bookkeeping for the examine/clarify system.
/// State changes (charge spend, cursors, sounds) are delegated to
/// ClaritySystem/GameState so this class only moves TileMapLayers around.
/// </summary>
public static class ExamineHelper
{
    // -------------------------------------------------------------------------
    // EXAMINE CYCLING
    // Examines are named Examine1, Examine2, Examine3, ...
    // Each click advances to the next. At the last one, stay there on repeat clicks.
    // Earlier layers are freed once we advance past them.
    // -------------------------------------------------------------------------
    public static void CycleExamine(TileMapLayer clicked)
    {
        if (clicked == null)
            return;

        // Collect all sibling Examine layers (same parent), sorted by number.
        var parent = clicked.GetParent();
        if (parent == null)
            return;

        var examineLayers = GetSortedExamineLayers(parent);
        if (examineLayers.Count == 0)
            return;

        // Find which layer is currently visible.
        int currentIndex = FindVisibleIndex(examineLayers);

        if (currentIndex == -1)
        {
            // Nothing visible yet — show the first one.
            ShowLayer(examineLayers[0]);
            return;
        }

        int lastIndex = examineLayers.Count - 1;

        if (currentIndex >= lastIndex)
        {
            // Already at the last examine — stay here, do nothing more.
            return;
        }

        // Advance to next.
        HideLayer(examineLayers[currentIndex]);
        ShowLayer(examineLayers[currentIndex + 1]);

        // Free all layers before the one we just showed so memory is cleaned up.
        FreeLayersBefore(examineLayers, currentIndex + 1);
    }

    /// <summary>
    /// Re-entered rooms: rebuild an object's layer state from the tracker's
    /// record of clicked layers so it looks (and corrupts) exactly as it did
    /// when the player left. Clicking layer N shows N+1, so the visible layer
    /// is one past the deepest click, capped at the last layer; earlier
    /// layers are freed just like CycleExamine leaves them.
    /// </summary>
    public static void RestoreLayers(TileMapLayer anyLayer, IReadOnlyCollection<string> clickedLayerNames)
    {
        if (anyLayer == null || clickedLayerNames == null || clickedLayerNames.Count == 0)
            return;

        var parent = anyLayer.GetParent();
        if (parent == null)
            return;

        var layers = GetSortedExamineLayers(parent);
        if (layers.Count == 0)
            return;

        int deepestClicked = 0;
        foreach (var name in clickedLayerNames)
            deepestClicked = System.Math.Max(deepestClicked, ExtractExamineNumber(name));
        if (deepestClicked <= 0)
            return;

        int visibleIndex = System.Math.Min(deepestClicked, layers.Count - 1);
        for (int i = 0; i < layers.Count; i++)
        {
            if (i == visibleIndex)
                ShowLayer(layers[i]);
            else
                HideLayer(layers[i]);
        }
        FreeLayersBefore(layers, visibleIndex);
    }

    // -------------------------------------------------------------------------
    // CLARIFY
    // There is exactly one Clarify1 layer per object.
    // When triggered: hide/free all sibling Examine layers, then show Clarify1.
    // -------------------------------------------------------------------------

    /// <summary>True when the clicked object actually has something to reveal.</summary>
    public static bool HasClarifyLayer(TileMapLayer clicked)
    {
        if (clicked == null)
            return false;

        var parent = clicked.GetParent();
        return (parent != null && FindClarifyLayer(parent) != null) || FindClarifyLayer(clicked) != null;
    }

    public static void TriggerClarify(TileMapLayer clicked)
    {
        RevealClarifyLayer(clicked, spendCharge: true);
    }

    /// <summary>
    /// Apply exactly the visual change a single Clarity reveal would cause,
    /// without spending another charge or granting corruption relief. Used by
    /// Recollection (which has already spent every charge) and by room
    /// re-entry so a revealed object does not revert to its Examine art.
    /// Returns the now-visible Clarify layer, or null when none exists.
    /// </summary>
    public static TileMapLayer ApplyClarifyVisual(TileMapLayer clicked)
    {
        return RevealClarifyLayer(clicked, spendCharge: false);
    }

    private static TileMapLayer RevealClarifyLayer(TileMapLayer clicked, bool spendCharge)
    {
        if (clicked == null)
            return null;

        var parent = clicked.GetParent();
        if (parent == null)
            return null;

        // Find Clarify1 among siblings or children of clicked.
        TileMapLayer clarifyLayer = FindClarifyLayer(parent) ?? FindClarifyLayer(clicked);

        if (clarifyLayer == null)
        {
            // No Clarify layer found — leave Clarity ARMED (ClaritySystem owns
            // the mode now; a stray click must not silently eat the arm).
            Logger.Info("[ExamineHelper] No Clarify1 found; clarity stays armed.");
            return null;
        }

        // Hide and free all sibling Examine layers.
        var examineLayers = GetSortedExamineLayers(parent);
        foreach (var layer in examineLayers)
            FreeExamineLayer(layer);

        ShowLayer(clarifyLayer);
        if (spendCharge)
            ClaritySystem.ConsumeCharge();
        return clarifyLayer;
    }

    // -------------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all TileMapLayer children of <paramref name="parent"/> whose names
    /// start with "Examine", sorted by their trailing number.
    /// </summary>
    private static List<TileMapLayer> GetSortedExamineLayers(Node parent)
    {
        return parent.GetChildren()
            .OfType<TileMapLayer>()
            .Where(t => t.Name.ToString().StartsWith("Examine"))
            .OrderBy(t => ExtractExamineNumber(t.Name.ToString()))
            .ToList();
    }

    /// <summary>
    /// Finds the first TileMapLayer child of <paramref name="parent"/> named "Clarify1".
    /// </summary>
    private static TileMapLayer FindClarifyLayer(Node parent)
    {
        return parent.GetChildren()
            .OfType<TileMapLayer>()
            .FirstOrDefault(t => t.Name.ToString() == "Clarify1");
    }

    private static int FindVisibleIndex(List<TileMapLayer> layers)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (IsVisible(layers[i]))
                return i;
        }
        return -1;
    }

    public static void ShowLayer(TileMapLayer layer)
    {
        if (layer == null)
            return;

        SetAlpha(layer, 1.0f);

        // Special case: showing Clarify1 on Toast removes Cup2 from the scene.
        if (layer.Name.ToString() == "Clarify1" &&
            layer.GetParent()?.Name.ToString() == "Toast")
        {
            var cup2 = SceneManager.Instance?.CurrentScene?.GetNodeOrNull<TileMapLayer>("Objects/Cup2");
            cup2?.QueueFree();
        }
    }

    public static void HideLayer(TileMapLayer layer)
    {
        if (layer == null)
            return;

        SetAlpha(layer, 0.0f);
    }

    private static void SetAlpha(CanvasItem item, float alpha)
    {
        var mod = item.Modulate;
        mod.A = alpha;
        item.Modulate = mod;

        var selfMod = item.SelfModulate;
        selfMod.A = alpha;
        item.SelfModulate = selfMod;
    }

    private static bool IsVisible(CanvasItem item)
    {
        // Consider visible when both Modulate and SelfModulate alpha are fully opaque.
        return item.Modulate.A >= 0.9f && item.SelfModulate.A >= 0.9f;
    }

    /// <summary>
    /// Extracts the trailing integer from an "ExamineN" layer name.
    /// Always parses from the literal prefix "Examine" regardless of IS_CLARIFYING,
    /// so sorting is never corrupted by global clarify state.
    /// </summary>
    public static int ExtractExamineNumber(string layerName)
    {
        const string prefix = "Examine";
        if (layerName.StartsWith(prefix) &&
            int.TryParse(layerName.Substring(prefix.Length), out int number))
            return number;
        return 0;
    }

    /// <summary>
    /// Frees layers at indices 0..(keepFromIndex - 1).
    /// </summary>
    private static void FreeLayersBefore(List<TileMapLayer> layers, int keepFromIndex)
    {
        for (int i = 0; i < keepFromIndex; i++)
            FreeExamineLayer(layers[i]);
    }

    private static void FreeExamineLayer(TileMapLayer layer)
    {
        if (layer == null || !GodotObject.IsInstanceValid(layer))
            return;

        // Disable all processing on ExamineHandler subclasses before freeing
        // so their _ExitTree cursor cleanup runs correctly.
        if (layer is ExamineHandler handler)
        {
            handler.HoverEnabled = false;
            handler.SetProcessInput(false);
            handler.SetProcess(false);
        }

        layer.QueueFree();
    }
}

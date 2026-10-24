using Godot;

/// <summary>
/// The Clarity mechanic as one reusable service, shared by the in-room
/// ClarityToggle tiles, the toggle_clarity hotkey and GameUI.
///
/// Rules (fixed/improved over the original):
///  * Arming costs nothing; the charge is spent only on a successful reveal.
///  * All charges are usable — the old `count &lt;= 1` guard silently ate the last one.
///  * Right-click / re-toggling disarms for free, from anywhere.
///  * Clicking something that cannot be clarified keeps Clarity armed
///    (with a denied cue) instead of silently cancelling the mode.
///  * Successful reveals also steady the memory: corruption -2 (GameState).
/// </summary>
public static class ClaritySystem
{
    public const string EyeCursorPath = "res://assets/images/core/cursor_eye.png";

    private static ulong _lastDeniedMsec;

    public static bool IsArmed => Globals.Instance?.IS_CLARIFYING ?? false;

    /// <summary>Arm, or disarm if already armed. Returns the new armed state.</summary>
    public static bool Toggle()
    {
        if (Globals.Instance == null || Dialogue.IsInputBlocked)
            return IsArmed;

        if (IsArmed)
        {
            Disarm();
            return false;
        }

        if (Globals.Instance.CLARITY_TOGGLE_COUNT <= 0)
        {
            AudioManager.Play(Sfx.Denied, -4f);
            GameState.Instance?.Toast(GameState.Instance?.CollapseActive == true
                ? "No clarity remains — and the house is counting. Find what's left. Open the lock."
                : "No clarity remains. Finish a room to recover some.");
            Logger.Info("No clarity toggles left!");
            return false;
        }

        // A verb and Clarity are different ways of attending; only one can
        // be armed at a time.
        MemoryVerbSystem.Cancel(silent: true);

        // Apply the eye cursor BEFORE setting IS_CLARIFYING: CursorHelper.ApplyCursor
        // deliberately refuses to run while clarifying so hover code can't override it.
        CursorHelper.ApplyCursor(EyeCursorPath, 35, 35);
        Globals.Instance.IS_CLARIFYING = true;
        AudioManager.Play(Sfx.ClarifyArm, -5f);
        GameState.Instance?.Toast("Clarity — choose what to see truly. Right-click to release it.");
        return true;
    }

    /// <summary>Release Clarity without spending the charge.</summary>
    public static void Disarm(bool silent = false)
    {
        if (Globals.Instance == null || !Globals.Instance.IS_CLARIFYING)
            return;

        Globals.Instance.IS_CLARIFYING = false;
        CursorHelper.ResetCursor();
        if (!silent)
            AudioManager.Play(Sfx.UiBack, -8f);
    }

    /// <summary>
    /// Attempt to spend Clarity on the clicked layer.
    /// True = reveal happened and the charge was spent.
    /// False = nothing clarifiable there; Clarity stays armed.
    /// </summary>
    public static bool TryReveal(TileMapLayer clicked)
    {
        if (!ExamineHelper.HasClarifyLayer(clicked))
        {
            ulong now = Time.GetTicksMsec();
            if (now - _lastDeniedMsec > 1200)
            {
                _lastDeniedMsec = now;
                AudioManager.Play(Sfx.Denied, -10f, 1.15f);
                GameState.Instance?.Toast("Nothing here wants to be seen clearly.");
            }
            return false;
        }

        ExamineHelper.TriggerClarify(clicked);
        return true;
    }

    /// <summary>Spend one charge and sync the in-room toggle tiles + HUD + corruption relief.</summary>
    public static void ConsumeCharge()
    {
        if (Globals.Instance == null)
            return;

        Globals.Instance.IS_CLARIFYING = false;
        Globals.Instance.CLARITY_TOGGLE_COUNT =
            Mathf.Max(0, Globals.Instance.CLARITY_TOGGLE_COUNT - 1);
        CursorHelper.ResetCursor();

        int newCount = Globals.Instance.CLARITY_TOGGLE_COUNT;
        int oldCount = newCount + 1;

        var scene = SceneManager.Instance?.CurrentScene;
        var oldToggle = scene?.GetNodeOrNull<TileMapLayer>($"UI/ClarityToggle/{oldCount}");
        var newToggle = scene?.GetNodeOrNull<TileMapLayer>($"UI/ClarityToggle/{newCount}");
        if (oldToggle != null) SetAlpha(oldToggle, 0.0f);
        if (newToggle != null) SetAlpha(newToggle, 1.0f);

        AudioManager.Play(Sfx.ClarifyReveal, -4f);
        GameState.Instance?.RegisterClarityUsed();
        Logger.Info($"[ClaritySystem] Clarity used. Toggle count now: {newCount}");
    }

    /// <summary>Sync the tile counter after refills (room completion) / room loads.</summary>
    public static void RefreshToggleTiles()
    {
        if (Globals.Instance == null)
            return;

        var scene = SceneManager.Instance?.CurrentScene;
        if (scene == null)
            return;

        int count = Globals.Instance.CLARITY_TOGGLE_COUNT;
        for (int i = 0; i <= Globals.MaxClarity; i++)
        {
            var tile = scene.GetNodeOrNull<TileMapLayer>($"UI/ClarityToggle/{i}");
            if (tile != null)
                SetAlpha(tile, i == count ? 1.0f : 0.0f);
        }
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
}

using Godot;

/// <summary>
/// Arm/cancel/consume handling for the four late-game Memory Verbs.
/// GameState owns unlocks and persistence; this service owns player-facing
/// mode behaviour (cursor, sound, instruction toast) so room scripts only
/// ask "which verb is active?" when an object is used.
/// </summary>
public static class MemoryVerbSystem
{
    private const string EyeCursor = "res://assets/images/core/cursor_eye.png";
    private const string PointerCursor = "res://assets/images/core/cursor_pointer.png";
    private const string SearchCursor = "res://assets/images/core/cursor_magnifying_glass.png";

    public static MemoryVerb Active => GameState.Instance?.ActiveVerb ?? MemoryVerb.None;

    public static bool IsArmed(MemoryVerb verb) => verb != MemoryVerb.None && Active == verb;

    public static string DisplayName(MemoryVerb verb) => GameState.VerbDisplayName(verb);

    public static string HotkeyLabel(MemoryVerb verb) => verb switch
    {
        MemoryVerb.Echo => "E",
        MemoryVerb.Anchor => "A",
        MemoryVerb.Trace => "T",
        MemoryVerb.Release => "R",
        _ => string.Empty,
    };

    public static string Description(MemoryVerb verb) => verb switch
    {
        MemoryVerb.Echo => "Hear the order a memory still keeps.",
        MemoryVerb.Anchor => "Hold one true thing still while the room changes around it.",
        MemoryVerb.Trace => "Connect linked moments in the order they happened.",
        MemoryVerb.Release => "Let a completed memory stop being a room Mara has to live in.",
        _ => string.Empty,
    };

    private static string Instruction(MemoryVerb verb) => verb switch
    {
        MemoryVerb.Echo => "Echo — drift across what still has a voice. Right-click to stop listening.",
        MemoryVerb.Anchor => "Anchor — choose the one thing that has not moved. Right-click to let go.",
        MemoryVerb.Trace => "Trace — click linked memories in the order they happened. Right-click to break the line.",
        MemoryVerb.Release => "Release — choose the completed memory. Right-click if Mara is not ready.",
        _ => string.Empty,
    };

    private static string CursorFor(MemoryVerb verb) => verb switch
    {
        MemoryVerb.Echo => EyeCursor,
        MemoryVerb.Anchor => PointerCursor,
        MemoryVerb.Trace => SearchCursor,
        MemoryVerb.Release => PointerCursor,
        _ => PointerCursor,
    };

    /// <summary>Toggle a verb. Locked verbs explain themselves rather than failing silently.</summary>
    public static bool Toggle(MemoryVerb verb)
    {
        var gs = GameState.Instance;
        if (gs == null || verb == MemoryVerb.None || Dialogue.IsInputBlocked)
            return false;

        if (!gs.HasVerb(verb))
        {
            AudioManager.Play(Sfx.Denied, -7f);
            gs.Toast(verb switch
            {
                MemoryVerb.Echo => "Echo is still beyond the hallway. Recover the preserved fragment first.",
                MemoryVerb.Anchor => "Anchor is learned by hearing the hallway sentence whole.",
                MemoryVerb.Trace => "Trace is learned when one true thing can be held still.",
                MemoryVerb.Release => "Release comes only after the last afternoon has been traced.",
                _ => "That way of remembering has not been learned yet.",
            });
            return false;
        }

        if (Active == verb)
        {
            Cancel();
            return false;
        }

        ClaritySystem.Disarm(silent: true);
        gs.SetActiveVerb(verb);
        CursorHelper.ApplyCursor(CursorFor(verb), 32, 32);
        AudioManager.Play(verb == MemoryVerb.Echo ? Sfx.WordChime : Sfx.ClarifyArm, -6f,
            verb switch { MemoryVerb.Anchor => 0.88f, MemoryVerb.Trace => 1.05f, MemoryVerb.Release => 1.15f, _ => 1f });
        gs.Toast(Instruction(verb));
        return true;
    }

    public static void Cancel(bool silent = false)
    {
        var gs = GameState.Instance;
        if (gs == null || gs.ActiveVerb == MemoryVerb.None)
            return;

        // Clear the mode first: CursorHelper preserves a verb cursor while
        // a verb is active, by design.
        gs.SetActiveVerb(MemoryVerb.None);
        CursorHelper.ResetCursor();
        if (!silent)
            AudioManager.Play(Sfx.UiBack, -8f);
    }

    /// <summary>Consume the active mode after a room has successfully used it.</summary>
    public static bool ConsumeActive(MemoryVerb expected)
    {
        var gs = GameState.Instance;
        if (gs == null || gs.ActiveVerb != expected)
            return false;

        gs.RegisterVerbUsed(expected);
        Cancel(silent: true);
        return true;
    }

    /// <summary>Record a successful use without ending a multi-step verb mode.</summary>
    public static void RegisterUse(MemoryVerb verb)
    {
        if (Active == verb)
            GameState.Instance?.RegisterVerbUsed(verb);
    }
}

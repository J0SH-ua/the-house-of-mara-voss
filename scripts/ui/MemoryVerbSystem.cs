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
        MemoryVerb.Anchor => "G",
        MemoryVerb.Trace => "T",
        MemoryVerb.Release => "R",
        _ => string.Empty,
    };

    public static string Description(MemoryVerb verb) => verb switch
    {
        MemoryVerb.Echo => "Listen to memories in the order they were spoken.",
        MemoryVerb.Anchor => "Hold one unchanged thing still.",
        MemoryVerb.Trace => "Join causes in the order they happened.",
        MemoryVerb.Release => "Answer a memory that is whole.",
        _ => string.Empty,
    };

    private static string Instruction(MemoryVerb verb) => verb switch
    {
        MemoryVerb.Echo => "Echo is in Mara's hands.",
        MemoryVerb.Anchor => "Anchor is in Mara's hands.",
        MemoryVerb.Trace => "Trace is in Mara's hands.",
        MemoryVerb.Release => "Release is in Mara's hands.",
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
                MemoryVerb.Echo => "Echo has not woken yet. It will come with the preserved names in the kept room's jars.",
                MemoryVerb.Anchor => "Anchor has not woken yet. It will come when the hallway sentence can be heard whole.",
                MemoryVerb.Trace => "Trace has not woken yet. It will come after one true thing is held still and the apology is recovered.",
                MemoryVerb.Release => "Release has not woken yet. It comes only after the last afternoon has been traced.",
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

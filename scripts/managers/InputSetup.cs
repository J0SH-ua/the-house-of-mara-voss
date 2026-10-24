using Godot;

/// <summary>
/// Central registry of named input actions.
/// Actions are also declared in project.godot ([input]); this class is the
/// belt-and-braces fallback that creates any missing action at runtime so a
/// hand-merged or older project.godot can never leave the game uncontrollable.
/// Use the constants instead of string literals everywhere.
/// </summary>
public static class InputSetup
{
    public const string PauseGame = "pause_game";
    public const string AdvanceDialogue = "advance_dialogue";
    public const string SkipDialogue = "skip_dialogue";
    public const string ToggleClarity = "toggle_clarity";
    public const string RevealHotspots = "reveal_hotspots";
    public const string OpenJournal = "open_journal";
    public const string RequestHint = "request_hint";

    private static bool _ensured;

    public static void Ensure()
    {
        if (_ensured)
            return;
        _ensured = true;

        AddKeyAction(PauseGame, Key.Escape, Key.P);
        AddKeyAction(AdvanceDialogue, Key.Space, Key.Enter, Key.KpEnter);
        AddKeyAction(SkipDialogue, Key.End);
        AddKeyAction(ToggleClarity, Key.C);
        AddKeyAction(RevealHotspots, Key.Tab);
        AddKeyAction(OpenJournal, Key.J, Key.M);
        AddKeyAction(RequestHint, Key.H);
    }

    private static void AddKeyAction(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);

        foreach (var key in keys)
        {
            bool exists = false;
            foreach (var existing in InputMap.ActionGetEvents(action))
            {
                if (existing is InputEventKey k && k.PhysicalKeycode == key)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }
}

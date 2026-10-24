using Godot;
using Godot.Collections;

/// <summary>
/// Raw run-state field store (autoload). Existing scenes read/write these
/// fields directly; the *logic* over them (corruption tiers, clarity economy,
/// shard rules, saves) lives in GameState — prefer calling GameState from new
/// code so HUD/audio/persistence stay in sync.
/// </summary>
public partial class Globals : Node
{
	public static Globals Instance { get; private set; }

	/// <summary>Carried shards, not yet placed in Room 7.</summary>
	public static Array<bool> SHARDS_COLLECTED = [false, false, false, false, false, false];

	public const int MaxClarity = 5;
	public const int StartingClarity = 5;

	[ExportCategory("Gameplay")]
	[Export] public int CORRUPTION_COUNT = 0;
	[Export] public int CLARITY_TOGGLE_COUNT = StartingClarity;
	[Export] public bool IS_CLARIFYING = false;
	[Export] public Array<bool> SHARDS_PLACED = [false, false, false, false, false, false];
	[Export] public bool PHOTO1_COLLECTED = false;

	public override void _Ready()
	{
		Instance = this;
		InputSetup.Ensure();

		Logger.Info("Initializing Globals...");
	}

	/// <summary>Restore every run-state field to its new-game value.</summary>
	public void ResetRunState()
	{
		CORRUPTION_COUNT = 0;
		CLARITY_TOGGLE_COUNT = StartingClarity;
		IS_CLARIFYING = false;
		PHOTO1_COLLECTED = false;

		for (int i = 0; i < SHARDS_COLLECTED.Count; i++)
			SHARDS_COLLECTED[i] = false;
		for (int i = 0; i < SHARDS_PLACED.Count; i++)
			SHARDS_PLACED[i] = false;
	}
}

using System;
using System.Threading.Tasks;
using Godot;

public partial class SceneManager : Node
{
	public static SceneManager Instance { get; private set; }
	public static bool IsChanging { get; private set; }
	private static string _pendingScenePath;

	/// <summary>A scene change held back while GameUI plays a just-earned
	/// fragment's moment (hand, showcase, bag). GameUI resumes it via
	/// <see cref="ResumeDeferredSceneChange"/> when the bag closes over it.</summary>
	private static string _deferredScenePath;

	/// <summary>One-shot pass set when Mara has just been asked "leave
	/// anyway?" about a practical thing left behind, and answered yes.</summary>
	private static bool _leavePromptBypass;

	/// <summary>Fired after a scene has been swapped in (before fade-in completes).
	/// GameState uses it for music, objectives and autosave.</summary>
	public static event System.Action<string> SceneChanged;

	/// <summary>Path of the currently loaded sub-scene ("" before the first load).</summary>
	public static string CurrentScenePath { get; private set; } = string.Empty;

	/// <summary>
	/// Reverse navigation by scene file name: puzzle close-ups step back to
	/// their room ("Step Back"), each room steps back to the previous room.
	/// Forward travel stays gated by the in-world arrows/drawers as authored;
	/// this only adds the way back.
	/// </summary>
	private static readonly System.Collections.Generic.Dictionary<string, (string Target, string Label)> BackTargets = new()
	{
		// The kitchen once had no way back — the house had moved the
		// hallway. It relents for one small reason: a hairpin sleeping
		// in a nightstand drawer is allowed to be fetched.
		["room_1.tscn"] = ("res://scenes/world/bedroom_prologue.tscn", "← Previous Room"),
		["room_1_puzzle.tscn"] = ("res://scenes/world/room_1.tscn", "← Step Back"),
		["room_2.tscn"] = ("res://scenes/world/room_1.tscn", "← Previous Room"),
		["kitchen_cabinet.tscn"] = ("res://scenes/world/room_1.tscn", "← Step Back"),
		["room_2_puzzle.tscn"] = ("res://scenes/world/room_2.tscn", "← Step Back"),
		["difference_investigation.tscn"] = ("res://scenes/world/room_2_puzzle.tscn", "← Step Back"),
		["room_3.tscn"] = ("res://scenes/world/room_2.tscn", "← Previous Room"),
		["jar_closeup.tscn"] = ("res://scenes/world/room_3.tscn", "← Step Back"),
		["room_4.tscn"] = ("res://scenes/world/room_3.tscn", "← Previous Room"),
		["Room_4_puzzle.tscn"] = ("res://scenes/world/room_4.tscn", "← Step Back"),
		["flashback_landing.tscn"] = ("res://scenes/world/room_4.tscn", "← Step Back"),
		["room_5.tscn"] = ("res://scenes/world/room_4.tscn", "← Previous Room"),
		["apartment_now.tscn"] = ("res://scenes/world/room_5.tscn", "← Step Back"),
		["vent_crawl.tscn"] = ("res://scenes/world/room_5.tscn", "← Back Out"),
		["basement.tscn"] = ("res://scenes/world/room_5.tscn", "← Climb Back"),
		["breaker_box.tscn"] = ("res://scenes/world/basement.tscn", "← Step Back"),
		["room_6.tscn"] = ("res://scenes/world/room_5.tscn", "← Previous Room"),
		["bridge_repair.tscn"] = ("res://scenes/world/room_6.tscn", "← Back to the Garden"),
		["porch_447.tscn"] = ("res://scenes/world/room_6.tscn", "← Previous Room"),
		["room_7.tscn"] = ("res://scenes/world/porch_447.tscn", "← Previous Room"),
	};

	/// <summary>Where "step back" leads from <paramref name="scenePath"/>, if anywhere.</summary>
	public static bool TryGetBackTarget(string scenePath, out string targetPath, out string label)
	{
		targetPath = string.Empty;
		label = string.Empty;
		if (string.IsNullOrEmpty(scenePath))
			return false;

		int slash = scenePath.LastIndexOf('/');
		string fileName = slash >= 0 ? scenePath.Substring(slash + 1) : scenePath;
		if (!BackTargets.TryGetValue(fileName, out var entry))
			return false;

		targetPath = entry.Target;
		label = entry.Label;
		return true;
	}


	[ExportCategory("Scene Manager Variables")]
	[Export] public NodePath SceneRootPath = "../Control/SubViewportContainer/SubViewport";
	[Export] public NodePath FadeRectPath = "../Control/FadeRect";
	[Export(PropertyHint.Range, "0.05,2.0,0.05")] public float FadeDuration = 0.25f;

	private Node _sceneRoot;
	private ColorRect _fadeRect;

	public Node CurrentScene { get; private set; }

	public override void _Ready()
	{
		Instance = this;
		IsChanging = false;
		_sceneRoot = GetNodeOrNull(SceneRootPath);
		_fadeRect = GetNodeOrNull<ColorRect>(FadeRectPath);

		if (_sceneRoot == null)
			Logger.Error($"SceneManager could not resolve SceneRootPath: {SceneRootPath}");


		if (_fadeRect == null)
			Logger.Error($"SceneManager could not resolve FadeRectPath: {FadeRectPath}");
		else
		{
			var color = _fadeRect.Color;
			color.A = 1.0f;
			_fadeRect.Color = color;
			_fadeRect.Visible = true;
			_fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore;
		}

		Logger.Info("Initializing Scene Manager...");

		if (!string.IsNullOrWhiteSpace(_pendingScenePath))
		{
			string pendingScenePath = _pendingScenePath;
			_pendingScenePath = null;
			ChangeScene(pendingScenePath);
		}
	}

	/// <summary>The first departure from a finished room is a walk, not
	/// a cut: the first time Mara travels a leg that has a journey
	/// scene, the journey carries her (and remembers itself, so it is
	/// never compulsory twice). Centralised here so that *how* she
	/// leaves — Leave button, an in-world arrow, a door — no longer
	/// decides whether the walk happens. Never during the blackout,
	/// when the dark house is crossed one room at a time instead.</summary>
	private static string ApplyFirstDepartureJourney(string scenePath)
	{
		var gs = GameState.Instance;
		if (gs == null)
			return scenePath;
		if (gs.HasFlag("blackout_active") && !gs.HasFlag("backup_power_on"))
			return scenePath;

		string from = FileNameOf(CurrentScenePath);
		string to = FileNameOf(scenePath);
		string journey = null;
		string seen = null;
		if (to == "room_2.tscn" && (from == "room_1.tscn" || from == "room_1_puzzle.tscn"))
		{
			journey = "journey_to_room_2.tscn";
			seen = "journey_seen_Room1ToRoom2";
		}
		else if (to == "room_3.tscn" && (from == "room_2.tscn" || from == "room_2_puzzle.tscn"))
		{
			journey = "journey_to_room_3.tscn";
			seen = "journey_seen_Room2ToRoom3";
		}
		else if (to == "room_4.tscn" && (from == "room_3.tscn" || from == "jar_closeup.tscn"))
		{
			journey = "journey_to_room_4.tscn";
			seen = "journey_seen_Room3ToRoom4";
		}
		else if (to == "porch_447.tscn" && from == "room_6.tscn")
		{
			journey = "journey_to_porch.tscn";
			seen = "journey_seen_Room6ToPorch";
		}

		if (journey != null && !gs.HasFlag(seen))
			return "res://scenes/world/" + journey;
		return scenePath;
	}

	private static string FileNameOf(string scenePath)
	{
		if (string.IsNullOrEmpty(scenePath))
			return string.Empty;
		int slash = scenePath.LastIndexOf('/');
		return slash >= 0 ? scenePath.Substring(slash + 1) : scenePath;
	}

	public static async void ChangeScene(string scenePath)
	{
		if (string.IsNullOrWhiteSpace(scenePath))
			return;

		if (Instance == null)
		{
			Logger.Debug($"SceneManager Instance is null, deferring scene path: {scenePath}");
			_pendingScenePath = scenePath;
			return;
		}

		scenePath = ApplyFirstDepartureJourney(scenePath);

		// A fragment just earned gets its moment before the house moves
		// on: hold this request; GameUI resumes it once the fragment
		// is in the bag. (No waiting loops — the request simply waits.)
		if (GameUI.Instance != null && GameUI.Instance.IsHeldMomentPending())
		{
			_deferredScenePath = scenePath;
			Logger.Debug($"Scene change held for a fragment moment: {scenePath}");
			return;
		}

		// A fragment alone is enough to leave a room by — but before the
		// house lets her walk out light, Mara gets one honest chance to
		// say "maybe I should take a look around more." GameUI asks;
		// her answer resumes or shelves this exact request.
		if (!_leavePromptBypass && GameState.Instance != null && GameUI.Instance != null &&
		    GameState.Instance.TryGetLeavePrompt(CurrentScenePath, scenePath, out string leaveRoom) &&
		    GameUI.Instance.BeginLeavePrompt(leaveRoom))
		{
			_deferredScenePath = scenePath;
			Logger.Debug($"Scene change held for a leave-anyway prompt: {scenePath}");
			return;
		}
		_leavePromptBypass = false;
		_deferredScenePath = null; // explicit travel supersedes a held request

		while (IsChanging)
			await Instance.ToSignal(Instance.GetTree(), SceneTree.SignalName.ProcessFrame);

		IsChanging = true;

		try
		{
			await Instance.LoadScene(scenePath);
			await Instance.FadeIn();
		}
		catch (Exception ex)
		{
			Logger.Error($"Scene change failed: {ex.Message}\nStackTrace: {ex.StackTrace}");
		}
		finally
		{
			IsChanging = false;
		}
	}

	/// <summary>Called by GameUI when Mara answers "leave anyway": the held
	/// change proceeds, and the prompt is not asked twice about it.</summary>
	public static void ConfirmDeferredLeave()
	{
		if (string.IsNullOrWhiteSpace(_deferredScenePath))
			return;
		string next = _deferredScenePath;
		_deferredScenePath = null;
		_leavePromptBypass = true;
		ChangeScene(next);
	}

	/// <summary>Called by GameUI when Mara decides to look around a little
	/// more instead: the held change is simply forgotten.</summary>
	public static void CancelDeferredLeave()
	{
		_deferredScenePath = null;
	}

	/// <summary>Called by GameUI the moment a held fragment lands in the
	/// bag: if a scene change was held for it, finally let the house move.</summary>
	public static void ResumeDeferredSceneChange()
	{
		if (string.IsNullOrWhiteSpace(_deferredScenePath))
			return;
		string next = _deferredScenePath;
		_deferredScenePath = null;
		ChangeScene(next);
	}

	public static void ChangeSceneFromTileMapLayer(TileMapLayer layer)
	{
		if (layer == null)
			return;

		string targetScene = layer.GetMeta("target_scene_path", "").AsString();
		if (string.IsNullOrWhiteSpace(targetScene))
		{
			Logger.Error("TileMapLayer does not define metadata key 'target_scene_path': ", layer.Name);
			return;
		}

		ChangeScene(targetScene);
	}

	private async Task LoadScene(string scenePath)
	{
		if (_sceneRoot == null)
			Logger.Error("_sceneRoot is null in LoadScene");

		if (CurrentScene != null)
		{
			await FadeOut();
			_sceneRoot.RemoveChild(CurrentScene);
			CurrentScene.QueueFree();
			CurrentScene = null;
		}

		var packedScene = GD.Load<PackedScene>(scenePath);
		if (packedScene == null)
			throw new Exception("Could not load scene at path: " + scenePath);


		CurrentScene = packedScene.Instantiate<Node>();
		_sceneRoot.AddChild(CurrentScene);
		CurrentScenePath = scenePath;
		SceneChanged?.Invoke(scenePath);
	}

	private async Task FadeOut()
	{
		if (_fadeRect == null)
			return;

		Tween tween = CreateTween();
		tween.TweenProperty(_fadeRect, "color:a", 1.0f, FadeDuration);
		await ToSignal(tween, Tween.SignalName.Finished);
	}

	private async Task FadeIn()
	{
		if (_fadeRect == null)
			return;

		Tween tween = CreateTween();
		tween.TweenProperty(_fadeRect, "color:a", 0.0f, FadeDuration);
		await ToSignal(tween, Tween.SignalName.Finished);
	}
}

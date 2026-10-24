using System;
using System.Threading.Tasks;
using Godot;

public partial class SceneManager : Node
{
	public static SceneManager Instance { get; private set; }
	public static bool IsChanging { get; private set; }
	private static string _pendingScenePath;

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
		["room_1_puzzle.tscn"] = ("res://scenes/world/room_1.tscn", "← Step Back"),
		["room_2.tscn"] = ("res://scenes/world/room_1.tscn", "← Previous Room"),
		["room_2_puzzle.tscn"] = ("res://scenes/world/room_2.tscn", "← Step Back"),
		["room_3.tscn"] = ("res://scenes/world/room_2.tscn", "← Previous Room"),
		["room_4.tscn"] = ("res://scenes/world/room_3.tscn", "← Previous Room"),
		["Room_4_puzzle.tscn"] = ("res://scenes/world/room_4.tscn", "← Step Back"),
		["room_5.tscn"] = ("res://scenes/world/room_4.tscn", "← Previous Room"),
		["room_6.tscn"] = ("res://scenes/world/room_5.tscn", "← Previous Room"),
		["room_7.tscn"] = ("res://scenes/world/room_6.tscn", "← Previous Room"),
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

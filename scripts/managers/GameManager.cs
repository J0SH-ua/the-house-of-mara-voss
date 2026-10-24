using Godot;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }
    [ExportCategory("Game Flow")]
    [Export(PropertyHint.File, "*.tscn")] public string FirstRoomScenePath = "res://scenes/world/room_1.tscn";

    public SubViewport GameViewport { get; private set; }

    public override async void _Ready()
    {
        Instance = this;
        GameViewport = GetNodeOrNull<SubViewport>("Control/SubViewportContainer/SubViewport");

        if (GameViewport == null)
        {
            Logger.Error("GameManager could not find SubViewport at Control/SubViewportContainer/SubViewport.");
            return;
        }

        // SubViewports default physics_object_picking to OFF, which silently
        // disables every CollisionObject2D._InputEvent inside the game view
        // (brass key, room arrows, Room 7 slots) while GUI and _Input clicks
        // keep working — the classic "fragment won't click" trap.
        GameViewport.PhysicsObjectPicking = true;

        Logger.Info("Initializing Game Manager...");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // Continue resumes the saved room; a fresh run starts at FirstRoomScenePath.
        string startScene = GameState.Instance?.ConsumeStartScene(FirstRoomScenePath) ?? FirstRoomScenePath;
        SceneManager.ChangeScene(startScene);
    }

    public static SubViewport GetGameViewport()
    {
        return Instance?.GameViewport;
    }
}
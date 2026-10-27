using Godot;

/// <summary>
/// The little door key as its own clickable thing on the sitting-room
/// table.
///
/// This is deliberately a separate last child of Room2, not a bare
/// sprite whose clicks are heard by the room root. Godot delivers
/// _Input in reverse depth-first order, so the room root hears a
/// click only after every examine layer above it. The table and its
/// neighbours were therefore claiming the click first and marking it
/// handled — the key could be seen but never taken. As the final,
/// deepest branch of the room, this pickup hears its own click
/// before those layers, claims it, and asks Room2 to play the taking.
///
/// ExamineHandler and TableExamine1 also yield this rectangle while
/// the key waits, so Room2's root-level fallback remains reachable
/// even if scene-tree order ever changes.
/// </summary>
public partial class LittleDoorKeyPickup : Node2D
{
    private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";

    private Sprite2D _sprite;
    private bool _hovering;

    public override void _Ready()
    {
        SetProcess(true);
        SetProcessInput(true);
    }

    public void Configure(Texture2D texture, Vector2 spriteScale)
    {
        if (_sprite == null)
        {
            _sprite = new Sprite2D
            {
                Name = "LittleDoorKeyIcon",
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            AddChild(_sprite);
        }

        _sprite.Texture = texture;
        _sprite.Scale = spriteScale;
    }

    public bool ContainsGlobalPoint(Vector2 point)
    {
        float half = Room2.DoorKeyClickHalfExtent;
        return Visible &&
            new Rect2(GlobalPosition - Vector2.One * half,
                Vector2.One * half * 2f).HasPoint(point);
    }

    public override void _Input(InputEvent @event)
    {
        // The key can be clicked or taken with F, the house's hand.
        bool clicked = @event is InputEventMouseButton mouseEvent && mouseEvent.Pressed &&
            mouseEvent.ButtonIndex == MouseButton.Left;
        bool useKey = @event is InputEventKey keyEvent && keyEvent.Pressed &&
            !keyEvent.Echo && keyEvent.PhysicalKeycode == Key.F;
        if (!clicked && !useKey)
            return;
        if (Dialogue.IsInputBlocked || Dialogue.IsShowing ||
            !ContainsGlobalPoint(GetGlobalMousePosition()))
            return;

        if (GetParent() is Room2 room && room.TryBeginDoorKeyPickup())
            GetTree().Root.SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_sprite != null)
        {
            float pulse = Settings.ReduceMotionOrDefault()
                ? 1f
                : 0.86f + 0.14f * (float)System.Math.Sin(Time.GetTicksMsec() / 240.0);
            _sprite.Modulate = new Color(pulse, pulse, pulse);
        }

        bool hovering = !Dialogue.IsInputBlocked && !Dialogue.IsShowing &&
            ContainsGlobalPoint(GetGlobalMousePosition());
        if (hovering && !_hovering)
        {
            CursorHelper.BeginHover(this, PointerCursorPath, 32, 32);
            _hovering = true;
            AudioManager.Play(Sfx.UiHover, -12f);
        }
        else if (!hovering && _hovering)
        {
            CursorHelper.EndHover(this);
            _hovering = false;
        }
    }

    public void MarkTaken()
    {
        if (_hovering)
        {
            CursorHelper.EndHover(this);
            _hovering = false;
        }
        Visible = false;
        SetProcess(false);
        SetProcessInput(false);
    }

    public override void _ExitTree()
    {
        if (_hovering)
        {
            CursorHelper.EndHover(this);
            _hovering = false;
        }
    }
}

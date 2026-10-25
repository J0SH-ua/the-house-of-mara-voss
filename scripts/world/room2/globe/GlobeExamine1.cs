using Godot;
using System;

/// <summary>
/// The sitting room's globe — an ordinary examine, and, after the
/// crumbling room has been remembered right, the place the evening
/// sets its secret down.
///
/// When the investigation is solved and the little door key has not
/// yet been taken, the key simply lies here, beside the globe where
/// the letter used to live its ordinary life, drawn in code over the
/// painting (the tiles never knew about it). One click on the key
/// itself takes it — its own held moment, its own place in the bag.
/// Everything else about the globe behaves exactly as before, and a
/// sealed room still lets Mara back for an untaken key.
/// </summary>
public partial class GlobeExamine1 : ExamineHandler
{
    private const string SolvedFlag = "difference_room2_solved";
    private const string DoorKeyFlag = "item_hall_key_door";
    private const string DoorKeyIconPath = "res://assets/images/core/items/door_key.png";

    private Sprite2D _keySprite;
    private bool _takingKey;

    private bool KeyWaiting =>
        (GameState.Instance?.HasFlag(SolvedFlag) ?? false) &&
        !(GameState.Instance?.HasFlag(DoorKeyFlag) ?? false);

    public override void _Ready()
    {
        base._Ready();
        if (KeyWaiting)
            LayKeyByGlobe();
    }

    private void LayKeyByGlobe()
    {
        var texture = GD.Load<Texture2D>(DoorKeyIconPath);
        if (texture == null)
            return;

        Vector2 anchor = MapToLocal(GetUsedRect().GetCenter());
        _keySprite = new Sprite2D
        {
            Name = "LittleDoorKeyProp",
            Texture = texture,
            Position = anchor,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ZIndex = 6,
        };
        // The layers above this one are scaled up; aim for a key
        // about 84 px tall on screen whatever the nesting says.
        Vector2 parentScale = GlobalTransform.Scale;
        float s = 84f / 512f / Mathf.Max(0.01f, Mathf.Abs(parentScale.X));
        _keySprite.Scale = Vector2.One * s;
        AddChild(_keySprite);
        // Down off the globe itself, beside its stand.
        _keySprite.GlobalPosition += new Vector2(-28, 48);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_keySprite == null)
            return;
        if (!KeyWaiting)
        {
            _keySprite.QueueFree();
            _keySprite = null;
            return;
        }
        float pulse = Settings.ReduceMotionOrDefault()
            ? 1f
            : 0.86f + 0.14f * (float)System.Math.Sin(Time.GetTicksMsec() / 240.0);
        _keySprite.Modulate = new Color(pulse, pulse, pulse);
    }

    private bool ClickHitsKey(Vector2 globalMouse)
    {
        if (_keySprite == null || !KeyWaiting)
            return false;
        return new Rect2(_keySprite.GlobalPosition - new Vector2(48, 48),
            new Vector2(96, 96)).HasPoint(globalMouse);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed &&
            mouseEvent.ButtonIndex == MouseButton.Left &&
            !Dialogue.IsInputBlocked && !Dialogue.IsShowing && !_takingKey &&
            ClickHitsKey(GetGlobalMousePosition()))
        {
            GetTree().Root.SetInputAsHandled();
            _ = TakeDoorKeyAsync();
            return;
        }

        base._Input(@event);
    }

    private async System.Threading.Tasks.Task TakeDoorKeyAsync()
    {
        var gs = GameState.Instance;
        if (_takingKey || gs == null || gs.HasFlag(DoorKeyFlag))
            return;
        _takingKey = true;
        try
        {
            Vector2 anchor = _keySprite?.GlobalPosition ?? GetGlobalPosition();
            AudioManager.Play(Sfx.KeyType, -8f, 0.95f);
            await Dialogue.ShowText(this,
                "A key. Not a metaphor wearing key-coloured paint. Iron — and its bow is a little door, down to a knob too small for any hand but memory's.",
                5.5f, anchor.X, anchor.Y, fontSize: 24);
            if (!GodotObject.IsInstanceValid(this))
                return;
            await Dialogue.ShowText(this,
                "He taped it behind an evening and trusted I'd remember the room right. …I did, Dad. Just barely, with the ceiling coming down. But I did.",
                5.5f, anchor.X, anchor.Y, fontSize: 24);
            if (!GodotObject.IsInstanceValid(this))
                return;

            gs.SetFlag(DoorKeyFlag, save: false);
            gs.UnlockClue("hall_key_door");
            gs.Toast("Little door key — kept in Mara's bag (B). Somewhere ahead is a door with a little door stamped above its middle keyhole.");
            gs.SaveGame("little door key taken");
            if (_keySprite != null)
            {
                _keySprite.QueueFree();
                _keySprite = null;
            }
        }
        finally
        {
            _takingKey = false;
        }
    }

    protected override void OnExamineClicked()
    {
    }
}

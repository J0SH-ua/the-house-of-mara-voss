using Godot;

public partial class Room1 : ExamineHandler
{
    public override void _Ready()
    {
        if (Globals.Instance == null)
            return;

        bool shardTaken = Globals.SHARDS_COLLECTED[0];

        // The cabinet stays as you left it: once unlocked (or emptied) it
        // reads open from the room's point of view, before you click again.
        if (shardTaken || Room1Puzzle.IsUnlocked)
        {
            var drawerExamine1 = GetNodeOrNull<TileMapLayer>("Objects/DrawerFinal/Examine1");
            var drawerExamine2 = GetNodeOrNull<TileMapLayer>("Objects/DrawerFinal/Examine2");

            // Hide — never free — the closed layer: it carries the drawer
            // gate script, so the cabinet stays clickable for stepping back
            // into the close-up (to take, or re-take, the way through).
            if (drawerExamine1 != null)
            {
                var c1 = drawerExamine1.Modulate;
                c1.A = 0f;
                drawerExamine1.Modulate = c1;
                var s1 = drawerExamine1.SelfModulate;
                s1.A = 0f;
                drawerExamine1.SelfModulate = s1;
            }

            if (drawerExamine2 != null)
            {
                var c2 = drawerExamine2.Modulate;
                c2.A = 1.0f;
                drawerExamine2.Modulate = c2;
                var s2 = drawerExamine2.SelfModulate;
                s2.A = 1.0f;
                drawerExamine2.SelfModulate = s2;
            }
        }

        if (shardTaken)
        {
            var brassKey = GetNodeOrNull<Sprite2D>("UI/Inventory/BrassKey");
            var rightArrow = GetNodeOrNull<Area2D>("UI/RightArrow");

            if (brassKey != null)
            {
                var keyColor = brassKey.Modulate;
                keyColor.A = 1.0f;
                brassKey.Modulate = keyColor;
            }

            if (rightArrow != null)
            {
                var rightArrowColor = rightArrow.Modulate;
                rightArrowColor.A = 1.0f;
                rightArrow.Modulate = rightArrowColor;
            }
        }
    }
}

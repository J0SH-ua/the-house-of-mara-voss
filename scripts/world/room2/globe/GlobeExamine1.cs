using Godot;
using System;

/// <summary>
/// The sitting room's globe — an ordinary examine. (The little door
/// key that appears on the table by it after the Difference
/// investigation used to live here, as a child sprite of this layer;
/// it never survived contact with the examine machinery — cycling
/// or restoring the globe's layers hides this one, children and all,
/// and frees it outright. The key now belongs to the room root;
/// see Room2.LayDoorKeyOnTable.)
/// </summary>
public partial class GlobeExamine1 : ExamineHandler
{
    protected override void OnExamineClicked()
    {
    }
}

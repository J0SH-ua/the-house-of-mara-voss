using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

/// <summary>
/// Records which objects have been examined in each room and answers
/// "is this room done?" for gates (drawers, jars), the objective HUD,
/// hints and hotspot reveals. Progress is serialisable for GameState saves.
/// </summary>
public partial class RoomExamineTracker : Node
{
    /// <summary>(roomName, objectName) whenever an examine is recorded.</summary>
    public static event Action<string, string> ExamineRecorded;

    /// <summary>Objects that must be examined before a room's gate opens.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RequiredObjectsByRoom =
        new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["Room1"] = ["Window", "Cup2", "Toast", "Note", "Clock", "Calendar", "Drawing"],
            ["Room2"] = ["Bookshelf", "Phone", "RightChair", "LeftChair", "Painting", "Globe", "Letter"],
            ["Room3"] = ["Painting", "Bag", "Bed", "Note"],
        };

    // Track which examine nodes have been clicked: roomName -> objectName -> set of examine layer names
    private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, HashSet<string>>> _examineProgress = new();

    /// <summary>Bumped on every change so hover/UI code can cache cheaply.</summary>
    public static int Version { get; private set; }

    private static RoomExamineTracker _instance;

    public override void _Ready()
    {
        _instance = this;
    }

    private static RoomExamineTracker GetInstance()
    {
        return _instance;
    }

    public static void OnExamineClicked(TileMapLayer examineNode)
    {
        if (examineNode == null)
            return;

        var parent = examineNode.GetParent();
        if (parent == null)
            return;

        string roomName = ResolveRoomName(examineNode);
        string objectName = parent.Name.ToString();
        string examineName = examineNode.Name.ToString();

        if (objectName.EndsWith("Final"))
            return;

        if (!_examineProgress.ContainsKey(roomName))
            _examineProgress[roomName] = [];

        if (!_examineProgress[roomName].ContainsKey(objectName))
            _examineProgress[roomName][objectName] = [];

        bool isNew = _examineProgress[roomName][objectName].Add(examineName);
        Version++;
        if (isNew)
            ExamineRecorded?.Invoke(roomName, objectName);
    }

    /// <summary>Walk up past Objects to find the room root's name.</summary>
    public static string ResolveRoomName(Node node)
    {
        var current = node;
        bool passedObjects = false;

        while (current != null)
        {
            current = current.GetParent();
            if (current == null)
                break;

            string currentName = current.Name.ToString();

            // Track when we pass the Objects node
            if (currentName == "Objects")
            {
                passedObjects = true;
                continue;
            }

            // Once we've passed Objects, the room root can be a Node2D, CanvasLayer, or TileMapLayer
            if (passedObjects && (current is Node2D or CanvasLayer or TileMapLayer))
                return currentName;
        }

        return "Room1";
    }

    public static bool HasObjectBeenFullyExamined(string roomName, string objectName)
    {
        if (objectName.EndsWith("Final") || objectName.EndsWith("Exclude"))
            return true;

        if (_examineProgress.ContainsKey(roomName) && _examineProgress[roomName].ContainsKey(objectName))
        {
            int clickedExamines = _examineProgress[roomName][objectName].Count;
            return clickedExamines >= 1;
        }

        if (_examineProgress.ContainsKey(objectName) && _examineProgress[objectName].Count > 0)
            return true;

        return false;
    }

    public static bool HasRoomBeenFullyExamined(string roomName, string objectName = null)
    {
        if (!string.IsNullOrEmpty(objectName))
            return HasObjectBeenFullyExamined(roomName, objectName);

        GetRoomProgress(roomName, out _, out _, out var remaining);
        return remaining.Count == 0;
    }

    /// <summary>Progress snapshot for HUD objectives and hint text.</summary>
    public static void GetRoomProgress(string roomName, out int done, out int total, out List<string> remaining)
    {
        remaining = new List<string>();
        done = 0;

        if (roomName == null || !RequiredObjectsByRoom.TryGetValue(roomName, out var required))
        {
            total = 0;
            return;
        }

        total = required.Length;
        foreach (var requiredObject in required)
        {
            if (HasObjectBeenFullyExamined(roomName, requiredObject))
                done++;
            else
                remaining.Add(requiredObject);
        }
    }

    public static bool RoomHasRequirements(string roomName)
    {
        return roomName != null && RequiredObjectsByRoom.ContainsKey(roomName);
    }

    public static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, HashSet<string>>> GetExamineProgress()
    {
        return _examineProgress;
    }

    /// <summary>Layer names clicked for one object (a copy), or null if never
    /// examined. Used to restore an object's examine layers when its room is
    /// re-entered (step-back navigation, Continue).</summary>
    public static HashSet<string> GetClickedLayers(string roomName, string objectName)
    {
        if (roomName != null && objectName != null &&
            _examineProgress.TryGetValue(roomName, out var objects) &&
            objects.TryGetValue(objectName, out var layers) && layers.Count > 0)
            return new HashSet<string>(layers);
        return null;
    }

    public static void ResetProgress(string roomName = null)
    {
        if (string.IsNullOrEmpty(roomName))
        {
            _examineProgress.Clear();
        }
        else if (_examineProgress.ContainsKey(roomName))
        {
            _examineProgress[roomName].Clear();
        }

        Version++;
    }

    // ------------------------------------------------------------ save support

    public static Dictionary ExportProgress()
    {
        var root = new Dictionary();
        foreach (var (room, objects) in _examineProgress)
        {
            var roomDict = new Dictionary();
            foreach (var (obj, layers) in objects)
            {
                var arr = new Array<string>();
                foreach (var layer in layers)
                    arr.Add(layer);
                roomDict[obj] = arr;
            }
            root[room] = roomDict;
        }
        return root;
    }

    public static void ImportProgress(Dictionary data)
    {
        _examineProgress.Clear();
        if (data == null)
        {
            Version++;
            return;
        }

        foreach (var roomKey in data.Keys)
        {
            string room = roomKey.AsString();
            if (data[roomKey].VariantType != Variant.Type.Dictionary ||
                data[roomKey].AsGodotDictionary() is not Dictionary roomDict)
                continue;

            var objects = new System.Collections.Generic.Dictionary<string, HashSet<string>>();
            foreach (var objKey in roomDict.Keys)
            {
                var layers = new HashSet<string>();
                foreach (var v in roomDict[objKey].AsGodotArray())
                    layers.Add(v.AsString());
                objects[objKey.AsString()] = layers;
            }
            _examineProgress[room] = objects;
        }

        Version++;
    }
}

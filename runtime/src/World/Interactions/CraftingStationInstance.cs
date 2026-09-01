using Godot;
using OpenNV.Runtime.Gameplay.Crafting;

namespace OpenNV.Runtime.World.Interactions;

internal partial class CraftingStationInstance : Node3D
{
    internal string ReferenceFormId { get; private set; } = "";
    internal string EditorId { get; private set; } = "";
    internal CraftingStationContract Contract { get; private set; } = null!;

    internal void Configure(
        string referenceFormId,
        string editorId,
        CraftingStationContract contract)
    {
        ReferenceFormId = referenceFormId;
        EditorId = editorId;
        Contract = contract;
        Name = $"CRAFTING_STATION_{referenceFormId}_{editorId}";
    }
}

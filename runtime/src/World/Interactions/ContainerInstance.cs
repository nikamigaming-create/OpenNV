using Godot;
using OpenNV.Runtime.Gameplay.Items;

namespace OpenNV.Runtime.World.Interactions;

internal partial class ContainerInstance : Node3D
{
    internal string ReferenceFormId { get; private set; } = "";
    internal string EditorId { get; private set; } = "";
    internal string DisplayName { get; private set; } = "";
    internal IReadOnlyList<Entry> Items { get; private set; } = Array.Empty<Entry>();

    internal void Configure(
        string referenceFormId,
        string editorId,
        string displayName,
        IReadOnlyList<Entry> items)
    {
        ReferenceFormId = referenceFormId;
        EditorId = editorId;
        DisplayName = displayName;
        Items = items;
        Name = $"CONTAINER_{referenceFormId}_{editorId}";
    }

    internal readonly record struct Entry(ItemDefinition Item, int Count, bool Resolved)
    {
        internal string ItemFormId => Item.FormId;
        internal string EditorId => Item.EditorId;
        internal string DisplayName => Item.DisplayName ?? "";
        internal string RecordType => Item.RecordType;
    }
}

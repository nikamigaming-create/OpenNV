using Godot;

namespace OpenNV.Runtime;

internal partial class PickupInstance : Node3D
{
    internal string ReferenceFormId { get; private set; } = "";
    internal string ItemFormId { get; private set; } = "";
    internal string EditorId { get; private set; } = "";
    internal string RecordType { get; private set; } = "";
    internal int Count { get; private set; }
    internal WeaponProfile? Weapon { get; private set; }

    internal void Configure(
        string referenceFormId,
        string itemFormId,
        string editorId,
        string recordType,
        int count,
        WeaponProfile? weapon)
    {
        ReferenceFormId = referenceFormId;
        ItemFormId = itemFormId;
        EditorId = editorId;
        RecordType = recordType;
        Count = count;
        Weapon = weapon;
        Name = $"PICKUP_{referenceFormId}_{editorId}";
    }

    internal readonly record struct WeaponProfile(int Damage, int ClipSize, string? AmmoFormId);
}

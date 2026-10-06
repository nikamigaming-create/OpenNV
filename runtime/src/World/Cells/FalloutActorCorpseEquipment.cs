using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCorpseWeaponSource(FalloutFormKey Weapon, string Sha256)
{
    internal void Validate()
    {
        if (Weapon.ObjectId is 0 or > FalloutFormKey.ObjectIdMask || string.IsNullOrWhiteSpace(Weapon.OwnerPlugin) ||
            !FalloutActorFurnitureContinuation.ValidHash(Sha256))
            throw new InvalidDataException("Corpse weapon has no source identity.");
    }

    internal FalloutWeaponPresentation ValidateSource(FalloutPluginStack records, FalloutPlayerInventory inventory)
    {
        Validate();
        var record = records.GetEffective(Weapon);
        if (record.Signature != "WEAP" || !FalloutActorFurnitureContinuation.RecordHash(record).Equals(Sha256, StringComparison.OrdinalIgnoreCase) ||
            inventory.Item(Weapon) is not { Count: > 0, RecordType: "WEAP" } item || item.RuntimeFormId != records.RuntimeFormId(Weapon))
            throw new InvalidDataException("Corpse weapon differs from its winning declaration or retained inventory item.");
        return FalloutWeaponPresentation.Read(records, Weapon, false);
    }
}

internal sealed record FalloutCorpseWeaponNode(int Index, int Parent, string? BoneParent, int? SourceBlock,
    string SourceName, string NativeType, IReadOnlyList<float> Transform, bool Visible, uint? RenderLayers = null)
{
    internal void Validate()
    {
        if (Index < 0 || Parent < -1 || Parent >= Index || SourceBlock is < 0 || SourceName is null ||
            string.IsNullOrWhiteSpace(NativeType) || (Parent == -1) != (BoneParent is not null) ||
            BoneParent is not null && string.IsNullOrWhiteSpace(BoneParent))
            throw new InvalidDataException("Corpse weapon node has an invalid source parent or raw pose.");
        ValidateTransform(Transform, requireInverse: false);
    }

    internal static void ValidateTransform(IReadOnlyList<float> t, bool requireInverse)
    {
        if (t is not { Count: 12 } || t.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Corpse equipment has an invalid raw transform.");
        // Local equipment channels may already have published zero scale.
        // Only the actor/physical root is used as an inverse frame.
        if (!requireInverse) return;
        var determinant = (double)t[0] * (t[4] * (double)t[8] - t[5] * (double)t[7]) -
            t[3] * (t[1] * (double)t[8] - t[2] * (double)t[7]) + t[6] * (t[1] * (double)t[5] - t[2] * (double)t[4]);
        if (!double.IsFinite(determinant) || determinant == 0)
            throw new InvalidDataException("Corpse actor root has a singular raw pose.");
    }

    internal FalloutCorpseWeaponNode Copy() => this with { Transform = Transform.ToArray() };
}

internal sealed record FalloutCorpseWeaponBodyAttachment(int Index, string Bone, bool Visible);

internal sealed record FalloutCorpseWeaponAttachment(string Owner, FalloutCorpseWeaponSource Source,
    string ModelResource, string ModelSha256, bool PrimaryVisible, IReadOnlyList<FalloutCorpseWeaponNode> Nodes,
    IReadOnlyList<FalloutCorpseWeaponBodyAttachment> BodyAttachments)
{
    internal void Validate()
    {
        if (Source is null) throw new InvalidDataException("Corpse attachment has no source weapon.");
        Source.Validate();
        if (Owner is not ("combat" or "package") || string.IsNullOrWhiteSpace(ModelResource) ||
            !FalloutActorFurnitureContinuation.ValidHash(ModelSha256) || Nodes is not { Count: > 0 } || BodyAttachments is null)
            throw new InvalidDataException("Corpse attachment has no complete source model binding.");
        for (var index = 0; index < Nodes.Count; index++)
        {
            var node = Nodes[index] ?? throw new InvalidDataException("Corpse attachment has an absent node.");
            node.Validate();
            if (node.Index != index || index == 0 && (node.Parent != -1 || node.BoneParent != "Weapon"))
                throw new InvalidDataException("Corpse attachment node layout is incomplete or unordered.");
        }
        for (var index = 0; index < BodyAttachments.Count; index++)
            if (BodyAttachments[index] is not { } body || body.Index != index || string.IsNullOrWhiteSpace(body.Bone))
                throw new InvalidDataException("Corpse anatomical attachments are incomplete or unordered.");
    }

    internal void ValidateBinding(FalloutCorpseWeaponAttachment actual)
    {
        Validate(); actual.Validate();
        if (Owner != actual.Owner || Source.Weapon != actual.Source.Weapon ||
            !Source.Sha256.Equals(actual.Source.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !ModelResource.Equals(actual.ModelResource, StringComparison.OrdinalIgnoreCase) ||
            !ModelSha256.Equals(actual.ModelSha256, StringComparison.OrdinalIgnoreCase) ||
            Nodes.Count != actual.Nodes.Count || BodyAttachments.Count != actual.BodyAttachments.Count)
            throw new InvalidDataException("Corpse attachment differs from its owned model or native layout.");
        for (var index = 0; index < Nodes.Count; index++)
        {
            var saved = Nodes[index]; var bound = actual.Nodes[index];
            if (saved.Parent != bound.Parent || saved.BoneParent != bound.BoneParent || saved.SourceBlock != bound.SourceBlock ||
                saved.SourceName != bound.SourceName || saved.NativeType != bound.NativeType ||
                saved.RenderLayers.HasValue != bound.RenderLayers.HasValue)
                throw new InvalidDataException("Corpse attachment has an unbound source node or anatomical parent.");
        }
        for (var index = 0; index < BodyAttachments.Count; index++)
            if (BodyAttachments[index].Bone != actual.BodyAttachments[index].Bone)
                throw new InvalidDataException("Corpse attachment has an unbound source body part.");
    }

    internal FalloutCorpseWeaponAttachment Copy() => this with
    { Nodes = Nodes.Select(node => node.Copy()).ToArray(), BodyAttachments = BodyAttachments.ToArray() };
}

// This is consumed motion history, not a resumable route or a door completion.
internal sealed record FalloutCorpseRouteRetirement(bool SearchRetired, string? RouteError, string? CoarseRouteError,
    int Failures, FalloutFormKey? Door = null, bool DoorRequested = false, double DoorSeconds = 0, string? DoorError = null)
{
    internal void Validate()
    {
        if (Failures < 0 || !double.IsFinite(DoorSeconds) || DoorSeconds < 0 ||
            Door is null && (DoorRequested || DoorSeconds != 0 || DoorError is not null) ||
            Door is { } door && (string.IsNullOrWhiteSpace(door.OwnerPlugin) || door.ObjectId is 0 or > FalloutFormKey.ObjectIdMask))
            throw new InvalidDataException("Corpse motion retirement history is invalid.");
    }
}

internal sealed record FalloutActorCorpseEquipment(IReadOnlyList<float> RootPose, FalloutActorActivitySnapshot Activity,
    FalloutActorResidualPose ResidualPose,
    IReadOnlyList<FalloutCorpseWeaponAttachment> Attachments, FalloutCorpseWeaponSource? HandlingWeapon = null,
    FalloutWeaponHandlingSnapshot? WeaponHandling = null, string? EmbeddedMuzzleBone = null,
    FalloutCorpseRouteRetirement? RouteRetirement = null)
{
    internal void Validate()
    {
        FalloutCorpseWeaponNode.ValidateTransform(RootPose, requireInverse: true);
        if (Activity is null || ResidualPose is null || Attachments is null || Attachments.Any(attachment => attachment is null) ||
            Attachments.Select(attachment => attachment.Owner).Distinct(StringComparer.Ordinal).Count() != Attachments.Count ||
            (HandlingWeapon is null) != (WeaponHandling is null) ||
            EmbeddedMuzzleBone is not null && (string.IsNullOrWhiteSpace(EmbeddedMuzzleBone) || HandlingWeapon is null))
            throw new InvalidDataException("Corpse equipment has an absent or duplicated independent owner.");
        ResidualPose.Validate();
        Activity.Validate();
        foreach (var attachment in Attachments) attachment.Validate();
        HandlingWeapon?.Validate();
        if (WeaponHandling is { } handling)
        {
            FalloutWeaponHandling.Validate(handling);
            if (handling.ShotRandomState is null || handling.AttackRandomState is null)
                throw new InvalidDataException("Corpse weapon handling requires its actual random streams.");
        }
        if (Attachments.SingleOrDefault(attachment => attachment.Owner == "combat") is { } combat &&
            (combat.Source != HandlingWeapon || EmbeddedMuzzleBone is not null))
            throw new InvalidDataException("Corpse combat attachment differs from its actual handling owner.");
        RouteRetirement?.Validate();
    }

    internal static void ValidateSnapshot(FalloutReferenceSnapshot snapshot)
    {
        if (snapshot.CorpseEquipment is not { } equipment)
        {
            if (snapshot.Injury?.Dead == true && snapshot.Engagement?.WeaponHandling is not null)
                throw new NotSupportedException("Corpse weapon handling requires its independent equipment continuation.");
            return;
        }
        equipment.Validate();
        if (snapshot.Injury?.Dead != true || snapshot.Ragdoll is null || snapshot.Inventory is null ||
            snapshot.KnockedDown || snapshot.HitReaction is not null)
            throw new InvalidDataException("Corpse equipment requires actual death, inventory and an independent physical capture.");
        if (snapshot.Engagement?.WeaponHandling is { } history && !SameHandling(history, equipment.WeaponHandling))
            throw new InvalidDataException("Corpse handling differs from the retained combat history.");
        var inventory = snapshot.Inventory.Contents;
        if (inventory?.Inventory?.Items is null || inventory.EquippedRuntimeFormIds is null)
            throw new InvalidDataException("Corpse equipment has no captured authoritative items.");
        foreach (var source in equipment.Attachments.Select(attachment => attachment.Source)
            .Concat(equipment.HandlingWeapon is { } selectedSource ? [selectedSource] : []))
            if (!inventory.Inventory.Items.Any(item => item.FormKey == source.Weapon && item.RecordType == "WEAP" && item.Count > 0))
                throw new InvalidDataException("Corpse equipment has no captured source weapon item.");
        if (equipment.WeaponHandling is { } handling)
        {
            var selectedItem = inventory.Inventory.Items.Single(item => item.FormKey == equipment.HandlingWeapon!.Weapon);
            if (!inventory.EquippedRuntimeFormIds.Contains(selectedItem.RuntimeFormId))
                throw new InvalidDataException("Corpse handling has no captured equipped item.");
            foreach (var magazine in handling.Magazines)
                if (!inventory.Inventory.Items.Any(item => item.FormKey == magazine.Weapon && item.RecordType == "WEAP" && item.Count > 0) ||
                    magazine.UsesInventoryAmmo && magazine.Loaded > inventory.Inventory.Items
                        .Where(item => item.FormKey == magazine.Ammunition && item.RecordType == "AMMO").Sum(item => item.Count))
                    throw new InvalidDataException("Corpse magazine exceeds its captured actual weapon or carried ammunition.");
        }
    }

    internal void ValidateSource(FalloutPluginStack records, FalloutPlayerInventory inventory)
    {
        Validate();
        foreach (var attachment in Attachments)
        {
            var weapon = attachment.Source.ValidateSource(records, inventory);
            if (weapon.Model?.ModelPath is not { } path || !path.Equals(attachment.ModelResource, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Corpse attachment differs from its winning weapon model.");
        }
        if (HandlingWeapon is { } handlingSource)
        {
            var weapon = handlingSource.ValidateSource(records, inventory);
            if ((weapon.Model is null) != (EmbeddedMuzzleBone is not null) ||
                !inventory.Equipped.Contains(records.RuntimeFormId(weapon.Form)) ||
                weapon.Model is not null && !Attachments.Any(attachment => attachment.Owner == "combat"))
                throw new InvalidDataException("Corpse weapon handling differs from its actual embedded or equipped owner.");
            new FalloutWeaponHandling(inventory, nativeNpc: true).Restore(WeaponHandling!,
                key => FalloutWeaponPresentation.Read(records, key, false));
        }
        if (RouteRetirement?.Door is { } door)
        {
            var reference = records.GetEffective(door);
            if (reference.Signature != "REFR" ||
                records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME")).Signature != "DOOR")
                throw new InvalidDataException("Corpse route history has no source door.");
        }
    }

    internal static bool SameHandling(FalloutWeaponHandlingSnapshot? first, FalloutWeaponHandlingSnapshot? second) =>
        first is null ? second is null : second is not null && first.Drawn == second.Drawn &&
            first.ShotRandomState == second.ShotRandomState && first.AttackRandomState == second.AttackRandomState &&
            first.Magazines.SequenceEqual(second.Magazines);

    internal FalloutActorCorpseEquipment Copy() => this with
    {
        RootPose = RootPose.ToArray(),
        ResidualPose = ResidualPose.Copy(),
        Attachments = Attachments.Select(attachment => attachment.Copy()).ToArray(),
        WeaponHandling = WeaponHandling is { } handling ? handling with { Magazines = handling.Magazines.ToArray() } : null
    };
}

using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicHumanBinding(string[] ArtBases, bool Female, string Outfit, uint FaceDonor, string Remaining, string? HairColor = null,
    float ShoulderWidth = 1, float WaistWidth = 1, ClassicPropPalette[]? Palette = null);
internal sealed record ClassicPremadeFace(string Campaign, string Id, uint FaceDonor);
internal sealed record ClassicHeldItem(int Pid, int WeaponAnimation, string Model, string Pose, string Socket, string[]? InactiveObjects = null, string? Grip = null);
internal sealed record ClassicHumanRecipe(string Schema, ClassicHumanBinding[] Bindings, ClassicPremadeFace[] Premades, ClassicHeldItem[]? HeldItems = null);

/// <summary>Appearance donors only. No donor NPC inventory, stats, AI or reference enters the classic world.</summary>
internal sealed class ClassicOwnedHumanoids(RuntimeLiveContentSource source) : IDisposable
{
    private readonly FalloutPluginStack _records = FalloutPluginStack.Load(source.PluginSources);
    private readonly ClassicHumanRecipe _recipe = JsonSerializer.Deserialize<ClassicHumanRecipe>(
        Godot.FileAccess.GetFileAsString("res://config/classic-humanoids-v1.json"))
        ?? throw new InvalidDataException("Classic humanoid recipes are absent.");

    internal ClassicPlayerBody? Create(string artPath, ClassicCharacterDraft? choice = null, Fallout1NativeMapObject? placed = null, ClassicInventoryEntry? equipped = null)
    {
        if (_recipe.Schema != "opennv-classic-humanoids/v1") throw new InvalidDataException("Unsupported humanoid recipe.");
        var name = Path.GetFileNameWithoutExtension(artPath.Replace('\\', '/'));
        var binding = _recipe.Bindings.SingleOrDefault(row => row.ArtBases.Any(prefix => name.Length == prefix.Length + 2 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        if (binding is null) return null;
        // Armed poses and death states must keep their source art until a matching
        // weapon/socket/action recipe exists. An idle body cannot stand in for them.
        ClassicHeldItem? held = null;
        if (!name.EndsWith("aa", StringComparison.OrdinalIgnoreCase))
        {
            if (equipped is { Definition.Weapon: { } weapon })
                held = _recipe.HeldItems?.SingleOrDefault(row => row.Pid == equipped.Object.Pid && row.WeaponAnimation == weapon.Animation);
            else
            {
                if (placed is null || ((placed.Fid >> 16) & 0xff) != 0) return null;
                var items = placed.Inventory.Where(item => item.Prototype.ObjectType == 0 && item.Prototype.Subtype == 3 && (item.Flags & 0x03000000) != 0).ToArray();
                if (items.Length != 1) return null;
                held = _recipe.HeldItems?.SingleOrDefault(row => row.Pid == items[0].Pid && row.WeaponAnimation == ((placed.Fid >> 12) & 0xf));
            }
            if (held is null) return null;
        }
        var face = _recipe.Premades.SingleOrDefault(row => row.Campaign == choice?.Campaign && row.Id == choice?.PremadeId)?.FaceDonor ?? binding.FaceDonor;
        var faceKey = new FalloutFormKey("FalloutNV.esm", face);
        var outfit = FalloutDialogueTopic.Find(_records, "ARMO", binding.Outfit);
        FalloutActorAppearanceState? state = null;
        if (choice?.Appearance is { } approved)
        {
            if (approved.AppearanceStackId != source.StackId) throw new InvalidDataException("Custom face belongs to another owned appearance stack.");
            var contract = FalloutNativeRaceSexResolver.Resolve(_records);
            FalloutNativeRaceSexResolver.Validate(contract, approved.Character);
            faceKey = contract.Player;
            state = FalloutNativeCharacterCreation.ActorState(_records, faceKey, approved.Character);
        }
        var appearance = FalloutNpcAppearanceResolver.Resolve(_records, faceKey, equippedArmor: [outfit.FormKey], appearanceState: state);
        if (choice?.Appearance is null && binding.HairColor is { } sourceHair)
        {
            var rgb = Convert.FromHexString(sourceHair);
            if (rgb.Length != 3) throw new InvalidDataException("Classic hair colour must contain three RGB bytes.");
            appearance = appearance with { HairColorBytes = [rgb[0], rgb[1], rgb[2], 0] };
        }
        if (appearance.Female != binding.Female || choice is not null && appearance.Female != choice.Character.Female)
            throw new InvalidDataException("Classic source sex differs from its appearance donor.");
        var body = new ClassicPlayerBody { Name = "ClassicHumanoidAnalog" };
        try
        {
            body.Configure(source, _records, appearance, binding.Palette);
            if (choice?.Appearance is null) body.ConfigureShape(binding.ShoulderWidth, binding.WaistWidth);
            if (held is not null) body.BindHeldItem(source, held);
            body.SetMeta("source_art", artPath); body.SetMeta("presentation", "owned-3D-analog; likeness-in-progress");
            body.SetMeta("donor_face", faceKey.ToString()); body.SetMeta("donor_outfit", outfit.FormKey.ToString());
            body.SetMeta("remaining_likeness", binding.Remaining);
            if (choice is not null) body.SetMeta("classic_identity", JsonSerializer.Serialize(choice));
            if (placed is not null) body.SetMeta("classic_inventory", JsonSerializer.Serialize(placed.Inventory));
            return body;
        }
        catch { body.Free(); throw; }
    }

    public void Dispose() => _records.Dispose();
}

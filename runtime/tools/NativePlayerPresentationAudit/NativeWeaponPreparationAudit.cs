using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditWeaponPreparation(FalloutPluginStack records, FalloutNativeCampaignState saved)
    {
        foreach (var form in new uint[] { 0x1735d4, 0x8f21e })
        {
            var weapon = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(form));
            var inventory = new FalloutPlayerInventory();
            inventory.Add(records, weapon.Form, 1, 1, true);
            inventory.Add(records, weapon.Ammunition[0], 120, 1, true);
            inventory.Equip(records, weapon.Form);
            var player = new RuntimeNativePlayer();
            player.Configure(RuntimeConfiguration.Load(), Transform3D.Identity);
            AddChild(player); player.SetPhysicsProcess(false);
            using var world = new FalloutReferenceWorld(records);
            var package = new RuntimeNativePlayerPackage(records, player, new FalloutScriptSession(), world, () => saved.ActiveCell);
            try
            {
                var appearance = FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(7),
                    appearanceState: FalloutNativeCharacterCreation.ActorState(records, records.RuntimeFormKey(7), saved.Character));
                player.ConfigurePresentation(records, inventory, () => appearance, () => Colors.White);
                var handling = JsonSerializer.Serialize(player.CaptureWeaponHandling());
                var audio = JsonSerializer.Serialize(package.CaptureAudio());
                var items = JsonSerializer.Serialize(inventory.Items);
                for (var frame = 0; frame < 8; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var state = JsonSerializer.SerializeToElement(player.FiringState);
                if (state.GetProperty("resourcePreparation").GetProperty("error").ValueKind != JsonValueKind.Null ||
                    state.GetProperty("shots").GetInt64() != 0 ||
                    records.SoundVoices.ActiveVoices != 0 ||
                    state.GetProperty("muzzle").GetProperty("visible").GetBoolean() ||
                    state.GetProperty("muzzle").GetProperty("particles").EnumerateArray().Any(particle => particle.GetProperty("BirthCount").GetInt64() != 0) ||
                    JsonSerializer.Serialize(player.CaptureWeaponHandling()) != handling ||
                    JsonSerializer.Serialize(package.CaptureAudio()) != audio ||
                    JsonSerializer.Serialize(inventory.Items) != items)
                    throw new InvalidOperationException("Resource preparation dispatched gameplay/audio, changed randomness/ammunition, or failed its source binding.");
                GD.Print($"OPENNV_WEAPON_PREPARATION_PASS weapon={weapon.Form} shots=0 muzzleHidden=true births=0 " +
                    "handlingAndRandomUnchanged=true inventoryUnchanged=true audioHistoryUnchanged=true nativeVoices=0 gameplay=separate");
            }
            finally { player.Free(); }
        }
    }
}

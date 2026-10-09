using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.InputSystem;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutNativePluginCampaign? _nativePluginCampaign;
    private string? _nativePluginRetirementFailure;
    private RuntimeNativeDirectInput? _nativeDirectInput;
    private FalloutInventoryReferenceStore? _nativeInventoryReferences;
    private void BindNativePluginCampaign()
    {
        if (_nativePluginCampaign is not null || _nativeDirectInput is not null || _nativeInventoryReferences is not null)
            throw new InvalidOperationException("Actual campaign still owns native module/data generations.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Native campaign has no selected source.");
        var records = _nativePluginStack ?? throw new InvalidOperationException("Native campaign has no actual winning records.");
        var quests = _nativeQuestState ?? throw new InvalidOperationException("Native campaign has no actual quest state.");
        var scripts = _nativeQuestScripts?.Scripts ?? throw new InvalidOperationException("Native campaign has no actual script/clock/value stores.");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var companion = _options.GetValueOrDefault("native-plugin-companion",
            ProjectSettings.GlobalizePath("res://generated/native-plugins/" + configuration + "/opennv_plugin_domain.exe"));
        var privateRoot = Path.Combine(OS.GetUserDataDir(), "native-plugin-state");
        var declarations = source.EnsureNativePluginDeclarations();
        GD.Print("OPENNV_NATIVE_SOURCE_DECLARATIONS " + System.Text.Json.JsonSerializer.Serialize(new
        { source.StackId, declarations.Inventory, Initialization = "pending-original-calls" }));
        var world = scripts.References ?? throw new InvalidOperationException("Native campaign has no shared live reference owner.");
        if (world.NativePlugins is not null) throw new InvalidOperationException("World native command owner is already bound.");
        try
        {
            var controls = scripts.Controls ?? throw new InvalidOperationException("Actual source controls are unbound.");
            var input = RuntimeNativeDirectInput.Create(source.StackId, controls);
            _nativeDirectInput = input;
            AddChild(input);
            if (!input.IsInsideTree() || input.GetParent() != this)
                throw new InvalidOperationException("Actual DirectInput owner was not attached to its product lifetime.");
            var inventory = new FalloutInventoryReferenceStore(records, world,
                () => _nativeOpeningStageDriver?.PlayerLevel ?? throw new NotSupportedException("Actual player level has not been constructed."),
                () => _nativeGlobals ?? throw new NotSupportedException("Actual globals are unbound."));
            _nativeInventoryReferences = inventory;
            var owner = new FalloutNativePluginCampaign(records, quests, scripts, companion, privateRoot, declarations,
                new(input.State, inventory));
            world.NativePlugins = owner; _nativePluginCampaign = owner;
            _nativeQuestScripts!.BindDirectInput(input.State);
        }
        catch (FalloutNativePluginCampaignConstructionFault fault)
        {
            // Failed native termination still owns callable data and the input
            // device. Retain the genuine campaign until its exact children close.
            world.NativePlugins = fault.Owner; _nativePluginCampaign = fault.Owner;
            throw;
        }
        catch (Exception original)
        {
            try { RetireNativePluginCampaign(); }
            catch (Exception retirement) { throw new AggregateException("Native campaign/data construction and retirement failed.", original, retirement); }
            throw;
        }
    }
    private void RetireNativePluginCampaign()
    {
        var owner = _nativePluginCampaign;
        var failures = new List<Exception>();
        if (owner is not null)
        {
            try { owner.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (owner.ChildDomainsExited)
            {
                _nativePluginCampaign = null;
                if (_nativeQuestScripts?.Scripts.References is { } world && ReferenceEquals(world.NativePlugins, owner))
                    world.NativePlugins = null;
            }
        }
        if (_nativePluginCampaign is null)
        {
            if (_nativeInventoryReferences is { } inventory)
                try { inventory.Retire(); _nativeInventoryReferences = null; } catch (Exception error) { failures.Add(error); }
            if (_nativeDirectInput is { } input)
                try
                {
                    input.Retire();
                    if (GodotObject.IsInstanceValid(input)) input.Free();
                    _nativeDirectInput = null;
                }
                catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0)
        {
            var error = new AggregateException("Native campaign/data retirement retained independent failures.", failures);
            _nativePluginRetirementFailure = error.ToString(); throw error;
        }
    }
    private void RequireNativePluginSaveBoundary() => _nativePluginCampaign?.RequireIdleForSave();
}

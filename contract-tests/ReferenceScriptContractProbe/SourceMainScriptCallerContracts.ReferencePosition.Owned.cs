using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static int AuditOwnedReferenceStartup(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var owned = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(owned.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var controls = FalloutPlayerMovementControlSource.Reviewed(main);
        var before = SHA256.HashData(File.ReadAllBytes(owned.FalloutExecutablePath));
        var fieldSource = FalloutExecutableStringTable.ReadPackageCommandFields(owned.FalloutExecutablePath, controls);
        var source = FalloutExecutableStringTable.ReadReferencePosition(owned.FalloutExecutablePath, fieldSource);
        var fileSource = FalloutExecutableStringTable.ReadReferenceFormFileAssociation(owned.FalloutExecutablePath, source);
        var cellSource = FalloutExecutableStringTable.ReadInitialPlacementCells(owned.FalloutExecutablePath, source);
        using var fixture = new ReferencePositionFixture(constructGrid: false, original: records, controls: controls);
        var inputs = FalloutReferenceBaseAssignmentInputs.Read(source, records, fixture.Actor); inputs.Validate(source, fixture.Actor);
        var ini = FalloutInitialPlacementCellsSource.ReadGridInput(FalloutInstallationSettings.Read(owned));
        fixture.Call(FalloutReferencePositionIngress.ControllerConstruction, inv => fixture.Cells.ConstructGrid(inv, ini));
        var file = fixture.Call(FalloutReferencePositionIngress.ActorPosition, fixture.Files.ReadCurrentFile);
        var first = fixture.Call(FalloutReferencePositionIngress.ConstructorStartup, inv => fixture.Position.StoreConstructorStartupPosition(inv, fixture));
        fixture.Initialize();
        var prefix = fixture.Call(FalloutReferencePositionIngress.ActorPosition, inv => fixture.Position.InitializeConstructedPlayerFields(inv, FalloutInstallationSettings.Read(owned)));
        var second = fixture.Call(FalloutReferencePositionIngress.ConfiguredStartup, inv => fixture.Position.StoreConfiguredStartupPosition(inv, fixture));
        Require(file is { Count: 0, File: null } && first is { Returned: true, Change.Suppressed: true } && prefix.Returned && second.Returned &&
            fixture.Changes.Capture().Entries.Single().Flags == 2 && fixture.Cells.GridConstruction is { Returned: true } &&
            SHA256.HashData(File.ReadAllBytes(owned.FalloutExecutablePath)).AsSpan().SequenceEqual(before),
            "Owned reference startup lost an original selector, distinct vector, source dependency or read-only source bytes.");
        var saved = fixture.Capture();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var cold = new ReferencePositionFixture(saved, original: records, controls: controls);
            Require(cold.Events.Count == 0 && cold.Call(FalloutReferencePositionIngress.ActorPosition, cold.Position.ReadCurrentPosition).Position == second.After.Position,
                "Owned cold scalar fields replayed original startup or borrowed native pose.");
            saved = cold.Capture();
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "OPENNV_OWNED_REFERENCE_STARTUP_PASS", source = source.Identity,
            fileSource = fileSource.Identity, cellSource = cellSource.Identity, file, inputs, first, prefix, second,
            coldCycles = 3, scope = "AUTHORED_WITH_OWNED_DATA", originalStartup = "UNEXECUTED", wholePlayer = "UNEXECUTED",
            controllerReset = "AUTHORED_COMPONENT_CALLBACK", canonicalParity = "UNVERIFIED" }));
        return 0;
    }
}

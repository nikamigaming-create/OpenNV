using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunObservedPlayerControlCommands(FalloutPlayerMovementControlSource source, string stack)
    {
        var arguments = new[] { false, true, false, true, true };
        var observed = new FalloutPlayerControlCommandObservation(new("ObservedControlFixture.esm", 0x14),
            "authored-actual-script-host-effect", Guid.NewGuid(), false, arguments);
        var fields = new FalloutPlayerControlFieldsState(source, stack, Guid.NewGuid());
        fields.ObserveCommand(observed); arguments[0] = true;
        var saved = fields.Capture();
        Require(saved is { WordAvailable: false, DisabledWord: 0, FlagStores: 0, Call: null, Sequence: 1 } &&
            saved.Observations is [{ Source.ObjectId: 0x14, Enable: false }] && !saved.Observations[0].Arguments[0],
            "Observed command invented source stores or lost immutable original source arguments.");
        Reject(() => fields.ReadDisabledWord());
        Reject(() => fields.ApplyCommand(Guid.NewGuid(), "unowned-later-mask-writer", new(true, [])));
        Reject(() => fields.ObserveCommand(observed));
        Require(fields.Capture().Sequence == 1 && fields.ReadYawOffset() == 0,
            "Unbound command replay changed field stores or contaminated the independent yaw offset.");
        Reject(() => FalloutPlayerControlFieldsState.Validate(saved with { WordAvailable = true }, source, stack));
        Reject(() => FalloutPlayerControlFieldsState.Validate(saved with { Observations = [] }, source, stack));
        var roundTrip = JsonSerializer.Deserialize<FalloutPlayerControlFieldsSnapshot>(JsonSerializer.Serialize(saved))!;
        var cold = new FalloutPlayerControlFieldsState(source, stack, Guid.NewGuid(), roundTrip);
        Reject(() => cold.ReadDisabledWord());
        Require(cold.Capture().FlagStores == 0 && cold.Capture().Observations![0].SameCommand(roundTrip.Observations![0]),
            "Cold command observations replayed a source write or regained current-word availability.");
        var committed = new FalloutPlayerControlFieldsState(source, stack, Guid.NewGuid());
        var effects = new AuthoredControlFlagEffects(source, stack, committed.Capture().Process);
        committed.ApplyCommand(Guid.NewGuid(), "authored-source-writer", new(false, [false, false, false, false, true]), effects);
        var before = committed.Capture(); committed.ObserveCommand(observed);
        Require(committed.Capture() is { WordAvailable: false, DisabledWord: 2, FlagStores: 2, Call.Returned: true } &&
            committed.Capture().Call == before.Call && effects.Calls.Count == 1,
            "Observing a later unowned command erased completed history or replayed actual effects.");
        Console.WriteLine("OPENNV_PLAYER_CONTROL_OBSERVATIONS_PASS exactArgs=true currentWordUnavailable=true independentYaw=true coldNoWrites=true");
    }
}

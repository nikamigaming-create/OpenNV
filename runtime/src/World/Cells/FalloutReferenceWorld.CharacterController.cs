using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutCharacterControllerFactory ReadSourcePlayerCharacterControllerFactory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var process = ActorProcesses.Read(_enginePlayer);
        if (process.Retired || process.Level is not { } level || level != FalloutDetectionProcessLevel.High)
            throw new InvalidDataException("Actual Player character controller requires its real current High process epoch.");
        var body = ProcessCommon.RequireCharacterControllerSourceBody(_enginePlayer, process.Epoch);
        var result = new FalloutCharacterControllerFactory(FalloutCharacterControllerSource.Read(
            FalloutMainPlayerPendingSource.Read(CampaignMainPlayerCellSource)), _processRuntimeStack!,
            ProcessRuntime.CharacterControllerSourceProcess, process.Epoch, level, ReadCombatActorIdentity(_enginePlayer),
            body, "actual-source-Player-High-character-controller-field-factory");
        FalloutCharacterControllerState.RequireFactory(result); return result;
    }

    internal void RequireSourcePlayerCharacterControllerFactory(FalloutCharacterControllerFactory factory)
    {
        var actual = ReadSourcePlayerCharacterControllerFactory();
        if (factory.Source != actual.Source || factory.Stack != actual.Stack || factory.Process != actual.Process ||
            factory.ProcessEpoch != actual.ProcessEpoch || factory.Level != actual.Level || factory.Actor != actual.Actor ||
            !FalloutActorProcessCommonState.BodyEquivalent(factory.Body, actual.Body))
            throw new InvalidDataException("Character controller changed its genuine current source/body/process factory.");
    }

    internal IDisposable BindCurrentPlayerCharacterController(IFalloutCharacterControllerBody body)
    {
        RequireSourcePlayerCharacterControllerFactory(body.Factory);
        var current = CampaignPlayerPendingConsumers.BindCharacterController(body);
        return new PlayerCharacterControllerLease(current, body);
    }
    private sealed class PlayerCharacterControllerLease(FalloutCharacterControllerState controller,
        IFalloutCharacterControllerBody body) : IDisposable
    {
        private bool _retired;
        public void Dispose()
        {
            if (_retired) return;
            controller.Detach(body); _retired = true;
        }
    }
}

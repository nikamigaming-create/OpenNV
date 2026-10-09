using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutConsolePresence { Unowned, Absent, Present }
internal sealed record FalloutConsoleActivitySource(string EngineSha256, string RuntimeSha256, string ContractSha256)
{
    private const string Contract = "IsConsoleOpen;existing-interface-and-enabled-interface-flag;existing-console;" +
        "signed-source-open-counter-greater-than-zero;not-console-execution-TLS-not-save-completion";
    internal string Identity => FalloutInterfaceFadeSource.Hash(EngineSha256 + "\0" + RuntimeSha256 + "\0" + ContractSha256);
    internal static string CurrentContractSha256 => FalloutInterfaceFadeSource.Hash(Contract);
    internal static FalloutConsoleActivitySource Read(FalloutAdvancementRuntimeReceipt runtime)
    {
        runtime.Validate();
        if (runtime.EngineSha256 is not ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" or
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"))
            throw new NotSupportedException("Selected console activity predicate is unreviewed.");
        return new(runtime.EngineSha256, runtime.SourceSha256, FalloutInterfaceFadeSource.Hash(Contract));
    }
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(EngineSha256) || !FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) ||
            ContractSha256 != FalloutInterfaceFadeSource.Hash(Contract) || EngineSha256 is not
                ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" or
                    "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"))
            throw new InvalidDataException("Console activity changed selected source contract.");
    }
}
// These are observations of actual factories/controllers, not absence inferred
// from a missing callback. Counter is signed source storage, never a job count.
internal sealed record FalloutConsoleActivitySample(string SourceIdentity, string Producer,
    FalloutConsolePresence Interface, bool? InterfaceEnabled, FalloutConsolePresence Console, sbyte? OpenCounter)
{
    internal void Validate(FalloutConsoleActivitySource source)
    {
        if (SourceIdentity != source.Identity || string.IsNullOrWhiteSpace(Producer) || !Enum.IsDefined(Interface) || !Enum.IsDefined(Console) ||
            Interface != FalloutConsolePresence.Present && InterfaceEnabled is not null ||
            Interface == FalloutConsolePresence.Present && InterfaceEnabled is null ||
            Console != FalloutConsolePresence.Present && OpenCounter is not null ||
            Console == FalloutConsolePresence.Present && OpenCounter is null)
            throw new InvalidDataException("Console activity has no complete actual source factory/controller observation.");
    }
}
internal sealed class FalloutConsoleActivity
{
    internal FalloutConsoleActivitySource Source { get; }
    private Func<FalloutConsoleActivitySample>? _read;
    private bool _retired;
    private string? _failure;
    internal string? Failure => _failure;
    internal object State => new { Source, bound = _read is not null, retired = _retired, failure = _failure };
    internal FalloutConsoleActivity(FalloutConsoleActivitySource source) { source.Validate(); Source = source; }
    internal void Bind(Func<FalloutConsoleActivitySample> actualControllerRead)
    {
        ArgumentNullException.ThrowIfNull(actualControllerRead);
        if (_read is not null || _retired || _failure is not null)
            throw new InvalidOperationException("Console activity cannot replace its actual source controller lifetime.");
        _read = actualControllerRead;
    }
    internal FalloutRestObservation ObserveRestClose()
    {
        var identity = "actual-IsConsoleOpen:" + Source.Identity;
        if (_failure is not null) return new(FalloutRestFactState.Unowned, identity, _failure);
        if (_read is null || _retired)
            return new(FalloutRestFactState.Unowned, identity, "The selected interface/console factory and input counter are not published.");
        try
        {
            var sample = _read(); sample.Validate(Source);
            if (sample.Interface == FalloutConsolePresence.Unowned)
                return new(FalloutRestFactState.Unowned, identity + ":" + sample.Producer, "Actual source interface existence is unowned.");
            if (sample.Interface == FalloutConsolePresence.Absent || sample.InterfaceEnabled == false)
                return new(FalloutRestFactState.Satisfied, identity + ":" + sample.Producer);
            if (sample.Console == FalloutConsolePresence.Unowned)
                return new(FalloutRestFactState.Unowned, identity + ":" + sample.Producer, "Actual source console existence is unowned.");
            if (sample.Console == FalloutConsolePresence.Absent || sample.OpenCounter <= 0)
                return new(FalloutRestFactState.Satisfied, identity + ":" + sample.Producer);
            return new(FalloutRestFactState.Denied, identity + ":" + sample.Producer, "The actual source console is open.");
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            _failure ??= error.GetType().Name + ": " + error.Message;
            return new(FalloutRestFactState.Unowned, identity, _failure);
        }
    }
    internal void Retire() { _retired = true; _read = null; }
}

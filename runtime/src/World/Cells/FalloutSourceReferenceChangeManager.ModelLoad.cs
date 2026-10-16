using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerModelChangeFlagsRead(FalloutReferenceModelDataInvocation Invocation, string Source,
    string Stack, Guid Process, Guid Factory, Guid Constructor, long Sequence, uint Flags);

internal sealed partial class FalloutSourceReferenceChangeManager
{
    private bool _modelLoadFlagsReading;
    internal FalloutPlayerModelChangeFlagsRead ReadPlayerModelLoadFlags(FalloutReferenceModelDataInvocation invocation,
        FalloutPlayerModelLoadPrerequisiteSource source, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(requireActual);
        if (_current is not null || _modelLoadFlagsReading)
        { ++_fault; throw new InvalidOperationException("Player model load reentered the same actual change-manager fields."); }
        var fault = _fault; _modelLoadFlagsReading = true;
        try
        {
            GuardLoad();
            if (_writerBoundary is not null) throw new NotSupportedException(_writerBoundary);
            var result = new FalloutPlayerModelChangeFlagsRead(invocation, source.Identity, Stack, Process, Factory, Constructor, _sequence, _managerFlags);
            GuardLoad(); return result;
        }
        finally { _modelLoadFlagsReading = false; }

        void GuardLoad()
        {
            invocation.Actor.Validate();
            if (_retired || !_modelLoadFlagsReading || _current is not null || _fault != fault || source.Reference.Identity != Source.Identity ||
                invocation.Call == Guid.Empty || invocation.ReferenceProcess != Process || invocation.ReferenceConstructor == Guid.Empty ||
                invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 || invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad ||
                invocation.ThreadId != Environment.CurrentManagedThreadId || _thread is { } thread && thread != invocation.ThreadId)
                throw new InvalidOperationException("Player model load lost its actual shared change manager, caller or current thread.");
            requireActual(invocation);
            if (_retired || !_modelLoadFlagsReading || _current is not null || _fault != fault)
                throw new InvalidOperationException("Player model load guard swallowed reentry or replaced the actual flags owner.");
            _thread ??= invocation.ThreadId;
        }
    }
}

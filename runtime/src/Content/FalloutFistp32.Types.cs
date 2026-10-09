namespace OpenNV.Runtime.Content;

internal enum FalloutFistpOperation : byte { Observe, Convert, SourceArgument }
internal enum FalloutFistpOutcome : byte { Unset, Observed, Converted, PendingException, UnmaskedException, ReservedPrecision, OccupiedStack }
internal enum FalloutFistpRounding : byte { NearestEven, Down, Up, TowardZero }
internal sealed record FalloutFistpObservation(uint Abi, uint Bytes, uint NativeProcess, uint NativeThread,
    uint InputBits, int Value, ushort ControlBefore, ushort StatusBefore, ushort ControlAfter, ushort StatusAfter,
    byte TagBefore, byte TagAfter, FalloutFistpOutcome Outcome, FalloutFistpOperation Operation, uint Reserved,
    uint ArgumentBits, ushort StatusAtConversion, ushort ReservedTail);
internal sealed record FalloutFistpHostBinding(Guid Identity, string AdapterSha256, int ManagedThread,
    FalloutFistpObservation Construction);
internal sealed record FalloutFistp32Value(int Integer, ushort RaisedExceptions, bool Inexact, bool Invalid);

internal static class FalloutFistp32Abi
{
    internal const uint Version = 1, Bytes = 48;
    internal static void RequireHeader(FalloutFistpObservation value, FalloutFistpOperation operation, uint input)
    {
        if (value is null || value.Abi != Version || value.Bytes != Bytes || value.NativeProcess == 0 || value.NativeThread == 0 ||
            value.InputBits != input || value.Operation != operation || value.Reserved != 0 || value.ReservedTail != 0 ||
            !Enum.IsDefined(value.Operation) || !Enum.IsDefined(value.Outcome) || value.Outcome == FalloutFistpOutcome.Unset)
            throw new InvalidDataException("Calling-thread x87 observation lost its exact ABI, operand or native identity.");
        if (operation == FalloutFistpOperation.Observe && (value.Outcome != FalloutFistpOutcome.Observed || input != 0 || value.Value != int.MinValue ||
            value.ControlBefore != value.ControlAfter || value.StatusBefore != value.StatusAfter || value.TagBefore != value.TagAfter ||
            value.ArgumentBits != 0 || value.StatusAtConversion != value.StatusBefore))
            throw new InvalidDataException("Calling-thread x87 construction observation changed the live environment.");
        if (operation != FalloutFistpOperation.Observe && value.Outcome == FalloutFistpOutcome.Observed)
            throw new InvalidDataException("An environment observation was substituted for the actual FISTP instruction.");
    }
    internal static void RequireBinding(FalloutFistpHostBinding binding)
    {
        if (binding is null || binding.Identity == Guid.Empty || binding.ManagedThread < 1 || binding.AdapterSha256 is not { Length: 64 } ||
            !binding.AdapterSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Calling-thread float host has no actual construction/image/thread lease.");
        RequireHeader(binding.Construction, FalloutFistpOperation.Observe, 0);
    }
}

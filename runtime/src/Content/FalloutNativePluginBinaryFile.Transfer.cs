namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginBinaryFile
{
    internal sealed record BinaryPiece(uint Length, uint? SourceOffset, byte[]? Bytes)
    {
        internal static BinaryPiece Source(uint offset, uint length) => new(length, offset, null);
        internal static BinaryPiece Memory(byte[] bytes) => new(checked((uint)bytes.Length), null, bytes);
    }

    internal sealed class FalloutNativeBinaryTransfer
    {
        private readonly FalloutNativePluginBinaryFile _owner;
        private readonly IReadOnlyList<BinaryPiece> _pieces;
        private uint _position;
        internal ulong Id { get; }
        internal uint Requested { get; }
        internal uint Actual { get; }
        internal uint Position => _position;
        internal bool Completed { get; private set; }
        internal FalloutNativeBinaryTransfer(FalloutNativePluginBinaryFile owner, ulong id, uint requested,
            uint actual, IReadOnlyList<BinaryPiece> pieces)
        {
            _owner = owner; Id = id; Requested = requested; Actual = actual; _pieces = pieces;
            if (pieces.Aggregate(0UL, (sum, piece) => checked(sum + piece.Length)) != actual)
                throw new InvalidDataException("Binary transfer lacks its complete actual byte extent.");
        }
        internal byte[] Slice(uint offset, uint count)
        {
            Require();
            if (offset != _position || count == 0 || count > 1024 * 1024 || count > Actual - _position)
                throw new InvalidDataException("Binary output has a repeated/skipped/out-of-range transfer slice.");
            var output = new byte[checked((int)count)]; var done = 0; var basis = 0UL;
            try
            {
                foreach (var piece in _pieces)
                {
                    var absolute = checked((ulong)offset + (uint)done);
                    var end = checked(basis + piece.Length);
                    if (absolute >= basis && absolute < end)
                    {
                        var within = checked((uint)(absolute - basis));
                        var take = checked((int)Math.Min((uint)(output.Length - done), piece.Length - within));
                        if (piece.SourceOffset is { } original)
                            _owner.ReadOriginal(checked(original + within), output.AsSpan(done, take));
                        else if (piece.Bytes is { } memory && memory.Length == piece.Length)
                            memory.AsSpan(checked((int)within), take).CopyTo(output.AsSpan(done, take));
                        else throw new InvalidDataException("Binary transfer piece lost its actual source owner.");
                        done = checked(done + take);
                        if (done == output.Length) break;
                    }
                    basis = end;
                }
                if (done != output.Length) throw new InvalidDataException("Binary output did not account for its complete slice.");
                _position = checked(_position + count); return output;
            }
            catch (Exception error) { _owner._fault = error; throw; }
        }
        internal void Complete()
        {
            Require();
            if (_position != Actual) throw new InvalidOperationException("Binary output ended before the complete original extent.");
            Completed = true; _owner._pending = null;
        }
        internal void Fail(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            // A failed Slice already faults the backend. Retaining that same
            // failure must not invoke the healthy-reader guard and replace it
            // with a second exception before its original prefix is recorded.
            ObjectDisposedException.ThrowIf(_owner._disposed, _owner);
            if (Environment.CurrentManagedThreadId != _owner._thread || Completed || !ReferenceEquals(_owner._pending, this))
                throw new InvalidOperationException("Binary failure has no current exact output-transfer lifetime.");
            _owner._fault = _owner._fault is null || ReferenceEquals(_owner._fault, failure) ? failure :
                new AggregateException("Binary transfer retains backend and output failures.", _owner._fault, failure);
            // Retain this exact output identity and transferred prefix. Neither
            // a later call nor retirement can label it completed or replay it.
        }
        private void Require()
        {
            _owner.RequireCurrent();
            if (Completed || !ReferenceEquals(_owner._pending, this))
                throw new InvalidOperationException("Binary transfer is foreign, completed or retired.");
        }
    }
}

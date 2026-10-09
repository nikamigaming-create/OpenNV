using System.Buffers.Binary;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginBinaryFile
{
    internal FalloutNativeBinaryTransfer ReadInherited(uint count) => ReadCore(count, false);
    internal uint BinaryCursor() { RequireIdle(); return _binaryOffset; }
    internal void SeekMember(uint argument, uint sourceOrigin)
    {
        RequireIdle();
        if (!_good) return;
        var effectiveOrigin = sourceOrigin; var inheritedArgument = argument;
        uint logical;
        if (sourceOrigin == _construction.SeekSet)
        {
            logical = argument;
            // The selected read-only derived wrapper converts Set to a
            // relative request using its own logical word, not the backend.
            effectiveOrigin = _construction.SeekCurrent;
            inheritedArgument = unchecked(argument - _logicalOffset);
        }
        else if (sourceOrigin == _construction.SeekCurrent) logical = unchecked(_logicalOffset + argument);
        else if (sourceOrigin == _construction.SeekEnd) logical = unchecked(Size() - argument);
        else return; // The original derived wrapper leaves an unknown origin unchanged.
        if (logical == _logicalOffset) return;

        if (effectiveOrigin == _construction.SeekCurrent)
        {
            var candidate = unchecked((int)(_consumed + inheritedArgument));
            if (candidate >= 0 && (uint)candidate < _valid)
            {
                _consumed = checked((uint)candidate); _binaryOffset = unchecked(_binaryOffset + inheritedArgument);
                _logicalOffset = logical; _revision = checked(_revision + 1); return;
            }
            // A real retained buffer has already advanced the CRT backend.
            // Its unread bytes are subtracted from the relative request.
            inheritedArgument = unchecked(inheritedArgument + _consumed - _valid);
        }
        // The inherited read flush commits before its backend seek. Original
        // bytes already written into the allocation remain, including tails.
        _valid = _consumed = 0; _revision = checked(_revision + 1);
        var signedArgument = unchecked((int)inheritedArgument);
        var physical = effectiveOrigin == _construction.SeekCurrent ? (long)_backendOffset + signedArgument :
            effectiveOrigin == _construction.SeekEnd ? _input.Length + signedArgument : signedArgument;
        if (physical < 0)
        {
            // A seek before byte zero has the source failure/status behavior:
            // it leaves backend/independent cursor unchanged, clears good,
            // and the derived wrapper still publishes its logical target.
            _good = false; _logicalOffset = logical; return;
        }
        if (physical > int.MaxValue)
            throw new NotSupportedException("Original signed CRT seek/ftell large-file result remains unowned after its real buffer-flush prefix.");
        _backendOffset = _binaryOffset = checked((uint)physical);
        _good = true; _logicalOffset = logical;
    }

    internal void FlushReadBuffer()
    {
        RequireIdle();
        // The admitted read-mode inherited flush discards valid/consumed bytes.
        // It does not close a file, write anything, seek its backend, clear old
        // written tails or manufacture a CRT flush receipt.
        _valid = _consumed = 0; _revision = checked(_revision + 1);
    }

    internal IReadOnlyList<NativeNvseDataField> BinaryMemberFields()
    {
        RequireCurrent();
        return NativeFields().Select(field => new NativeNvseDataField(field.Offset, field.Bytes.ToArray(), field.Owner)).ToArray();
    }

    internal void RetainMemberFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Binary failure belongs to another original-reader thread.");
        _fault = _fault is null || ReferenceEquals(_fault, error) ? error :
            new AggregateException("Binary member retains source and native-output failures.", _fault, error);
    }

    internal uint MemberWord(int offset)
    {
        RequireCurrent();
        var field = BinaryMemberFields().Single(field => field.Offset == offset);
        if (field.Bytes.Length != 4) throw new InvalidDataException("Binary member scalar is not its source UInt32 extent.");
        return BinaryPrimitives.ReadUInt32LittleEndian(field.Bytes.Span);
    }
}

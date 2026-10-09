using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // A bounded declaration parser for scalar loaded-file wrappers. It is not
    // an instruction interpreter: a different declaration refuses immediately.
    private sealed class BinaryMemberCode(byte[] bytes, uint origin)
    {
        private int _at;
        private byte Take()
        {
            if (_at >= bytes.Length) throw Unowned(); return bytes[_at++];
        }
        private static NotSupportedException Unowned() => new("Selected binary scalar wrapper has an unowned field/ABI declaration.");
        private void Need(params byte[] input)
        { foreach (var value in input) if (Take() != value) throw Unowned(); }
        private int I32()
        {
            if (_at > bytes.Length - 4) throw Unowned();
            var result = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(_at, 4)); _at += 4; return result;
        }
        internal void Frame(byte allocation) => Need(0x55, 0x8b, 0xec, 0x83, 0xec, allocation);
        internal int StoreLocal(int register)
        {
            Need(0x89); if (Take() != (0x45 | register << 3)) throw Unowned(); return unchecked((sbyte)Take());
        }
        internal int LoadLocal(int local)
        {
            Need(0x8b); var mode = Take();
            if ((mode & 0xc7) != 0x45 || unchecked((sbyte)Take()) != local) throw Unowned(); return mode >> 3 & 7;
        }
        internal void LoadLocalInto(int register, int local)
        { if (LoadLocal(local) != register) throw Unowned(); }
        internal int ZeroRegister()
        {
            if (Take() is not (0x31 or 0x33)) throw Unowned(); var mode = Take();
            if ((mode & 0xc0) != 0xc0 || (mode & 7) != (mode >> 3 & 7)) throw Unowned(); return mode & 7;
        }
        internal void CompareFieldZero(int receiver, byte offset)
        { Need(0x83, checked((byte)(0x78 | receiver)), offset, 0); }
        internal void SetNotEqual(int register) => Need(0x0f, 0x95, checked((byte)(0xc0 | register)));
        internal int WidenByte(int register)
        {
            Need(0x0f, 0xb6); var mode = Take();
            if ((mode & 0xc7) != (0xc0 | register)) throw Unowned(); return mode >> 3 & 7;
        }
        internal void TestRegister(int register) => Need(0x85, checked((byte)(0xc0 | register << 3 | register)));
        internal int JumpNotEqual()
        {
            var opcode = Take(); int target;
            if (opcode == 0x75) { var displacement = unchecked((sbyte)Take()); target = checked(_at + displacement); }
            else { if (opcode != 0x0f || Take() != 0x85) throw Unowned(); var displacement = I32(); target = checked(_at + displacement); }
            if (target <= _at || target >= bytes.Length) throw Unowned(); return target;
        }
        internal void Position(int position)
        { if (position < _at || position >= bytes.Length) throw Unowned(); _at = position; }
        internal void PushRegister(int register) => Need(checked((byte)(0x50 | register)));
        internal uint Call()
        { Need(0xe8); var displacement = I32(); return unchecked(origin + checked((uint)_at) + (uint)displacement); }
        internal int LoadField(int receiver, int field)
        {
            Need(0x8b); var mode = Take();
            if ((mode & 0xc7) != (0x80 | receiver) || I32() != field) throw Unowned(); return mode >> 3 & 7;
        }
        internal void AddLocal(int register, int local)
        { Need(0x03, checked((byte)(0x45 | register << 3)), unchecked((byte)local)); }
        internal void StoreField(int receiver, int field, int register)
        { Need(0x89, checked((byte)(0x80 | register << 3 | receiver))); if (I32() != field) throw Unowned(); }
        internal void ReturnFrame(byte stackBytes) => Need(0x8b, 0xe5, 0x5d, 0xc2, stackBytes, 0);
    }
}

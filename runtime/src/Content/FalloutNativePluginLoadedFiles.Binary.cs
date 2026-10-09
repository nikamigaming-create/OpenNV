using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginLoadedFiles
{
    // An actual retained contributor lease owns this adapter. It neither opens
    // another parser nor invents a BSFile/CRT object from these scalar fields.
    internal sealed class BinaryAuthority : INativeNvseBinaryFileAuthority
    {
        private readonly Lease _lease;
        private readonly FalloutNativePluginBinaryFile _file;
        internal BinaryAuthority(Lease lease) { _lease = lease; lease.RequireCurrent(); _file = lease.BinaryFile(); }
        public string BinarySourceSha256 => _lease.SourceFileSha256;
        public string BinaryRuntimeSha256 => File().Construction.RuntimeSha256;
        public uint BinaryCapacity => File().Capacity;
        public void RequireBinaryCurrent() { _lease.RequireCurrent(); File().RequireCurrent(); }
        private FalloutNativePluginBinaryFile File() { _lease.RequireCurrent(); return _file; }
        public NativeNvseBinaryResult InspectBinary()
        {
            RequireBinaryCurrent(); var file = File();
            return new(0, null, file.BinaryMemberFields(), file.WrittenBuffer(), null);
        }

        public NativeNvseBinaryResult CallBinary(NativeNvseBinaryMethod method, uint first, uint second)
        {
            RequireBinaryCurrent(); var file = File(); file.RequireIdle();
            if (!Enum.IsDefined(method)) throw new NotSupportedException("Original binary member has no typed engine owner.");
            if (method is not NativeNvseBinaryMethod.Read and not NativeNvseBinaryMethod.ReadInherited and
                not NativeNvseBinaryMethod.Seek && second != 0 ||
                method is NativeNvseBinaryMethod.Size or NativeNvseBinaryMethod.Cursor or NativeNvseBinaryMethod.FlushReadBuffer && first != 0 ||
                method == NativeNvseBinaryMethod.SelectProcedures && first > 1 ||
                method is NativeNvseBinaryMethod.Read or NativeNvseBinaryMethod.ReadInherited && second != 0)
                throw new InvalidDataException("Binary member arguments differ from their admitted original ABI.");
            INativeNvseBinaryOutput? output = null; var result = 0U;
            try
            {
                switch (method)
                {
                    case NativeNvseBinaryMethod.Read:
                    case NativeNvseBinaryMethod.ReadInherited:
                        var actual = method == NativeNvseBinaryMethod.Read ? file.Read(first) : file.ReadInherited(first);
                        output = new Output(actual); result = actual.Actual; break;
                    case NativeNvseBinaryMethod.Seek: file.SeekMember(first, second); break;
                    case NativeNvseBinaryMethod.SeekCurrent: file.SeekMember(first, file.Construction.SeekCurrent); break;
                    case NativeNvseBinaryMethod.Size: result = file.Size(); break;
                    case NativeNvseBinaryMethod.Cursor: result = file.BinaryCursor(); break;
                    case NativeNvseBinaryMethod.FlushReadBuffer: file.FlushReadBuffer(); result = 1; break;
                    // The native owner installs its real procedure pointers.
                    // Selecting an alternate procedure does not execute it.
                    case NativeNvseBinaryMethod.SelectProcedures: break;
                    default: throw new NotSupportedException("Binary member behavior has no original source owner.");
                }
                return new(result, output, file.BinaryMemberFields(), file.WrittenBuffer(), null);
            }
            catch (Exception error) { file.RetainMemberFailure(error); throw; }
        }

        public void RetainBinaryFailure(Exception error) => _file.RetainMemberFailure(error);
        private sealed class Output(FalloutNativePluginBinaryFile.FalloutNativeBinaryTransfer transfer) : INativeNvseBinaryOutput
        {
            public ulong Id => transfer.Id;
            public uint Requested => transfer.Requested;
            public uint Actual => transfer.Actual;
            public uint Position => transfer.Position;
            public byte[] Slice(uint position, uint count) => transfer.Slice(position, count);
            public void Complete() => transfer.Complete();
            public void Fail(Exception error) => transfer.Fail(error);
        }
    }
}

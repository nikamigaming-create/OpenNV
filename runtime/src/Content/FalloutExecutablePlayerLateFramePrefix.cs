using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerLateFramePrefixSource ReadPlayerLateFramePrefix(string path,
        FalloutPlayerFollowingContinuationSource following)
    {
        var source = FalloutPlayerLateFramePrefixSource.Reviewed(following); source.Validate();
        _ = ReadPlayerFollowingContinuation(path, following.Controls);
        _ = ReadActorMover(path, following.Controls);
        _ = ReadPlayerLateActorTime(path, following);
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != following.Controls.Main.EngineSha256)
            throw new InvalidDataException("Late Player prefix changed its actual selected executable.");
        var (code, image) = Load(original);
        var player = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray() is [var table] ? table :
            throw new NotSupportedException("Late Player prefix lost the selected Player class.");
        var frame = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x2f8, 4), 0));
        Need(frame + 0xca2, [0x80, 0xbf, 0xe5, 6, 0, 0, 0, 0x0f, 0x85], "same following first-inhibition target");
        var late = Near(frame + 0xca9);
        Need(late, [0x80, 0xbf, 0xa9, 5, 0, 0, 0, 0x74, 0x1b], "independent middle request byte before optional view child");
        Need(late + 9, [0x80, 0xbf, 0xa8, 5, 0, 0, 0, 0x75, 0x12], "actual current model-view byte before disabled controls");
        Need(late + 0x12, [0xf6, 0x87, 0xdc, 5, 0, 0, 0x10, 0x74, 9, 0x6a, 1, 0x8b, 0xcf, 0xe8],
            "same disabled-control bit before optional view call");
        Need(late + 0x24, [0x8b, 0x8f, 0x84, 1, 0, 0, 0x0f, 0xb7, 0x45, 0xec, 0x50, 0x8b, 0x11, 0xff, 0x52, 0xc],
            "same mover Flags setter and genuine caller-local unsigned-short input");
        // The control Boolean merge is another local. The input/output word is
        // initialized from the actual prior mover flags and passed by address.
        Need(frame + 0x935, [0x8b, 0x45, 0xd8, 0x8b, 0x8f, 0xa8, 0, 0, 0, 0x25, 0, 0xcc, 0, 0,
            0x89, 0x45, 0xec], "same masked inout word before the independent physical/control child");
        Need(frame + 0x94b, [0xf3, 0x0f, 0x10, 0x43, 8, 0x8d, 0x45, 0xec, 0x6a, 0, 0x50, 0x6a, 0,
            0x51, 0x8b, 0xcf, 0xf3, 0x0f, 0x11, 4, 0x24, 0xe8], "source scene argument and actual word address before control call");
        Need(late + 0x34, [0xf3, 0x0f, 0x10, 5], "current shared inhibition-time read after returned Flags setter");
        var scalar = Word(code, late + 0x38);
        Need(late + 0x3c, [0x0f, 0x2f, 5], "actual Float32 threshold read");
        if (Word(image.Read(Word(code, late + 0x3f), 4), 0) != FalloutPlayerLateFramePrefixSource.InhibitionThresholdBits ||
            !image.IsWritableExtent(scalar, 4) || Word(image.ReadLoaderData(scalar, 4), 0) != FalloutPlayerLateFramePrefixSource.InitialInhibitionTimerBits)
            throw new NotSupportedException("Late Player prefix changed its own shared scalar or fifteen comparison.");
        Need(late + 0x43, [0x76, 0x31], "unordered-or-not-greater edge to second inhibition");
        Need(late + 0x76, [0x80, 0xbf, 0xe4, 6, 0, 0, 0, 0x0f, 0x84], "second inhibition before first read");
        var first = Near(late + 0x7d);
        Need(first, [0x80, 0xbf, 0xe5, 6, 0, 0, 0, 0x74], "independent first inhibition on returned second-zero arm");
        var view = checked(first + 9 + unchecked((sbyte)code[first + 8]));
        Need(view, [0x8a, 0x87, 0xaa, 5, 0, 0, 0x3a, 0x87, 0xa8, 5, 0, 0, 0x75, 0x12],
            "actual desired/current fields at the zero-lane edge");
        if (view != late + 0x23f)
            throw new NotSupportedException("Late Player zero lanes no longer join the actual Actor-time comparison.");
        Need(view + 0xe, [0xf3, 0x0f, 0x10, 0x43, 8, 0x51, 0x8b, 0xcf, 0xf3, 0x0f, 0x11, 4, 0x24, 0xe8],
            "current source scene argument before the same selected Actor-time child");
        Need(Call(view + 0x1b), [0x55, 0x8b, 0xec, 0x83, 0xec, 0x14, 0x57, 0x8b, 0xf9], "same independent Actor-time receiver");

        RequirePlayerControlFlagWriters(code, image);
        var mask = Call(Handler("DisablePlayerControls") + 0xb6); var setter = Call(mask + 0x1a);
        var povScalar = Word(code, setter + 0x6d);
        if (povScalar == scalar)
            throw new NotSupportedException("Late Player inhibition time was aliased to the independent POV camera scalar.");
        var constructors = Enumerable.Range(0, code.Length - 0x64a).Where(at =>
            Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
            Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]) &&
            Match(code, at + 0x54a, [0xc7, 0x87, 0xa8, 5, 0, 0, 0, 0, 0, 0]) &&
            Match(code, at + 0x640, [0xc7, 5]) && Word(code, at + 0x642) == povScalar && Word(code, at + 0x646) == 0).ToArray();
        if (constructors is not [_])
            throw new NotSupportedException("Late Player request byte has no unique genuine packed-zero Player constructor.");
        Need(late + 0x6e, [0xf3, 0x0f, 0x10, 5], "second shared timer read after returned over-threshold raw-state children");
        Need(late + 0x83, [0xf3, 0x0f, 0x58, 0x43, 8], "second-inhibition adds actual source scene argument");
        Need(late + 0x8c, [0xf3, 0x0f, 0x11, 5], "second-inhibition timer store before process children");
        Need(late + 0x1f0, [0xf3, 0x0f, 0x58, 0x43, 8], "first-inhibition adds the same actual source argument");
        Need(late + 0x1ff, [0xf3, 0x0f, 0x11, 5], "first-inhibition timer store before its raw-state child");
        foreach (var relative in new[] { 0x1c7, 0x1d8, 0x235 })
        {
            Need(late + relative, [0xc7, 5], "distinct reached timer-zero store");
            if (Word(code, late + relative + 2) != scalar || Word(code, late + relative + 6) != 0)
                throw new NotSupportedException("Late Player reset borrowed another scalar or manufactured a nonzero value.");
        }
        var expected = new HashSet<int>(new[] { 0x38, 0x72, 0x90, 0x1c9, 0x1da, 0x203, 0x237 }.Select(relative => late + relative));
        var actual = new HashSet<int>();
        for (var at = 0; at <= code.Length - sizeof(uint); at++)
            if (Word(code, at) == scalar) actual.Add(at);
        if (!actual.SetEquals(expected))
            throw new NotSupportedException("Late inhibition timer acquired an unowned direct source symbol/preceding writer.");
        return source;

        int Near(int at) => checked(at + 6 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 2)));
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Late Player prefix lost an actual direct child.");
        int Handler(string name)
        {
            var handlers = new List<int>();
            foreach (var (_, data) in image.StaticDescriptorSections())
                for (var at = 0; at <= data.Length - 40; at += 4)
                    if (image.Literal(Word(data, at)) == name && Word(data, at + 0x10) == 0x00070000 &&
                        image.IsExecutableExtent(Word(data, at + 0x18)))
                        handlers.Add(SourceOffset(code, image.CodeBase, Word(data, at + 0x18)));
            return handlers is [var handler] ? handler : throw new NotSupportedException("Late Player has no unique source control handler.");
        }
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Late Player prefix changed " + role + "."); }
    }
}

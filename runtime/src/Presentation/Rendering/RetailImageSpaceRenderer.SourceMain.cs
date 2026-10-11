using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static partial class RetailImageSpaceRenderer
{
    internal static Application CreateFromSource(FalloutSourceImageSpaceProfile source,
        RetailImageSpaceConfiguration configuration, CaptureConfiguration capture, ColorTransferConfiguration outputTransfer)
    {
        source.Validate();
        float Read(int index) => BitConverter.UInt32BitsToSingle(source.ReadWord(index));
        var cinematic = new Vector4(Read(25), Read(26), Read(27), Read(28));
        var tint = new Vector4(Read(29), Read(30), Read(31), Read(32));
        var fade = new Vector4(Read(33), Read(34), Read(35), Read(36));
        var effect = new RetailHdrCompositorEffect(Read(4), 1, cinematic, tint, fade,
            configuration, capture, outputTransfer, runtimeAdaptation: true,
            FalloutImageSpacePrograms.Read(RuntimeLiveContentSource.Current ??
                throw new InvalidOperationException("Source image factory has no actual owned program selection.")));
        var compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { effect } };
        return new(configuration.Schema, cinematic, tint, fade, 1, source.Source, [], effect, compositor);
    }
}

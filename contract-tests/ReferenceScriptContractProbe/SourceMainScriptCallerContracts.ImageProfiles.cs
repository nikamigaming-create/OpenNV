using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    private static void ImageShortProfileAndCold()
    {
        FalloutImageSpace Read(int extent)
        {
            var bytes = new byte[extent];
            for (var index = 0; index < bytes.Length / 4; index++)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), ImageBits(index + .125f));
            return FalloutImageSpaceReader.Decode(new("Authored.esm", 0x506), 11, bytes);
        }
        var source = Read(132); var profile = FalloutSourceImageSpaceProfile.Winning(source);
        var loadedMask = (1UL << 34) - 1; var finishedMask = (1UL << 37) - 1;
        Require(profile.DefinedWords == loadedMask && profile.SourceByteLength == 132 &&
            profile.ReadWord(13) == ImageBits(13.125f) && profile.ReadWord(14) == ImageBits(1) &&
            profile.ReadWord(15) == ImageBits(14.125f) && profile.ReadWord(33) == ImageBits(32.125f),
            "Short IMGS lost its authored words, inserted skin dimmer or actual unwritten trailing cells.");
        Reject(() => profile.ReadWord(34)); Reject(() => profile.ReadWord(37));
        Reject(() => (profile with { DefinedWords = FalloutSourceImageSpaceProfile.CompleteWords }).Validate());
        var invented = profile.CloneProfile(); invented.Words[34] = ImageBits(1); Reject(invented.Validate);
        var legacy = FalloutSourceImageSpaceProfile.Winning(Read(148));
        var current = FalloutSourceImageSpaceProfile.Winning(Read(152));
        Require(legacy.DefinedWords == FalloutSourceImageSpaceProfile.CompleteWords && legacy.ReadWord(37) == ImageBits(36.125f) &&
            current.DefinedWords == FalloutSourceImageSpaceProfile.CompleteWords && current.ReadWord(14) == ImageBits(14.125f) &&
            current.ReadWord(37) == ImageBits(37.125f), "Complete IMGS layouts acquired unwritten cells or shifted native words.");

        var accumulator = new FalloutSourceImageSpaceAccumulator(); accumulator.CopyProfile(profile); accumulator.Reset();
        accumulator.ApplyScalar(34, 0, 0, 1, batch: false); accumulator.NoActiveBatch(); accumulator.FinishBatch();
        Require(accumulator.Capture().DefinedWords == loadedMask, "Scalar arithmetic manufactured values from unwritten IMGS cells.");
        Reject(() => accumulator.Read(34)); accumulator.FinishColors();
        Require(accumulator.Capture().DefinedWords == finishedMask && accumulator.Read(34) == 0,
            "Final IMGS color writers did not define the originally unwritten fade cells.");

        using var warm = new StandaloneFixture(); var (images, native) = BindImageFixture(warm);
        images.PublishSourceImagePrimary(source);
        images.Apply(ImageProgram()); images.Apply(ImageProgram());
        using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult();
        var saved = images.CaptureSourceMainImages(); var runtime = warm.Owner.Capture();
        Require(saved.Last is { Returned: true } &&
            saved.Primary!.DefinedWords == loadedMask && saved.Fields.DefinedWords == finishedMask &&
            saved.Output!.Profile.DefinedWords == finishedMask && saved.Output.Profile.ReadWord(34) == 0 &&
            saved.Output.Profile.ReadWord(35) == 0 && saved.Output.Profile.ReadWord(36) == 0 && native.Profiles == 1,
            "Short IMGS batch fabricated trailing values or lost the actual final fade writers.");
        Reject(() => saved.Output!.Profile.ReadWord(37));
        Reject(() => FalloutImageSpaceState.ValidateSourceMainImages(saved with { Fields = saved.Fields with { DefinedWords = loadedMask } }));

        var serialized = JsonSerializer.Serialize(saved);
        var roundTrip = JsonSerializer.Deserialize<FalloutSourceImageSpaceMainSnapshot>(serialized)!;
        FalloutImageSpaceState.ValidateSourceMainImages(roundTrip);
        var missing = JsonNode.Parse(serialized)!.AsObject();
        missing["Primary"]!.AsObject().Remove("DefinedWords");
        var refused = false;
        try { JsonSerializer.Deserialize<FalloutSourceImageSpaceMainSnapshot>(missing.ToJsonString()); }
        catch (JsonException) { refused = true; }
        Require(refused, "Cold IMGS omitted its unwritten-cell boundary and silently became complete.");
        using var cold = new StandaloneFixture(runtime, warm.Fade.Capture());
        var (restored, newNative) = BindImageFixture(cold, roundTrip); restored.RebindSourceImagePrograms(_ => ImageProgram());
        Require(newNative.Profiles == 0, "Cold short IMGS replayed its historical native publication.");
        using (cold.Bind()) cold.Owner.ExecuteMainScriptCaller(1, 0).GetAwaiter().GetResult();
        var next = restored.CaptureSourceMainImages();
        Require(next.Primary!.DefinedWords == loadedMask && next.Output!.Profile.DefinedWords == finishedMask &&
            next.Output.Profile.SourceByteLength == 132 && next.Last!.Native == newNative.NativeIdentity && newNative.Profiles == 1,
            "Cold short IMGS reused old native fields or forgot the original source extent.");
        images.RetireSourceMainImageNative(native); restored.RetireSourceMainImageNative(newNative);
    }
}

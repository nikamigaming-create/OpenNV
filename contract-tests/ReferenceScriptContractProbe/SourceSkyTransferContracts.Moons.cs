using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceSkyTransferContracts
{
    private static void MoonContracts()
    {
        var fnv = FalloutMoonSource.Read(Declaration);
        var fo3 = FalloutMoonSource.Read(new(FalloutSourceMainFamily.Fallout3,
            FalloutSkyTransferDeclaration.StandaloneContractSha256));
        Require(fnv.Contract != fo3.Contract && fnv.Texture(FalloutMoonRole.Masser, 0) == "textures/sky/Masser_full.dds" &&
            fnv.Texture(FalloutMoonRole.Secunda, 7) == "textures/sky/Secunda_three_wax.dds" &&
            fnv.Texture(FalloutMoonRole.Secunda, 4) is null, "Moon constructors aliased selected engines, roles or the empty New phase.");
        Reject(() => (fnv with { Contract = fo3.Contract }).Require(Declaration));
        Reject(() => fnv.Texture((FalloutMoonRole)2, 0));
        Reject(() => fnv.Texture(FalloutMoonRole.Masser, 8));
        var settings = new FalloutMoonSettings(FalloutMoonRole.Masser, Bits(30), Bits(20), Bits(10), Bits(1), 0, 17);
        foreach (var source in new[] { fnv, fo3 })
        {
            Require(FalloutMoonMath.Phase(15, 2, 0) == 7 && FalloutMoonMath.Phase(16, 2, 7) == 0 &&
                FalloutMoonMath.Phase(100, 0, 5) == 5, "Moon phase changed unsigned division, wrap or zero-length cache preservation.");
            Require(FalloutMoonMath.Advance(source, settings, 90, 23, 1) == 210 &&
                FalloutMoonMath.Fade(source, settings, 25, false) == .5f &&
                FalloutMoonMath.Fade(source, settings, 155, false) == .5f &&
                FalloutMoonMath.Fade(source, settings, 90, false) == 1 &&
                FalloutMoonMath.Fade(source, settings, 200, false) == 0 &&
                FalloutMoonMath.Fade(source, settings, 15, true) == .5f,
                "Source Moon lost midnight elapsed storage or its independent primary/shadow fade intervals.");
        }
        Reject(() => FalloutMoonMath.SourceDays(-1));
        Reject(() => FalloutMoonMath.SourceDays(float.PositiveInfinity));
        Reject(() => (settings with { SpeedBits = Bits(float.NaN) }).Validate());
        var primary = FalloutMoonMath.Geometry(17, false); var shadow = FalloutMoonMath.Geometry(17, true);
        Require(primary.Vertices.Length == 4 && primary.Triangles.SequenceEqual(new[] { 0, 1, 2, 2, 1, 3 }) &&
            primary.Uv.Select(value => (value.X, value.Y)).SequenceEqual(new[] { (0f, 0f), (0f, 1f), (1f, 0f), (1f, 1f) }) &&
            primary.Vertices.Select(value => (value.X, value.Y, value.Z)).SequenceEqual(new[]
                { (-17f, 17f, 0f), (-17f, -17f, 0f), (17f, 17f, 0f), (17f, -17f, 0f) }) &&
            primary.Colors[0] == new System.Numerics.Vector4(1, 0, 0, 1) &&
            shadow.Colors.All(color => color == System.Numerics.Vector4.One),
            "Generated Moon geometry changed original vertices, triangles, UVs or independent colour arrays.");
        using var fixture = new SourceFixture();
        var process = Guid.NewGuid(); var sky = new FalloutSkyTransferState(Declaration, fixture.Records, new(), Stack, .5f);
        sky.BindProcess(process);
        var moons = new FalloutSkyMoonState(sky, fixture.Records, Stack, process);
        var saved = moons.Capture();
        Require(saved.Phase == 0 && saved.StoredDays == 0 && !saved.FactoryEntered && saved.Moons.Count == 0,
            "Moon owner invented factory success or frame advancement from construction.");
        Reject(() => FalloutSkyMoonState.Validate(saved with { Phase = 1 }, fnv, Stack, fixture.Records));
        Reject(() => FalloutSkyMoonState.Validate(saved with { FactoryEntered = true }, fnv, Stack, fixture.Records));
        var nextProcess = Guid.NewGuid(); var nextSky = new FalloutSkyTransferState(Declaration, fixture.Records, new(), Stack, .5f);
        nextSky.BindProcess(nextProcess);
        var cold = new FalloutSkyMoonState(nextSky, fixture.Records, Stack, nextProcess);
        cold.Restore(saved, saved.CapturedSky, saved.CapturedProcess);
        Require(cold.Capture().CapturedSky != saved.CapturedSky && cold.Capture().CapturedProcess != saved.CapturedProcess,
            "Cold Moon fields reused captured source/process ownership.");
        Reject(() => moons.Restore(saved, saved.CapturedSky, saved.CapturedProcess));
        cold.Retire(); moons.Retire(); sky.Retire(); nextSky.Retire();
        Console.WriteLine("OPENNV_SOURCE_MOON_CONTRACT_PASS selectedRoles=true phaseAndFloat32Fields=true generatedQuadInputs=true coldNewSourceEpoch=true foreignAndInventedFactoryRefused=true nativeExecution=unexecuted activeShadersAndWeather=unowned");
    }
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
}

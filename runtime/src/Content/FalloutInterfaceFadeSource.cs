using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal enum FalloutInterfaceFadeArithmetic { WideQuotientThenFloat32, Float32EachOperation }
internal enum FalloutInterfaceFadeRoot { Primary, Secondary }
internal sealed record FalloutInterfaceFadeCatalogRow(int Channel, FalloutInterfaceFadeRoot Root, string TexturePath);
internal sealed record FalloutInterfaceFadeDeclaration(string EngineSha256, FalloutInterfaceFadeArithmetic Arithmetic,
    IReadOnlyList<FalloutInterfaceFadeCatalogRow> Rows)
{
    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(EngineSha256) || !Enum.IsDefined(Arithmetic) || Rows is null || Rows.Count != 3 ||
            Rows.Where((row, ordinal) => row is null || row.Channel != ordinal || !Enum.IsDefined(row.Root) ||
                !row.TexturePath.StartsWith("textures/interface/faders/", StringComparison.Ordinal) ||
                !row.TexturePath.EndsWith(".dds", StringComparison.Ordinal) || row.TexturePath.Contains("..", StringComparison.Ordinal)).Any())
            throw new InvalidDataException("Selected interface fade catalog is invalid.");
        var arithmetic = EngineSha256 switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => FalloutInterfaceFadeArithmetic.WideQuotientThenFloat32,
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => FalloutInterfaceFadeArithmetic.Float32EachOperation,
            _ => throw new NotSupportedException("Selected image has no reviewed interface fade consumer."),
        };
        if (Arithmetic != arithmetic || Rows[0].Root != FalloutInterfaceFadeRoot.Primary ||
            Rows.Skip(1).Any(row => row.Root != FalloutInterfaceFadeRoot.Secondary) ||
            Rows[0].TexturePath != Rows[1].TexturePath || Rows[1].TexturePath == Rows[2].TexturePath)
            throw new InvalidDataException("Interface fade catalog disagrees with its selected consumer.");
    }
}
internal sealed record FalloutInterfaceFadeTexture(int Channel, string LogicalPath, string BytesSha256);
internal sealed record FalloutInterfaceFadeSource(FalloutInterfaceFadeDeclaration Declaration, string RuntimeSha256,
    IReadOnlyList<FalloutInterfaceFadeTexture> Textures, string ContractSha256)
{
    private const string Contract = "three-ordered-source-channels;source-root-and-dds-catalog;full-viewport-source-alpha;" +
        "positive-duration-from-transparent;immediate-opaque-shared-three-channel-update-hold;" +
        "ui-source-timer-divided-global-time-multiplier;selected-float32-stores;normal-release-opaque-minus-source-epsilon;" +
        "zero-opacity-retired-on-next-ui-update;replace-downward-only;native-prefix-and-current-cold-no-action-replay";
    internal string Identity => Hash(JsonSerializer.Serialize(this));
    internal static string CurrentContractSha256 => Hash(Contract);
    internal static FalloutInterfaceFadeSource Read(FalloutAdvancementRuntimeSource runtime)
    {
        runtime.RequireLivingRestSource();
        var declaration = FalloutExecutableStringTable.ReadInterfaceFade(runtime.OwnedSource.FalloutExecutablePath);
        if (declaration.EngineSha256 != runtime.Receipt.EngineSha256)
            throw new InvalidDataException("Interface fade selected a different executable lifetime.");
        var textures = declaration.Rows.Select(row =>
        {
            if (!runtime.OwnedSource.TryRead(row.TexturePath, null, out var bytes, out _))
                throw new FileNotFoundException("Selected interface fade texture is absent.", row.TexturePath);
            return new FalloutInterfaceFadeTexture(row.Channel, row.TexturePath,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }).ToArray();
        var result = new FalloutInterfaceFadeSource(declaration, runtime.Receipt.SourceSha256, textures, Hash(Contract));
        result.Validate(); return result;
    }
    internal void Validate()
    {
        if (Declaration is null || Textures is null) throw new InvalidDataException("Interface fade omits its selected declarations.");
        Declaration.Validate();
        if (!FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) || ContractSha256 != Hash(Contract) || Textures.Count != 3 ||
            Textures.Where((texture, ordinal) => texture is null || texture.Channel != ordinal || texture.LogicalPath != Declaration.Rows[ordinal].TexturePath ||
                !FalloutAdvancementRuntimeReceipt.Digest(texture.BytesSha256)).Any())
            throw new InvalidDataException("Interface fade has no complete selected source and texture identity.");
    }
    internal void RequireCurrent(FalloutInterfaceFadeSource current)
    {
        Validate(); current.Validate();
        if (Identity != current.Identity) throw new InvalidDataException("Saved interface fade changed selected source or texture bytes.");
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

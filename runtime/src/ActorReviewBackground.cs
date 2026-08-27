using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal sealed class ActorReviewBackground
{
    private readonly CellContentLoader.LoadedContent _content;

    private ActorReviewBackground(
        string scenePath,
        string sceneSha256,
        CellContentLoader.LoadedContent content,
        RetailExteriorEnvironment environment)
    {
        ScenePath = scenePath;
        SceneSha256 = sceneSha256;
        _content = content;
        Environment = environment;
    }

    internal string ScenePath { get; }
    internal string SceneSha256 { get; }
    internal string CellFormId => _content.FormId;
    internal string CellEditorId => _content.EditorId;
    internal int Assets => _content.Assets;
    internal int References => _content.References;
    internal int Textures => _content.Textures;
    internal int AuthoredDdsTextures => _content.AuthoredDdsTextures;
    internal int AuthoredDdsMipChainTextures => _content.AuthoredDdsMipChainTextures;
    internal int DecodedAuthoredBc1AlphaMipChainTextures =>
        _content.DecodedAuthoredBc1AlphaMipChainTextures;
    internal int RuntimeGeneratedMipTextures => _content.RuntimeGeneratedMipTextures;
    internal int MaterialBindings => _content.MaterialBindings;
    internal CellContentLoader.LoadedContent Content => _content;
    internal RetailExteriorEnvironment Environment { get; }

    internal static ActorReviewBackground Load(
        string scenePath,
        Node3D parent,
        RuntimeConfiguration configuration)
    {
        var resolved = VerifiedGltfLoader.ResolvePath(scenePath);
        var session = new GameplaySession
        {
            ProcessMode = Node.ProcessModeEnum.Disabled,
        };
        var content = CellContentLoader.Load(
            resolved,
            parent,
            session,
            configuration,
            false,
            null,
            null,
            false,
            false,
            1u);
        using var document = JsonDocument.Parse(File.ReadAllText(resolved));
        var environment = RetailExteriorEnvironment.Load(
            document.RootElement,
            configuration.FalloutEnvironment.ImageSpace);
        using var stream = File.OpenRead(resolved);
        return new ActorReviewBackground(
            resolved,
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            content,
            environment);
    }

    internal void AlignToActor(Vector3 actorRootGameUnits)
    {
        var actorInCellUnits = GamebryoCoordinate.ConvertVector(
            actorRootGameUnits - _content.OriginGameUnits);
        _content.Root.GlobalPosition = -(_content.Root.GlobalBasis * actorInCellUnits);
    }
}

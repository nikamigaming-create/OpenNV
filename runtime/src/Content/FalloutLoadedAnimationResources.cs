using System.Security.Cryptography;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLoadedAnimationResourceReceipt(string Source, Guid Factory, string Path,
    string Sha256, int InputOrder, int RootBlock, string Name, ushort Group, string Identity);
internal sealed record FalloutLoadedAnimationResource(FalloutLoadedAnimationResourceReceipt Receipt,
    FalloutNifFile File, FalloutNifControllerSequence Sequence)
{
    internal FalloutActorMoverVector Motion { get; set; } = FalloutActorMoverVector.Default;
}
internal sealed record FalloutLoadedAnimationResourceSnapshot(string Source, Guid Factory, Guid RecordsFactory,
    string RecordsSource, Guid ModelFactory, string SkeletonPath, string SkeletonSha256, bool Female,
    IReadOnlyList<FalloutLoadedAnimationResourceReceipt> Resources, string OrderLane, bool? PlayerToddler = null);

// Original files remain the launch input. This in-process catalog owns the
// exact returned C# enumeration and each decoded sequence/root association.
internal sealed class FalloutLoadedAnimationResources
{
    internal const string OrderLane = "actual-owned-index-order-and-source-prepend;canonical-retail-file-search-order-unverified";
    private readonly FalloutPluginStack _records;
    private readonly RuntimeLiveContentSource _content;
    private readonly Action _requireActualModel;
    private readonly List<FalloutLoadedAnimationResource> _resources = [];
    private readonly string[] _paths;
    private readonly bool? _toddler;
    private bool _loaded, _enumerating;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutLoadedAnimationSource Source { get; }
    internal FalloutActorSourceModel Model { get; }
    internal IEnumerable<FalloutLoadedAnimationResource> Resources => LoadAll();
    internal FalloutLoadedAnimationResources(FalloutLoadedAnimationSource source, FalloutPluginStack records,
        RuntimeLiveContentSource content, FalloutActorSourceModel model, Action requireActualModel,
        FalloutLoadedAnimationRole role, FalloutScriptSession? session = null,
        Action<FalloutScriptSession>? requireActualSession = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(requireActualModel);
        Source = source; _records = records; _content = content; Model = model; _requireActualModel = requireActualModel; RequireCurrent();
        var skeleton = Canonical(model.Appearance.SkeletonPath); var split = skeleton.LastIndexOf('/');
        if (split < 0 || !skeleton.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Animation list lost its actual model path directory.");
        var directory = skeleton[..split];
        var index = content.ResourcePathsUnder(directory).Select(Canonical).ToArray(); RequireCurrent();
        var stages = new List<string> { directory, directory + "/locomotion", directory + "/locomotion/hurt" };
        if (role == FalloutLoadedAnimationRole.FirstPerson)
        {
            if (session is null || requireActualSession is null) throw new InvalidOperationException("First animation list has no actual shared ScriptSession toddler field.");
            requireActualSession(session); _toddler = session.PlayerToddler;
            if (_toddler == true) stages.Add(directory + "/locomotion/toddler/idleanims");
            stages.Add(directory + "/locomotion/" + (model.Appearance.Female ? "female" : "male") + "/idleanims");
            requireActualSession(session);
        }
        var ordered = new Stack<string>();
        foreach (var stage in stages)
            foreach (var path in index.Where(path => path.EndsWith(".kf", StringComparison.OrdinalIgnoreCase) &&
                path[..path.LastIndexOf('/')].Equals(stage, StringComparison.OrdinalIgnoreCase)))
                ordered.Push(path);
        _paths = ordered.ToArray(); RequireCurrent();
    }
    private IEnumerable<FalloutLoadedAnimationResource> LoadAll()
    {
        RequireCurrent();
        if (_loaded) { foreach (var resource in _resources) yield return resource; yield break; }
        if (_enumerating || _resources.Count != 0) throw new InvalidOperationException("Animation resource import repeated an entered or failed original list.");
        _enumerating = true; var inputOrder = 0;
        try
        {
            foreach (var path in _paths)
            {
                RequireCurrent();
                if (!_content.TryRead(path, null, out var bytes, out var identity))
                    throw new FileNotFoundException("Returned animation-list entry has no current winning file.", path);
                var file = FalloutNifFile.Read(bytes); RequireCurrent();
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                foreach (var root in file.Roots)
                {
                    if (file.ReadObject(root) is not FalloutNifControllerSequence sequence)
                        throw new NotSupportedException("source-animation-KF-non-sequence-root-factory-unowned:" + path);
                    var group = TryGroup(Source, path, sequence.Name);
                    if (group is null) continue;
                    var resource = new FalloutLoadedAnimationResource(new(Source.Identity, Factory, path, hash, inputOrder++, root, sequence.Name,
                        group.Value, identity), file, sequence);
                    _resources.Add(resource); yield return resource;
                }
            }
            _loaded = true; RequireCurrent();
        }
        finally { _enumerating = false; }
    }
    internal void RequireCurrent()
    {
        _requireActualModel(); _records.RequireLiveLoadReceipt(_records.LoadReceipt);
        if (!ReferenceEquals(_records.OwnedSource, _content) || _content.ResourceReadsClosed)
            throw new InvalidOperationException("Animation resources lost the actual retained records/content/model lifetime.");
    }
    internal FalloutLoadedAnimationResourceSnapshot Capture()
    {
        RequireCurrent(); var records = _records.LoadReceipt;
        return new(Source.Identity, Factory, records.Factory, records.SourceHash, Model.Factory,
            Canonical(Model.Appearance.SkeletonPath), Model.Skeleton.Source.Sha256, Model.Appearance.Female,
            _resources.Select(row => row.Receipt).ToArray(), OrderLane, _toddler);
    }
    internal static ushort? TryGroup(FalloutLoadedAnimationSource source, string path, string sequenceName)
    {
        var group = source.Groups.SingleOrDefault(row => row.Name.Equals(sequenceName, StringComparison.OrdinalIgnoreCase));
        // Original unknown group names produce the invalid low-byte sentinel;
        // registration refuses that resource and continues the returned list.
        if (group is null) return null;
        var file = path[(path.LastIndexOf('/') + 1)..]; var armor = file.StartsWith("pa", StringComparison.OrdinalIgnoreCase);
        if (armor) file = file[2..];
        var movement = Prefix(source.MovementPrefixes); var weapon = Prefix(source.WeaponPrefixes);
        return checked((ushort)((armor ? 0x8000 : 0) | movement << 12 | weapon << 8 | group.Index));
        int Prefix(IReadOnlyList<string> prefixes)
        {
            for (var index = 1; index < prefixes.Count; index++)
                if (file.StartsWith(prefixes[index], StringComparison.OrdinalIgnoreCase))
                { file = file[prefixes[index].Length..]; return index; }
            return 0;
        }
    }
    private static string Canonical(string path) => FalloutBsaArchive.CanonicalPath(path).Replace('\\', '/').ToLowerInvariant();
}

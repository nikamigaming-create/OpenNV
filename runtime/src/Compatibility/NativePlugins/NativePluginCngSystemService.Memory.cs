using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginCngSystemService
{
    private bool _sdkPrepareEntered, _sdkPrepared, _memoryOnlyRetired;
    internal NativePluginExecutionDomain MemoryDomain => _domain ?? throw new InvalidOperationException("Memory service has no actual process generation.");
    internal bool SdkPrepareEntered => _sdkPrepareEntered;
    internal bool SdkPrepared => _sdkPrepared;
    internal bool MemoryOnlyRetired => _memoryOnlyRetired;
    private void PrepareSdkForActualConsumer(byte[] request)
    {
        var step = request.Length >= 4 ? (NativePluginCngServiceStep)BinaryPrimitives.ReadUInt32LittleEndian(request) :
            throw new InvalidDataException("Actual CNG consumer omitted its operation.");
        if (_sdkPrepared) return;
        if (_sdkPrepareEntered || _memoryOnlyRetired || step is not (NativePluginCngServiceStep.Begin or NativePluginCngServiceStep.SharedBind))
            throw new InvalidDataException("SDK admission requires the first genuine CNG construction/argument consumer and cannot replay.");
        _sources.RequireCurrent(); _sdkPrepareEntered = true;
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
        {
            writer.Write((uint)NativePluginCngServiceStep.Prepare);
            Text(writer, _sources.Provider.Path); Text(writer, _sources.Provider.Sha256);
            Text(writer, _sources.Primitives.Path); Text(writer, _sources.Primitives.Sha256);
        }
        using var output = Reader(MemoryDomain.CngSystemExchange(payload.ToArray()));
        if (output.ReadUInt32() != 1 || output.ReadUInt32() != 1 ||
            ReadText(output) != _sources.Provider.Path || ReadText(output) != _sources.Provider.Sha256 ||
            ReadText(output) != _sources.Primitives.Path || ReadText(output) != _sources.Primitives.Sha256)
            throw new InvalidDataException("Actual CNG mapped providers/signature admission lost selected source identities.");
        Finish(output); _sources.RequireCurrent(); _sdkPrepared = true;
    }
    internal void RetireMemoryOnly()
    {
        if (_memoryOnlyRetired) return;
        if (_sdkPrepareEntered || _sdkPrepared || _providerRetired || _sourcesRetired)
            throw new InvalidOperationException("Memory-only retirement cannot substitute for entered SDK/provider destruction.");
        _sources.RequireCurrent();
        MemoryDomain.Dispose();
        if (!MemoryDomain.ChildExited || !MemoryDomain.NaturallyRetired)
            throw new InvalidDataException("Windows memory-only service did not retire its exact process normally.");
        _sources.RequireCurrent(); _sources.Dispose(); _sourcesRetired = true; _memoryOnlyRetired = true;
        // No SDK Prepare/retired receipt is emitted for providers never loaded.
    }
}

internal sealed partial class NativePluginExecutionDomain
{
    private NativePluginCngSystemService RequireCngMemoryService(ulong parent, NativePluginPrivateIo owner, ulong module)
    {
        if (parent == 0 || module == 0 || _cngOriginalUnloading)
            throw new InvalidDataException("New Windows memory service lacks its entered original source caller.");
        owner.RequireCryptoSourceCurrent(owner.CryptoProvider.Path, owner.CryptoProvider.Sha256);
        if (_cngService is null)
        {
            var image = _cngServiceImage ?? (_cngServiceBuild is { } build ? NativePluginCngSystemBuild.ReadSibling(build) :
                throw new NotSupportedException("Shared original caller lacks the retained first-party memory/service build."));
            try
            {
                _cngService = new(image, owner.CryptoProvider, Generation, module, NativeThread);
                _cngService.BindOriginalSharedOwner(this);
            }
            catch (NativePluginCngServiceFaultException error) { _cngService = error.Owner; throw; }
        }
        if (_cngService.ChildExited || _cngService.MemoryOnlyRetired)
            throw new InvalidOperationException("Actual memory/service generation has already retired.");
        return _cngService;
    }
    private void RetireUnenteredCngMemoryService()
    {
        if (_cngService is { SdkPrepareEntered: false, MemoryOnlyRetired: false })
        {
            RequireSharedPlacementsSourceReleased();
            _cngService.RetireMemoryOnly();
        }
    }
}

using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private NativePluginCngServiceImage? _cngServiceImage;
    private string? _cngServiceBuild;
    private NativePluginCngSystemService? _cngService;
    private bool CngSystemOwnersRetired => _cngService is null || _cngService.ResourcesRetired;
    internal IReadOnlyList<NativePluginCngServiceReceipt> CngSystemReceipts =>
        _cngService?.Receipts ?? Array.Empty<NativePluginCngServiceReceipt>();
    internal IReadOnlyList<NativePluginCngEmergencyReceipt> CngEmergencyReceipts =>
        _cngService?.EmergencyReceipts ?? Array.Empty<NativePluginCngEmergencyReceipt>();
    internal IReadOnlyList<NativePluginCngLocalPublication> CngLocalPublications =>
        _cngService?.LocalPublications ?? Array.Empty<NativePluginCngLocalPublication>();

    internal void BindCngSystemServiceImage(NativePluginCngServiceImage image)
    {
        VerifyOwner(); ArgumentNullException.ThrowIfNull(image);
        if (_cngServiceImage is not null || _cngServiceBuild is not null || _cngService is not null || NativeModuleCount != 0)
            throw new InvalidOperationException("CNG service build identity must bind once before original module entry.");
        // Root supplies the actual first-party build-manifest identity. No
        // original callback may nominate another executable or provider path.
        _cngServiceImage = image;
    }
    internal void BindCngSystemServiceBuild(string actualCompanion)
    {
        VerifyOwner();
        if (_cngServiceImage is not null || _cngServiceBuild is not null || _cngService is not null || NativeModuleCount != 0)
            throw new InvalidOperationException("CNG service build must bind once before original entry.");
        _cngServiceBuild = Path.GetFullPath(actualCompanion);
        // Manifest/provider loading is deferred until a genuine CNG invocation.
    }
    private byte[] DispatchCngSystemService(ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        if (parent == 0)
            throw new InvalidDataException("CNG service request has no real original-module call context.");
        var originalModule = CurrentOriginalIoModule(parent);
        var remaining = checked((int)(reader.BaseStream.Length - reader.BaseStream.Position));
        var request = reader.ReadBytes(remaining); Finish(reader);
        owner.RequireCryptoSourceCurrent(owner.CryptoProvider.Path, owner.CryptoProvider.Sha256);
        if (_cngService is null)
        {
            var firstStep = request.Length >= 4 ? (NativePluginCngServiceStep)BinaryPrimitives.ReadUInt32LittleEndian(request) : NativePluginCngServiceStep.Prepare;
            if (_cngOriginalUnloading || firstStep is not (NativePluginCngServiceStep.Begin or NativePluginCngServiceStep.SharedBind))
                throw new InvalidDataException("A CNG service must be requested by its first actual SDK invocation.");
            var image = _cngServiceImage ?? (_cngServiceBuild is { } build ? NativePluginCngSystemBuild.ReadSibling(build) :
                throw new NotSupportedException("The selected original CNG caller lacks its retained first-party system-service build."));
            try
            {
                _cngService = new(image, owner.CryptoProvider, Generation, originalModule, NativeThread);
                _cngService.BindOriginalSharedOwner(this);
            }
            catch (NativePluginCngServiceFaultException error) { _cngService = error.Owner; throw; }
        }
        var reply = _cngService.Forward(parent, request, _cngOriginalUnloading);
        // Service generation accompanies every reply. The original child keeps
        // its own token pointers; no remote Windows address crosses this wire.
        return Payload(writer => { writer.Write(_cngService.Generation); writer.Write(reply); });
    }
    private void RequireCngSystemServiceRetired()
    {
        if (_cngService is not null && !_cngService.OrderlyRetired)
            throw new InvalidDataException("Original CNG caller still owns a live or failed system-service lifetime.");
    }
    private void ReleaseCngSystemServiceAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("CNG service ownership must survive exact original child closure.");
        _cngService?.ReleaseAfterOriginalChildExit(ChildExited);
        // Keep receipts and failed owners observable across repeated retirement.
    }
}

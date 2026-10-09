namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint CryptoCallback = 21;
    private readonly Dictionary<ulong, (uint Handle, string Name, string Implementation, bool Present, uint Flags)> _cryptoAlgorithms = [];
    private readonly Dictionary<ulong, (uint Handle, ulong Algorithm, uint Object, uint Bytes, uint Flags)> _cryptoHashes = [];
    private readonly HashSet<ulong> _cryptoLifetimes = [];
    private readonly List<NativePluginCryptoReceipt> _cryptoReceipts = [];
    private uint _cryptoProviderModule;
    private bool _cryptoProviderRegistered, _cryptoProviderRetired;
    internal IReadOnlyList<NativePluginCryptoReceipt> NvseCryptoReceipts => _cryptoReceipts.AsReadOnly();
    private static void WriteCryptoProvider(BinaryWriter writer, NativePluginPrivateIo owner)
    {
        var source = owner.CryptoProvider; WriteText(writer, source.Path); WriteText(writer, source.Sha256); WriteText(writer, source.SourceOwner);
    }
    private byte[] DispatchPrivateCrypto(ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        var category = reader.ReadUInt32();
        if (category == 4) return DispatchCngSystemService(parent, owner, reader);
        if (category == 1)
        {
            var module = reader.ReadUInt32(); var path = ReadText(reader); var sha = ReadText(reader); Finish(reader);
            owner.RequireCryptoSourceCurrent(path, sha);
            if (module == 0 || _cryptoProviderRegistered || _cryptoProviderRetired)
                throw new InvalidDataException("The actual CNG module repeats or lacks its provider lifetime.");
            _cryptoProviderModule = module; _cryptoProviderRegistered = true;
            return Payload(writer => writer.Write(1U));
        }
        if (category == 3)
        {
            var module = reader.ReadUInt32(); var released = reader.ReadUInt32(); var error = reader.ReadUInt32(); Finish(reader);
            if (!_cryptoProviderRegistered || _cryptoProviderRetired || module != _cryptoProviderModule ||
                released != 1 || error != 0 || _cryptoAlgorithms.Count != 0 || _cryptoHashes.Count != 0)
                throw new InvalidDataException("CNG provider reference did not retire after all genuine hash/algorithm handles.");
            owner.RequireCryptoSourceCurrent(owner.CryptoProvider.Path, owner.CryptoProvider.Sha256);
            _cryptoProviderRetired = true; return Payload(writer => writer.Write(1U));
        }
        if (category != 2 || !_cryptoProviderRegistered || _cryptoProviderRetired)
            throw new InvalidDataException("CNG operation has no current actual export provider.");
        var operation = (NativePluginCryptoOperation)reader.ReadUInt32(); var id = reader.ReadUInt64(); var algorithm = reader.ReadUInt64();
        var handle = reader.ReadUInt32(); var flags = reader.ReadUInt32(); var requested = reader.ReadUInt32(); var resultBytes = reader.ReadUInt32();
        var resultAvailable = reader.ReadUInt32(); var status = reader.ReadInt32(); var name = ReadText(reader); var implementation = ReadText(reader); var present = reader.ReadUInt32();
        var objectAddress = reader.ReadUInt32(); var objectBytes = reader.ReadUInt32(); Finish(reader);
        if (!Enum.IsDefined(operation) || id == 0 || present > 1 || present == 0 && implementation.Length != 0 ||
            resultAvailable > 1 || resultAvailable == 0 && resultBytes != 0 ||
            resultAvailable != 0 && operation is not (NativePluginCryptoOperation.GetProperty or NativePluginCryptoOperation.FinishHash))
            throw new InvalidDataException("CNG operation lost its complete native signature/lifetime identity.");
        var succeeded = status >= 0; var retired = false;
        if (operation == NativePluginCryptoOperation.OpenAlgorithm)
        {
            if (!_cryptoLifetimes.Add(id) || algorithm != 0 || objectAddress != 0 || objectBytes != 0 || requested != 0 ||
                resultBytes != 0 || (handle != 0) != succeeded)
                throw new InvalidDataException("CNG algorithm construction has a repeated or fabricated native result.");
            if (succeeded)
            {
                if (_cryptoAlgorithms.Values.Any(value => value.Handle == handle) || _cryptoHashes.Values.Any(value => value.Handle == handle))
                    throw new InvalidDataException("CNG algorithm aliases a live hash/algorithm handle.");
                _cryptoAlgorithms.Add(id, (handle, name, implementation, present != 0, flags));
            }
        }
        else if (operation == NativePluginCryptoOperation.CreateHash)
        {
            if (!_cryptoLifetimes.Add(id) || !_cryptoAlgorithms.ContainsKey(algorithm) ||
                (handle != 0) != succeeded || name.Length != 0 || implementation.Length != 0 || present != 0 || resultBytes != 0 ||
                succeeded && objectBytes != 0 && objectAddress == 0)
                throw new InvalidDataException("CNG hash construction has no genuine current algorithm/object-buffer lifetime.");
            if (succeeded)
            {
                if (_cryptoAlgorithms.Values.Any(value => value.Handle == handle) || _cryptoHashes.Values.Any(value => value.Handle == handle))
                    throw new InvalidDataException("CNG hash aliases another live handle.");
                _cryptoHashes.Add(id, (handle, algorithm, objectAddress, objectBytes, flags));
            }
        }
        else
        {
            var isHash = _cryptoHashes.TryGetValue(id, out var hash);
            var isAlgorithm = _cryptoAlgorithms.TryGetValue(id, out var alg);
            if (isHash == isAlgorithm || handle != (isHash ? hash.Handle : alg.Handle) ||
                algorithm != (isHash ? hash.Algorithm : 0) || objectAddress != (isHash ? hash.Object : 0) || objectBytes != (isHash ? hash.Bytes : 0) ||
                implementation.Length != 0 || present != 0 || operation != NativePluginCryptoOperation.GetProperty && name.Length != 0 ||
                !isHash && operation is not (NativePluginCryptoOperation.GetProperty or NativePluginCryptoOperation.CloseAlgorithm) ||
                isHash && operation == NativePluginCryptoOperation.CloseAlgorithm)
                throw new InvalidDataException("CNG operation targets a foreign, retired or wrong-kind handle.");
            // The Windows provider, including reusable/HMAC behavior, decides
            // statuses. A successful Finish does not pretend to destroy a hash.
            if (succeeded && operation == NativePluginCryptoOperation.DestroyHash) { _cryptoHashes.Remove(id); retired = true; }
            if (succeeded && operation == NativePluginCryptoOperation.CloseAlgorithm) { _cryptoAlgorithms.Remove(id); retired = true; }
        }
        _cryptoReceipts.Add(new(checked((ulong)_cryptoReceipts.Count + 1), Generation, parent, operation, id, algorithm,
            handle, flags, requested, resultBytes, resultAvailable != 0, status, name, implementation, present != 0, objectAddress, objectBytes,
            retired, owner.CryptoProvider.SourceOwner));
        return Payload(writer => writer.Write(1U));
    }
    private void RequirePrivateCryptoRetired()
    {
        RequireCngSystemServiceRetired();
        if (_cryptoAlgorithms.Count != 0 || _cryptoHashes.Count != 0 || _cryptoProviderRegistered && !_cryptoProviderRetired)
            throw new InvalidDataException("Original CNG retirement retains real algorithm/hash/provider references.");
    }
    private void ClearPrivateCrypto()
    {
        if (!ChildExited) throw new InvalidOperationException("CNG capability cleanup requires verified exact child closure.");
        ReleaseCngSystemServiceAfterChildExit();
        _cryptoAlgorithms.Clear(); _cryptoHashes.Clear(); _cryptoLifetimes.Clear();
        _cryptoProviderModule = 0; _cryptoProviderRegistered = false; _cryptoProviderRetired = false;
        // Operation receipts survive terminal cleanup; that is not a clean
        // BCryptDestroyHash/CloseAlgorithmProvider/FreeLibrary assertion.
    }
}

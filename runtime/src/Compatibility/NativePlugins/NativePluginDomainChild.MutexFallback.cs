using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginDomainChild
{
    private sealed class FallbackReference(string role, NativePluginKernelHandle handle)
    {
        internal string Role { get; } = role;
        internal NativePluginKernelHandle Handle { get; } = handle;
        internal ulong Identity { get; } = unchecked((ulong)(nuint)handle.DangerousGetHandle());
        internal bool CloseEntered { get; set; }
        internal bool? CloseResult { get; set; }
        internal uint? CloseError { get; set; }
        internal NativePluginMutexFallbackClose Capture() => new(Role, Identity, CloseEntered, CloseResult, CloseError);
    }

    // Observation only: four real directory opens, no new directory, mutex,
    // token grant, access-mask downgrade or transfer to the original process.
    // The source Windows fallback has a denied full root, traversal root, then
    // its existing Restricted child. Both Restricted rights are retained even
    // when the source condition would not select that fallback.
    internal NativePluginMutexFallbackObservation ProbeMutexDirectoryFallback(ulong generation,
        ulong module, ulong call, uint nativeThread, string restrictingSid, string rootName,
        NativePluginObjectDirectoryHandle root)
    {
        RequireMutexProcess();
        if (generation == 0 || module == 0 || call == 0 || nativeThread == 0 || root.IsClosed || root.IsInvalid)
            throw new InvalidDataException("Mutex fallback observation lacks its actual retained caller/root.");
        if (NativePluginObjectDirectory.Name(root.DangerousGetHandle()) != rootName)
            throw new InvalidDataException("Mutex fallback observation received another actual directory.");
        _ = RequireMutexNamespaceToken(restrictingSid);
        var references = new List<FallbackReference>();
        var tokens = new List<NativePluginMutexFallbackToken>();
        var descriptors = new List<NativePluginMutexFallbackDescriptor>();
        var opens = new List<NativePluginMutexFallbackOpen>();
        var failures = new List<Exception>();
        uint? threadOpenError = null, revertError = null;
        bool? threadPresent = null, revertResult = null;
        var impersonated = false; var revertEntered = false; var started = false; var exited = false;
        bool? eligible = null, opened15 = null, opened6 = null, unchanged = null;
        FallbackReference? restrictedDescriptor = null;
        FallbackReference Retain(string role, NativePluginKernelHandle handle)
        {
            var retained = new FallbackReference(role, handle); references.Add(retained); return retained;
        }
        try
        {
            // These metadata references request query/read-control only under
            // the parent's existing authority; every returned status is kept.
            descriptors.Add(FallbackDescriptor("root-before", rootName, null, root.DangerousGetHandle()));
            var read = NativePluginObjectDirectory.OpenRaw(root.DangerousGetHandle(), "Restricted", 0x20001);
            if (read.Handle is not (0 or -1)) restrictedDescriptor = Retain("restricted-descriptor",
                new NativePluginKernelHandle(read.Handle));
            descriptors.Add(FallbackDescriptor("restricted-before", rootName + "\\Restricted",
                unchecked((uint)read.Status), read.Handle));

            var caller = FallbackOpenThread(0x40, false, nativeThread);
            if (caller.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Mutex caller thread token is unreadable.");
            _ = Retain("caller-thread", caller);
            var actualPid = FallbackThreadProcess(caller);
            if (actualPid == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (actualPid != checked((uint)Id)) throw new InvalidDataException("Mutex caller thread belongs to another actual process.");
            if (!OpenNamespaceToken(CngCreationHandle, 0xa, out var primary)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var primaryRef = Retain("primary-token", primary);
            var primaryBefore = FallbackToken("primary-before", primary); tokens.Add(primaryBefore);
            if (!primaryBefore.Restricted || primaryBefore.Restrictions.Length != 1 ||
                primaryBefore.Restrictions[0].Sid != restrictingSid)
                throw new InvalidDataException("Actual source-directory probe lost its selected module restriction.");
            NativePluginKernelHandle effective;
            if (FallbackOpenThreadToken(caller, 0xa, true, out var supplied))
            {
                threadPresent = true; effective = supplied; _ = Retain("effective-thread-token", supplied);
            }
            else
            {
                threadOpenError = unchecked((uint)Marshal.GetLastWin32Error()); threadPresent = false;
                if (threadOpenError != 1008) throw new Win32Exception(unchecked((int)threadOpenError), "Actual caller token observation failed.");
                supplied.Dispose(); effective = primaryRef.Handle;
            }
            tokens.Add(FallbackToken("effective-before", effective));
            // The installed Windows root getter temporarily removes thread
            // impersonation and selects from the process's primary token.
            // Retain the distinct effective caller token above; do not treat
            // the two as interchangeable at the subsequent NtCreateMutant.
            if (!NamespaceDuplicateToken(primaryRef.Handle, 2, out var duplicated)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var duplicateRef = Retain("probe-token", duplicated);
            tokens.Add(FallbackToken("probe-before", duplicated));
            var worker = new Thread(() =>
            {
                try
                {
                    if (!NamespaceImpersonate(duplicateRef.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    impersonated = true;
                    var full = Open("root15", 0, rootName, rootName, 15);
                    var traversal = Open("root2", 0, rootName, rootName, 2);
                    eligible = full.Status < 0 && traversal.Status >= 0 && traversal.Handle is not (0 or -1);
                    if (traversal.Status >= 0 && traversal.Handle is not (0 or -1))
                    {
                        // Source-relative opens use the real restricted-token
                        // traversal reference, not the broader parent handle.
                        var first = Open("restricted15", traversal.Handle, rootName, "Restricted", 15);
                        var second = Open("restricted6", traversal.Handle, rootName, "Restricted", 6);
                        opened15 = first.Status >= 0 && first.Handle is not (0 or -1);
                        opened6 = second.Status >= 0 && second.Handle is not (0 or -1);
                    }
                }
                catch (Exception failure) { failures.Add(failure); }
                finally
                {
                    if (impersonated)
                    {
                        revertEntered = true; revertResult = NamespaceRevert();
                        revertError = unchecked((uint)Marshal.GetLastWin32Error());
                        if (revertResult != true) failures.Add(new Win32Exception(unchecked((int)revertError), "Actual fallback worker did not revert its token."));
                    }
                }
            })
            { IsBackground = true, Name = "OpenNV restricted named-object fallback observation" };
            worker.Start(); started = true; worker.Join(); exited = !worker.IsAlive;
            tokens.Add(FallbackToken("probe-after", duplicateRef.Handle));
            tokens.Add(FallbackToken("effective-after", effective));
            tokens.Add(FallbackToken("primary-after", primaryRef.Handle));
            var after = FallbackDescriptor("root-after", rootName, null, root.DangerousGetHandle()); descriptors.Add(after);
            if (restrictedDescriptor is not null)
                descriptors.Add(FallbackDescriptor("restricted-after", rootName + "\\Restricted",
                    unchecked((uint)read.Status), restrictedDescriptor.Handle.DangerousGetHandle()));
            var rootBefore = descriptors.Single(value => value.Role == "root-before");
            var childBefore = descriptors.Single(value => value.Role == "restricted-before");
            var childAfter = descriptors.SingleOrDefault(value => value.Role == "restricted-after");
            if (rootBefore.Sha256 is not null && after.Sha256 is not null && childBefore.Sha256 is not null && childAfter?.Sha256 is not null)
                unchanged = rootBefore.Sha256 == after.Sha256 && childBefore.Sha256 == childAfter.Sha256;
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            // Attempt every acquired reference independently, including token
            // and metadata refs. A failed close stays strongly retained by the
            // existing child-closure-dependent verification owner.
            for (var at = references.Count - 1; at >= 0; --at)
            {
                var reference = references[at]; reference.CloseEntered = true;
                reference.CloseResult = NativePluginIoSecurity.CloseHandle(reference.Handle.DangerousGetHandle());
                reference.CloseError = unchecked((uint)Marshal.GetLastWin32Error());
                if (reference.CloseResult == true) { reference.Handle.SetHandleAsInvalid(); reference.Handle.Dispose(); }
                else
                {
                    _failedMutexVerificationRefs.Add(reference.Handle);
                    failures.Add(new Win32Exception(unchecked((int)reference.CloseError), "Independent fallback reference closure failed: " + reference.Role));
                }
            }
        }
        return new(generation, module, call, nativeThread, checked((uint)Id), rootName, restrictingSid,
            threadOpenError, threadPresent, tokens.ToImmutableArray(), descriptors.ToImmutableArray(),
            opens.ToImmutableArray(), references.Select(value => value.Capture()).ToImmutableArray(),
            impersonated, revertEntered, revertResult, revertError, started, exited, eligible, opened15, opened6, unchanged,
            failures.Count == 0 ? null : string.Join(" | ", failures.Select(value => value.ToString())));

        (int Status, nint Handle) Open(string stage, nint directory, string original, string name, uint access)
        {
            var result = NativePluginObjectDirectory.OpenRaw(directory, name, access);
            var error = result.Status < 0 ? NativePluginObjectDirectory.DosError(unchecked((uint)result.Status)) : (uint?)null;
            FallbackReference? strong = null;
            if (result.Handle is not (0 or -1)) strong = Retain(stage, new NativePluginKernelHandle(result.Handle));
            string? canonical = null, metadataFailure = null;
            if (strong is not null)
                try { canonical = NativePluginObjectDirectory.Name(strong.Handle.DangerousGetHandle()); }
                catch (Exception failure) { metadataFailure = failure.ToString(); }
            opens.Add(new(opens.Count, stage, original, name, access, unchecked((uint)result.Status), error,
                unchecked((ulong)(nuint)result.Handle), canonical, metadataFailure));
            if (result.Status >= 0 && strong is null || result.Status < 0 && strong is not null)
                throw new InvalidDataException("Directory status and actual acquired reference disagree.");
            return result;
        }
    }

    private static NativePluginMutexFallbackToken FallbackToken(string role, NativePluginKernelHandle token)
    {
        var data = FallbackTokenInformation(token, 10, Marshal.SizeOf<FallbackTokenStatistics>());
        FallbackTokenStatistics statistics;
        try { statistics = Marshal.PtrToStructure<FallbackTokenStatistics>(data.Memory); }
        finally { Marshal.FreeHGlobal(data.Memory); }
        var user = FallbackTokenInformation(token, 1, Marshal.SizeOf<NamespaceSid>());
        string userSid;
        try { userSid = FallbackSidText(Marshal.ReadIntPtr(user.Memory), user); }
        finally { Marshal.FreeHGlobal(user.Memory); }
        var acl = FallbackTokenInformation(token, 6, nint.Size);
        string aclHash;
        try
        {
            var pointer = Marshal.ReadIntPtr(acl.Memory);
            if (pointer == 0) throw new InvalidDataException("Actual fallback token has no explicit default DACL.");
            FallbackRequireExtent(acl, pointer, 8);
            var size = unchecked((ushort)Marshal.ReadInt16(pointer, 2));
            FallbackRequireExtent(acl, pointer, size);
            if (size < 8 || !FallbackValidAcl(pointer)) throw new InvalidDataException("Actual fallback token DACL has no valid complete extent.");
            var bytes = new byte[size]; Marshal.Copy(pointer, bytes, 0, bytes.Length); aclHash = Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { Marshal.FreeHGlobal(acl.Memory); }
        return new(role, statistics.TokenId, statistics.AuthenticationId, statistics.ModifiedId,
            statistics.Type, statistics.Type == 2 ? statistics.Impersonation : null,
            NamespaceNumber(token, 12), NamespaceTokenRestricted(token), userSid,
            FallbackSids(token, 2), FallbackSids(token, 11), aclHash);
    }
    private static ImmutableArray<NativePluginMutexFallbackSid> FallbackSids(NativePluginKernelHandle token, int kind)
    {
        var first = Marshal.OffsetOf<NamespaceTokenGroups>(nameof(NamespaceTokenGroups.First)).ToInt32();
        var data = FallbackTokenInformation(token, kind, first);
        try
        {
            var count = unchecked((uint)Marshal.ReadInt32(data.Memory)); var stride = Marshal.SizeOf<NamespaceSid>();
            if (count > 65536 || (ulong)first + (ulong)count * (uint)stride > (uint)data.Length)
                throw new InvalidDataException("Fallback token SID list has no complete returned extent.");
            var values = ImmutableArray.CreateBuilder<NativePluginMutexFallbackSid>(checked((int)count));
            for (var at = 0; at < count; ++at)
            {
                var item = Marshal.PtrToStructure<NamespaceSid>(data.Memory + checked(first + at * stride));
                values.Add(new(FallbackSidText(item.Sid, data), item.Attributes));
            }
            return values.MoveToImmutable();
        }
        finally { Marshal.FreeHGlobal(data.Memory); }
    }
    private static string FallbackSidText(nint sid, (nint Memory, int Length) data)
    {
        FallbackRequireExtent(data, sid, 8);
        var size = checked(8 + Marshal.ReadByte(sid, 1) * 4); FallbackRequireExtent(data, sid, size);
        if (!FallbackValidSid(sid)) throw new InvalidDataException("Actual fallback token SID is invalid.");
        if (!FallbackConvertSid(sid, out var text)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Actual fallback token SID is unreadable.");
        try { return Marshal.PtrToStringUni(text) ?? throw new InvalidDataException("Actual fallback token SID has no string."); }
        finally { NamespaceLocalFree(text); }
    }
    private static void FallbackRequireExtent((nint Memory, int Length) data, nint pointer, int length)
    {
        var start = unchecked((ulong)(nuint)data.Memory); var actual = unchecked((ulong)(nuint)pointer);
        if (length < 0 || actual < start || actual - start > (ulong)data.Length || (ulong)length > (ulong)data.Length - (actual - start))
            throw new InvalidDataException("Native fallback metadata points outside its actual returned allocation.");
    }
    private static (nint Memory, int Length) FallbackTokenInformation(NativePluginKernelHandle token, int kind, int minimum)
    {
        _ = NamespaceGetInformation(token, kind, 0, 0, out var needed);
        if (needed < minimum || needed > 1048576) throw new InvalidDataException("Fallback token metadata has no bounded source extent.");
        var memory = Marshal.AllocHGlobal(needed);
        try
        {
            if (!NamespaceGetInformation(token, kind, memory, needed, out var actual)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (actual < minimum || actual > needed) throw new InvalidDataException("Fallback token metadata has an incomplete returned extent.");
            return (memory, actual);
        }
        catch { Marshal.FreeHGlobal(memory); throw; }
    }
    private static NativePluginMutexFallbackDescriptor FallbackDescriptor(string role, string requested, uint? status, nint handle)
    {
        if (handle is 0 or -1) return new(role, requested, status, unchecked((ulong)(nuint)handle), null, null, null, null);
        string? name = null, hash = null, sddl = null, failure = null;
        try
        {
            name = NativePluginObjectDirectory.Name(handle);
            var error = FallbackSecurity(handle, 6, 7, out _, out _, out _, out _, out var descriptor);
            if (error != 0) throw new Win32Exception(unchecked((int)error), "Actual fallback directory descriptor is unreadable.");
            try
            {
                var length = FallbackDescriptorSize(descriptor);
                if (length < 20 || length > 1048576) throw new InvalidDataException("Actual directory descriptor lacks its complete extent.");
                var bytes = new byte[length]; Marshal.Copy(descriptor, bytes, 0, bytes.Length); hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!FallbackSddl(descriptor, 1, 7, out var text, out var count)) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    if (count == 0 || count > 1048576) throw new InvalidDataException("Actual directory SDDL extent is unbounded.");
                    sddl = Marshal.PtrToStringUni(text) ?? throw new InvalidDataException("Actual directory descriptor has no SDDL.");
                }
                finally { NamespaceLocalFree(text); }
            }
            finally { NamespaceLocalFree(descriptor); }
        }
        catch (Exception error) { failure = error.ToString(); }
        return new(role, requested, status, unchecked((ulong)(nuint)handle), name, hash, sddl, failure);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FallbackTokenStatistics
    {
        internal ulong TokenId, AuthenticationId; internal long Expiration;
        internal uint Type, Impersonation, DynamicCharged, DynamicAvailable, GroupCount, PrivilegeCount;
        internal ulong ModifiedId;
    }
    [DllImport("kernel32", EntryPoint = "OpenThread", SetLastError = true)] private static extern NativePluginKernelHandle FallbackOpenThread(uint access, bool inherit, uint thread);
    [DllImport("kernel32", EntryPoint = "GetProcessIdOfThread", SetLastError = true)] private static extern uint FallbackThreadProcess(NativePluginKernelHandle thread);
    [DllImport("advapi32", EntryPoint = "OpenThreadToken", SetLastError = true)] private static extern bool FallbackOpenThreadToken(NativePluginKernelHandle thread, uint access, bool self, out NativePluginKernelHandle token);
    [DllImport("advapi32", EntryPoint = "ConvertSidToStringSidW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool FallbackConvertSid(nint sid, out nint text);
    [DllImport("advapi32", EntryPoint = "IsValidSid")] private static extern bool FallbackValidSid(nint sid);
    [DllImport("advapi32", EntryPoint = "IsValidAcl")] private static extern bool FallbackValidAcl(nint acl);
    [DllImport("advapi32", EntryPoint = "GetSecurityInfo")] private static extern uint FallbackSecurity(nint handle, int kind, uint information, out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);
    [DllImport("advapi32", EntryPoint = "GetSecurityDescriptorLength")] private static extern uint FallbackDescriptorSize(nint descriptor);
    [DllImport("advapi32", EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool FallbackSddl(nint descriptor, uint revision, uint information, out nint text, out uint count);
}

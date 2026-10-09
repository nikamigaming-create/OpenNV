using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginTokenObjectReceipt(string RestrictingSid, string PreviousDaclSha256,
    string AppliedDaclSha256);

// A child-created object's default ACL must allow the same module restricting
// SID used by its private filesystem scopes. This changes only the new child
// token's default for NEW objects; no existing object's ACL is rewritten.
internal static class NativePluginTokenObjects
{
    internal static NativePluginTokenObjectReceipt AdmitDefaultObjects(NativePluginKernelHandle token, string restrictingSid)
    {
        if (!ConvertStringSidToSidW(restrictingSid, out var sid)) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint oldInformation = 0, newAcl = 0;
        var applied = false;
        try
        {
            RequireModuleRestriction(token, sid);
            oldInformation = ReadInformation(token, 6);
            var oldAcl = Marshal.ReadIntPtr(oldInformation);
            if (oldAcl == 0) throw new NotSupportedException("A native child has no explicit default object ACL to retain.");
            var previous = ReadAcl(oldAcl);
            var entry = new ExplicitAccess
            {
                Permissions = 0x10000000, // GENERIC_ALL maps at each newly created object.
                Mode = 1, // GRANT_ACCESS preserves existing deny/allow entries.
                Trustee = new Trustee { Form = 0, Type = 0, Sid = sid }
            };
            var result = SetEntriesInAclW(1, ref entry, oldAcl, out newAcl);
            if (result != 0) throw new Win32Exception(checked((int)result), "Native default object ACL could not retain its module grant.");
            var next = ReadAcl(newAcl);
            var defaultDacl = new DefaultDacl { Acl = newAcl };
            if (!SetTokenInformation(token, 6, ref defaultDacl, Marshal.SizeOf<DefaultDacl>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Native child default object ownership was not applied.");
            applied = true;
            var observed = ReadInformation(token, 6);
            try
            {
                if (!next.AsSpan().SequenceEqual(ReadAcl(Marshal.ReadIntPtr(observed))))
                    throw new InvalidDataException("Native child default object ACL readback diverged.");
                RequireModuleRestriction(token, sid);
            }
            finally { Marshal.FreeHGlobal(observed); }
            return new(restrictingSid, Convert.ToHexString(SHA256.HashData(previous)), Convert.ToHexString(SHA256.HashData(next)));
        }
        catch (Exception error)
        {
            if (applied)
            {
                var original = new DefaultDacl { Acl = Marshal.ReadIntPtr(oldInformation) };
                if (!SetTokenInformation(token, 6, ref original, Marshal.SizeOf<DefaultDacl>()))
                    throw new AggregateException("Native child object admission failed and its previous default was not restored.",
                        error, new Win32Exception(Marshal.GetLastWin32Error()));
            }
            throw;
        }
        finally
        {
            if (oldInformation != 0) Marshal.FreeHGlobal(oldInformation);
            if (newAcl != 0) LocalFree(newAcl);
            LocalFree(sid);
        }
    }

    private static void RequireModuleRestriction(NativePluginKernelHandle token, nint sid)
    {
        if (!IsTokenRestricted(token)) throw new InvalidDataException("Native child token lost its restrictions.");
        var information = ReadInformation(token, 11); // TokenRestrictedSids.
        try
        {
            if (Marshal.ReadInt32(information) != 1)
                throw new NotSupportedException("Native default objects require the exact single selected module restriction.");
            var first = Marshal.OffsetOf<TokenGroups>(nameof(TokenGroups.First)).ToInt32();
            if (!EqualSid(Marshal.ReadIntPtr(information, first), sid))
                throw new InvalidDataException("Native default object grant differs from the actual restricting SID.");
        }
        finally { Marshal.FreeHGlobal(information); }
    }

    private static nint ReadInformation(NativePluginKernelHandle token, int kind)
    {
        _ = GetTokenInformation(token, kind, 0, 0, out var length);
        if (length <= 0 || length > 1024 * 1024) throw new InvalidDataException("Native child token information has no bounded extent.");
        var memory = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, kind, memory, length, out var actual) || actual > length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return memory;
        }
        catch { Marshal.FreeHGlobal(memory); throw; }
    }
    private static byte[] ReadAcl(nint acl)
    {
        if (acl == 0 || !IsValidAcl(acl)) throw new InvalidDataException("Native default object ACL has no valid native extent.");
        var length = unchecked((ushort)Marshal.ReadInt16(acl, 2));
        if (length < 8) throw new InvalidDataException("Native default object ACL is truncated.");
        var bytes = new byte[length]; Marshal.Copy(acl, bytes, 0, length); return bytes;
    }

    [StructLayout(LayoutKind.Sequential)] private struct DefaultDacl { internal nint Acl; }
    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { internal nint Sid; internal uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenGroups { internal uint Count; internal SidAndAttributes First; }
    [StructLayout(LayoutKind.Sequential)] private struct Trustee { internal nint Multiple; internal int Operation, Form, Type; internal nint Sid; }
    [StructLayout(LayoutKind.Sequential)] private struct ExplicitAccess { internal uint Permissions; internal int Mode; internal uint Inheritance; internal Trustee Trustee; }
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSidToSidW(string text, out nint sid);
    [DllImport("advapi32", CharSet = CharSet.Unicode)] private static extern uint SetEntriesInAclW(uint count, ref ExplicitAccess entry, nint oldAcl, out nint newAcl);
    [DllImport("advapi32", SetLastError = true)] private static extern bool SetTokenInformation(NativePluginKernelHandle token, int kind, ref DefaultDacl information, int length);
    [DllImport("advapi32", SetLastError = true)] private static extern bool GetTokenInformation(NativePluginKernelHandle token, int kind, nint information, int length, out int actual);
    [DllImport("advapi32")] private static extern bool IsTokenRestricted(NativePluginKernelHandle token);
    [DllImport("advapi32")] private static extern bool EqualSid(nint first, nint second);
    [DllImport("advapi32")] private static extern bool IsValidAcl(nint acl);
    [DllImport("kernel32")] private static extern nint LocalFree(nint memory);
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using OpenAITunnelManager.Core.Abstractions;

namespace OpenAITunnelManager.Infrastructure.Windows;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const string ServicePrefix = "OpenAITunnelManager/";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(nint buffer);

    public string? Get(string credentialId)
    {
        var target = Target(credentialId);
        if (!CredRead(target, CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "读取 Windows 凭据管理器失败");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Set(string credentialId, string secret)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            throw new ArgumentException("凭据 ID 不能为空", nameof(credentialId));
        }

        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("API Key 不能为空", nameof(secret));
        }

        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 2560)
        {
            throw new ArgumentException("API Key 超过 Windows 通用凭据大小限制", nameof(secret));
        }

        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(credentialId),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "OpenAI tunnel-client runtime key"
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "写入 Windows 凭据管理器失败");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete(string credentialId)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            return;
        }

        if (CredDelete(Target(credentialId), CredTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error, "删除 Windows 凭据管理器凭据失败");
        }
    }

    private static string Target(string credentialId) => ServicePrefix + credentialId.Trim();
}

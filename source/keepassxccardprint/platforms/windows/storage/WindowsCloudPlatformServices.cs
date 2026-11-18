using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using KeePassXC.CloudTransport;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Platforms.Windows.Storage;

/// <summary>Windows-only credentials, browser and Proton CLI host services.
/// Shared synchronization code only sees the portable ICloudPlatformServices contract.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsCloudPlatformServices : ICloudPlatformServices
{
    private readonly WindowsCloudSecrets _secrets = new();
    public ICloudSecretStore Secrets => _secrets;
    public string ResolveProtonExecutable(string? configured) => WindowsProtonCliManager.Resolve(configured);
    public Task<ProtonCliStatus> CheckProtonCliAsync(string? configured, CancellationToken ct)
        => WindowsProtonCliManager.CheckAsync(configured, ct);
    public Task<string> InstallProtonCliAsync(string? configured, IProgress<int>? progress, CancellationToken ct)
        => WindowsProtonCliManager.InstallAsync(configured, progress, ct);

    public CloudTransportServices CreateServices() => new()
    {
        Secrets = _secrets,
        Browser = new CloudBrowser(),
        Cli = new CloudCli(),
        ProtonLogin = new ProtonLogin()
    };

    private sealed class CloudBrowser : ICloudBrowserLauncher
    {
        public void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    private sealed class CloudCli : ICloudCliRunner
    {
        public async Task<string> RunAsync(string executable, TimeSpan timeout, CancellationToken ct,
            params string[] args)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            };
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            if (!process.Start()) throw new IOException("Proton CLI could not start.");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            var stdout = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            try { await process.WaitForExitAsync(timeoutCts.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new IOException("Proton CLI failed: " + error);
            return output;
        }
    }
    private sealed class ProtonLogin : IProtonLoginLauncher
    {
        public async Task<bool> StartLoginAsync(string executable, CancellationToken ct)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                { UseShellExecute = false, CreateNoWindow = false }
            };
            process.StartInfo.ArgumentList.Add("auth");
            process.StartInfo.ArgumentList.Add("login");
            if (!process.Start()) return false;
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
    }
}

// Windows Generic Credentials remain on this machine and never enter configuration.zip.
[SupportedOSPlatform("windows")]
internal sealed class WindowsCloudSecrets : ICloudSecretStore
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags, Type;
        public IntPtr TargetName, Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadNative(string target, uint type, uint reserved, out IntPtr ptr);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteNative(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void FreeNative(IntPtr ptr);

    public (string User, string Password) Read(string target)
    {
        CheckTarget(target);
        if (!ReadNative(target, 1, 0, out var ptr))
            throw new InvalidOperationException("Cloud credential is missing in Windows Credential Manager.");
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(ptr);
            if (credential.BlobSize is > 2560 or 0 || credential.BlobSize % 2 != 0)
                throw new InvalidDataException("Invalid cloud credential.");
            var bytes = new byte[(int)credential.BlobSize];
            Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
            try { return (Marshal.PtrToStringUni(credential.UserName) ?? "", Encoding.Unicode.GetString(bytes)); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { FreeNative(ptr); }
    }
    public void Write(string target, string user, string secret)
    {
        CheckTarget(target);
        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 2560) throw new ArgumentException("Credential is too long.");
        var targetPtr = Marshal.StringToHGlobalUni(target);
        var userPtr = Marshal.StringToHGlobalUni(user);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var entry = new Credential { Type = 1, TargetName = targetPtr,
                UserName = userPtr, Blob = blob, BlobSize = (uint)bytes.Length, Persist = 2 };
            if (!WriteNative(ref entry, 0))
                throw new IOException("Could not save cloud credential to Windows Credential Manager.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(userPtr);
            Marshal.FreeHGlobal(targetPtr);
        }
    }
    private static void CheckTarget(string target)
    {
        if (!target.StartsWith("KeePassXC-Card-Print/", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected credential target.");
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CodexTimezoneGuard.Core;

[SupportedOSPlatform("windows")]
public sealed class WindowsRunningGuard : IRunningGuard
{
    public RunningState Check(CodexCandidate candidate) => new ObservedRunningGuard(Observe, DesktopIdentity.IsDesktopLayoutForRunningProcess).Check(candidate);

    private static IEnumerable<RunningProcessObservation> Observe()
    {
        var observations = new List<RunningProcessObservation>();
        // Old Codex packages may use ChatGPT.exe. Do not read account files or process environments.
        foreach (string name in new[] { "Codex", "ChatGPT" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    IntPtr handle = Native.OpenProcess(0x1000, false, process.Id);
                    if (handle == IntPtr.Zero) { observations.Add(new(null, null, false)); continue; }
                    try
                    {
                        uint length = 32768; var path = new StringBuilder((int)length);
                        if (!Native.QueryFullProcessImageName(handle, 0, path, ref length)) { observations.Add(new(null, null, false)); continue; }
                        uint familyLength = 256; var family = new StringBuilder((int)familyLength);
                        int result = Native.GetPackageFamilyName(handle, ref familyLength, family);
                        observations.Add(new(path.ToString(), result == 0 ? family.ToString() : null, result is 0 or 15700));
                    }
                    catch (Exception e) when (e is ArgumentException or IOException or System.ComponentModel.Win32Exception) { observations.Add(new(null, null, false)); }
                    finally { Native.CloseHandle(handle); }
                }
            }
        }
        return observations;
    }
}

/// <summary>Never terminates existing processes. The only termination is our own unreleased suspended child.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsChildLauncher : IChildLauncher
{
    public int Launch(CodexCandidate candidate, string timezone)
    {
        candidate = CodexDiscovery.ResolveCurrent(candidate); // Store updates cannot leave a trusted stale path.
        if (!DesktopIdentity.IsValid(candidate)) throw new GuardException("invalid_desktop_identity");
        var state = new WindowsRunningGuard().Check(candidate); // last check immediately before creation
        if (state != RunningState.Clear) throw new GuardException(state == RunningState.Running ? "already_running" : "running_check_uncertain");
        if (candidate.PackageFamily is not null) return LaunchSuspended(candidate, timezone);
        try { using var process = Process.Start(ChildEnvironment.StartInfo(candidate.Path, timezone)); return process?.Id ?? throw new GuardException("process_create_failed"); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException) { throw new GuardException("process_create_failed"); }
    }
    private static int LaunchSuspended(CodexCandidate candidate, string timezone)
    {
        var info = ChildEnvironment.StartInfo(candidate.Path, timezone);
        string block = string.Join('\0', info.Environment.Where(p => p.Value is not null).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)) + "\0\0";
        IntPtr environment = Marshal.StringToHGlobalUni(block);
        var startup = new Native.StartupInfo { Size = Marshal.SizeOf<Native.StartupInfo>() }; Native.ProcessInfo process = default; bool resumed = false;
        try
        {
            if (!Native.CreateProcess(candidate.Path, new StringBuilder('"' + candidate.Path + '"'), IntPtr.Zero, IntPtr.Zero, false, 0x00000004 | 0x00000400, environment, info.WorkingDirectory, ref startup, out process)) throw new GuardException("store_create_failed");
            uint fullLength = 512, appLength = 512;
            var full = new StringBuilder((int)fullLength); var app = new StringBuilder((int)appLength);
            if (Native.GetPackageFullName(process.Process, ref fullLength, full) != 0 || Native.GetApplicationUserModelId(process.Process, ref appLength, app) != 0 || full.ToString() != candidate.PackageFullName || app.ToString() != candidate.ApplicationId) throw new GuardException("store_identity_failed");
            if (Native.ResumeThread(process.Thread) == uint.MaxValue) throw new GuardException("store_resume_failed");
            resumed = true; return checked((int)process.ProcessId);
        }
        finally
        {
            // CREATE_SUSPENDED has not executed application code; never use this on an existing/user process.
            if (!resumed && process.Process != IntPtr.Zero) Native.TerminateProcess(process.Process, 1);
            if (process.Thread != IntPtr.Zero) Native.CloseHandle(process.Thread);
            if (process.Process != IntPtr.Zero) Native.CloseHandle(process.Process);
            Marshal.FreeHGlobal(environment);
        }
    }
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XChars, YChars, FillAttribute, Flags; public ushort ShowWindow, Reserved2Size; public IntPtr Reserved2, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string workingDirectory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder id);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
}

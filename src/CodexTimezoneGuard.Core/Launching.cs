using System.Diagnostics;

namespace CodexTimezoneGuard.Core;

public static class ChildEnvironment
{
    public static Dictionary<string, string> Create(IEnumerable<KeyValuePair<string, string>> parent, string timezone)
    {
        if (!TimezoneCatalog.TryGet(timezone, out _)) throw new GuardException("invalid_timezone");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in parent) if (!pair.Key.Equals("TZ", StringComparison.OrdinalIgnoreCase) && !pair.Key.Equals("ELECTRON_RUN_AS_NODE", StringComparison.OrdinalIgnoreCase)) result[pair.Key] = pair.Value;
        result["TZ"] = timezone; return result;
    }
    public static ProcessStartInfo StartInfo(string executable, string timezone)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))! };
        var child = Create(info.Environment.Where(p => p.Value is not null).Select(p => new KeyValuePair<string, string>(p.Key, p.Value!)), timezone);
        info.Environment.Clear(); foreach (var p in child) info.Environment[p.Key] = p.Value;
        return info;
    }
}
public sealed record CodexCandidate(string Path, string DisplayName, string? PackageFamily = null, string? PackageFullName = null, string? ApplicationId = null)
{
    public override string ToString() => DisplayName;
}
public static class DesktopIdentity
{
    public static bool HasElectronResources(string executable)
    {
        string folder = Path.GetDirectoryName(executable) ?? "";
        return File.Exists(executable) && File.Exists(Path.Combine(folder, "resources", "app.asar")) &&
            File.Exists(Path.Combine(folder, "icudtl.dat")) &&
            (File.Exists(Path.Combine(folder, "chrome_100_percent.pak")) || File.Exists(Path.Combine(folder, "resources.pak")));
    }
    public static bool IsValid(CodexCandidate candidate) => candidate.PackageFamily is null ? IsDesktopLayout(candidate.Path) :
        candidate.PackageFamily == CodexDiscovery.StoreFamily && candidate.PackageFullName?.StartsWith("OpenAI.Codex_", StringComparison.Ordinal) == true &&
        candidate.ApplicationId?.StartsWith(CodexDiscovery.StoreFamily + "!", StringComparison.Ordinal) == true &&
        Path.GetFileName(candidate.Path) is "Codex.exe" or "ChatGPT.exe" && HasElectronResources(candidate.Path);
    public static bool IsDesktopLayout(string executable)
    {
        try
        {
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || !Path.GetFileName(executable).Equals("Codex.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var info = FileVersionInfo.GetVersionInfo(executable);
            // CLI name alone, npm wrappers and a manually selected unknown exe are never enough.
            return info.ProductName?.Equals("Codex", StringComparison.OrdinalIgnoreCase) == true &&
                info.CompanyName?.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) == true &&
                HasElectronResources(executable);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { return false; }
    }
}
public enum RunningState { Clear, Running, Uncertain }
public interface IRunningGuard { RunningState Check(CodexCandidate candidate); }
public interface IChildLauncher { int Launch(CodexCandidate candidate, string timezone); }
public sealed class LaunchCoordinator(IRunningGuard running, IChildLauncher launcher)
{
    public int Launch(CodexCandidate candidate, string timezone)
    {
        if (!TimezoneCatalog.TryGet(timezone, out _)) throw new GuardException("invalid_timezone");
        // Existing-instance checks take priority so the GUI can give the actionable save-and-exit hint.
        var state = running.Check(candidate);
        if (state == RunningState.Running) throw new GuardException("already_running");
        if (state != RunningState.Clear) throw new GuardException("running_check_uncertain");
        if (!DesktopIdentity.IsValid(candidate)) throw new GuardException("invalid_desktop_identity");
        return launcher.Launch(candidate, timezone);
    }
}

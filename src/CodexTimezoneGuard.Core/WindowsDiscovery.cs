using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CodexTimezoneGuard.Core;

public sealed record DiscoveryResult(IReadOnlyList<CodexCandidate> Candidates, string Code);
public static class CodexDiscovery
{
    public const string StoreFamily = "OpenAI.Codex_2p2nqsd0c76g0";
    public static CodexCandidate? Unique(IReadOnlyList<CodexCandidate> candidates) => candidates.Count == 1 ? candidates[0] : null;
    [SupportedOSPlatform("windows")]
    public static DiscoveryResult Discover()
    {
        var candidates = new List<CodexCandidate>(); bool packageError = false;
        try
        {
            foreach (string fullName in PackageNames())
            {
                var candidate = PackageCandidate(fullName); if (candidate is not null) candidates.Add(candidate);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or GuardException or XmlException or ArgumentException) { packageError = true; }
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        {
            foreach (string relative in new[] { Path.Combine("Programs", "Codex", "Codex.exe"), Path.Combine("Codex", "Codex.exe") })
            {
                string path = Path.Combine(root, relative);
                if (DesktopIdentity.IsDesktopLayout(path) && !candidates.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) candidates.Add(new(path, "Codex 桌面 · 普通安装"));
            }
        }
        return new(candidates, packageError ? "discovery_partial" : candidates.Count == 0 ? "no_client" : candidates.Count == 1 ? "client_found" : "client_ambiguous");
    }
    [SupportedOSPlatform("windows")]
    public static CodexCandidate? ValidateManual(string path)
    {
        path = Path.GetFullPath(path);
        // Store paths never bypass registered identity just because resources look like Electron.
        var registered = Discover().Candidates.FirstOrDefault(c => c.PackageFamily is not null && c.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (registered is not null) return registered;
        if (path.Contains(Path.DirectorySeparatorChar + "WindowsApps" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        return DesktopIdentity.IsDesktopLayout(path) ? new(path, "Codex 桌面 · 已验证手动路径") : null;
    }
    [SupportedOSPlatform("windows")]
    public static CodexCandidate ResolveCurrent(CodexCandidate candidate)
    {
        if (candidate.PackageFamily is null) return ValidateManual(candidate.Path) ?? throw new GuardException("invalid_desktop_identity");
        if (candidate.PackageFamily != StoreFamily) throw new GuardException("invalid_package_identity");
        var current = Discover().Candidates.Where(c => c.PackageFamily == candidate.PackageFamily).ToArray();
        return Unique(current) ?? throw new GuardException(current.Length == 0 ? "store_not_registered" : "client_ambiguous");
    }
    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> PackageNames()
    {
        uint count = 0, length = 0;
        int result = Native.GetPackagesByPackageFamily(StoreFamily, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
        if (result == 0 && count == 0) return [];
        if (result != 122 || count > 64 || length > 65536) throw new GuardException("package_enumeration_failed");
        IntPtr pointers = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)length * 2));
        try
        {
            if (Native.GetPackagesByPackageFamily(StoreFamily, ref count, pointers, ref length, buffer) != 0) throw new GuardException("package_enumeration_failed");
            var names = new List<string>();
            for (int i = 0; i < count; i++) { string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointers, i * IntPtr.Size)); if (name is not null && name.StartsWith("OpenAI.Codex_", StringComparison.Ordinal)) names.Add(name); }
            return names;
        }
        finally { Marshal.FreeHGlobal(pointers); Marshal.FreeHGlobal(buffer); }
    }
    [SupportedOSPlatform("windows")]
    private static CodexCandidate? PackageCandidate(string fullName)
    {
        uint length = 0;
        if (Native.GetPackagePathByFullName(fullName, ref length, null) != 122 || length is 0 or > 32768) return null;
        var path = new StringBuilder((int)length);
        if (Native.GetPackagePathByFullName(fullName, ref length, path) != 0) return null;
        uint familyLength = 256; var family = new StringBuilder((int)familyLength);
        if (Native.PackageFamilyNameFromFullName(fullName, ref familyLength, family) != 0 || family.ToString() != StoreFamily) return null;
        string root = Path.GetFullPath(path.ToString()); string manifest = Path.Combine(root, "AppxManifest.xml");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 1048576) return null;
        using var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 1048576, XmlResolver = null });
        var xml = XDocument.Load(reader); var identity = xml.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity");
        if (identity?.Attribute("Name")?.Value != "OpenAI.Codex") return null;
        foreach (var app in xml.Descendants().Where(e => e.Name.LocalName == "Application"))
        {
            string? relative = app.Attribute("Executable")?.Value, id = app.Attribute("Id")?.Value;
            if (relative is null || id is null || id.Length > 64) continue;
            string exe = Path.GetFullPath(Path.Combine(root, relative));
            if (!exe.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileName(exe) is not ("Codex.exe" or "ChatGPT.exe") || !DesktopIdentity.HasElectronResources(exe)) continue;
            return new(exe, "Codex 桌面 · Store/MSIX（运行时未验证）", StoreFamily, fullName, StoreFamily + "!" + id);
        }
        return null;
    }
    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint bufferLength, IntPtr buffer);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int PackageFamilyNameFromFullName(string fullName, ref uint length, StringBuilder family);
    }
}

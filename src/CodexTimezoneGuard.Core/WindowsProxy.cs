using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace CodexTimezoneGuard.Core;

public static class ProxyConfiguration
{
    public static ProxyRoute Select(string? proxy, string? bypass, Uri destination)
    {
        if (string.IsNullOrWhiteSpace(proxy)) return new(ProxyKind.Direct);
        if (!string.IsNullOrWhiteSpace(bypass))
        {
            foreach (string item in bypass.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (item.Equals("<local>", StringComparison.OrdinalIgnoreCase)) { if (!destination.Host.Contains('.')) return new(ProxyKind.Direct); continue; }
                if (item.Length > 255) return new(ProxyKind.Error, null, "proxy_unsupported");
                string pattern = "^" + Regex.Escape(item).Replace("\\*", ".*") + "$";
                if (Regex.IsMatch(destination.Host, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) return new(ProxyKind.Direct);
            }
        }
        string? selected = null;
        foreach (string item in proxy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equal = item.IndexOf('=');
            if (equal > 0)
            {
                if (item[..equal].Equals(destination.Scheme, StringComparison.OrdinalIgnoreCase)) { selected = item[(equal + 1)..]; break; }
            }
            else if (selected is null) selected = item;
        }
        if (selected is null || selected.Any(char.IsWhiteSpace) || selected.Contains('@')) return new(ProxyKind.Error, null, "proxy_unsupported");
        if (!selected.Contains("://", StringComparison.Ordinal)) selected = "http://" + selected;
        if (!Uri.TryCreate(selected, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Host.Length == 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0) return new(ProxyKind.Error, null, "proxy_unsupported");
        return new(ProxyKind.Http, uri, "system_proxy");
    }
}

/// <summary>Read-only, per-destination WinHTTP/PAC resolution. Errors never become implicit direct routes.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProxyResolver : IProxyResolver
{
    public ProxyRoute Resolve(Uri destination)
    {
        if (!SafeHttpTransport.IsAllowed(destination)) return new(ProxyKind.Error, null, "endpoint_not_allowed");
        if (!Native.WinHttpGetIEProxyConfigForCurrentUser(out var config)) return new(ProxyKind.Error, null, "proxy_config_unavailable");
        try
        {
            string? pac = Marshal.PtrToStringUni(config.AutoConfigUrl);
            if (config.AutoDetect || !string.IsNullOrWhiteSpace(pac))
            {
                // Do not log the PAC URL, proxy URL, Win32 exception text or response body.
                IntPtr session = Native.WinHttpOpen("CodexTimezoneGuard/0.1", 1, null, null, 0);
                if (session == IntPtr.Zero) return new(ProxyKind.Error, null, "pac_failed");
                try
                {
                    Native.WinHttpSetTimeouts(session, 10000, 10000, 10000, 10000);
                    var options = new Native.AutoProxyOptions { Flags = (config.AutoDetect ? 1u : 0) | (!string.IsNullOrWhiteSpace(pac) ? 2u : 0), AutoDetectFlags = config.AutoDetect ? 3u : 0, AutoConfigUrl = pac, AutoLogonIfChallenged = false };
                    if (!Native.WinHttpGetProxyForUrl(session, destination.AbsoluteUri, ref options, out var info)) return new(ProxyKind.Error, null, "pac_failed");
                    try
                    {
                        if (info.AccessType == 1) return new(ProxyKind.Direct, null, "pac_direct");
                        if (info.AccessType != 3) return new(ProxyKind.Error, null, "pac_failed");
                        var result = ProxyConfiguration.Select(Marshal.PtrToStringUni(info.Proxy), Marshal.PtrToStringUni(info.Bypass), destination);
                        return result with { Code = result.Kind == ProxyKind.Error ? result.Code : result.Kind == ProxyKind.Direct ? "pac_direct" : "pac_proxy" };
                    }
                    finally { Free(info.Proxy); Free(info.Bypass); }
                }
                finally { Native.WinHttpCloseHandle(session); }
            }
            return ProxyConfiguration.Select(Marshal.PtrToStringUni(config.Proxy), Marshal.PtrToStringUni(config.Bypass), destination);
        }
        finally { Free(config.AutoConfigUrl); Free(config.Proxy); Free(config.Bypass); }
    }
    private static void Free(IntPtr p) { if (p != IntPtr.Zero) Native.GlobalFree(p); }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct IeConfig { [MarshalAs(UnmanagedType.Bool)] public bool AutoDetect; public IntPtr AutoConfigUrl, Proxy, Bypass; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct AutoProxyOptions { public uint Flags, AutoDetectFlags; [MarshalAs(UnmanagedType.LPWStr)] public string? AutoConfigUrl; public IntPtr Reserved; public uint Reserved2; [MarshalAs(UnmanagedType.Bool)] public bool AutoLogonIfChallenged; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProxyInfo { public uint AccessType; public IntPtr Proxy, Bypass; }
        [DllImport("winhttp.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinHttpGetIEProxyConfigForCurrentUser(out IeConfig config);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr WinHttpOpen(string userAgent, uint accessType, string? proxy, string? bypass, uint flags);
        [DllImport("winhttp.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinHttpSetTimeouts(IntPtr handle, int resolve, int connect, int send, int receive);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinHttpGetProxyForUrl(IntPtr session, string url, ref AutoProxyOptions options, out ProxyInfo info);
        [DllImport("winhttp.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinHttpCloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalFree(IntPtr memory);
    }
}

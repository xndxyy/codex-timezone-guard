using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CodexTimezoneGuard.Core;

public static class PublicAddress
{
    public static bool TryParse(string text, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrEmpty(text) || text != text.Trim() || text.Contains('%') || !IPAddress.TryParse(text, out var parsed)) return false;
        byte[] b = parsed.GetAddressBytes();
        if (parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            // IPAddress also accepts historical short/hex/octal forms. Only dotted decimal is allowed.
            if (parsed.ToString() != text) return false;
            if (b[0] is 0 or 10 or 127 || b[0] >= 224 || (b[0] == 100 && (b[1] & 0xc0) == 64) ||
                (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31) ||
                (b[0] == 192 && (b[1] == 168 || b[1] == 0 && b[2] is 0 or 2 || b[1] == 88 && b[2] == 99)) ||
                (b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100)) ||
                (b[0] == 203 && b[1] == 0 && b[2] == 113)) return false;
        }
        else if (parsed.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Conservative global-unicast policy: reject special-purpose, transition, documentation,
            // scoped/mapped and unallocated ranges instead of treating every non-private IPv6 as public.
            if ((b[0] & 0xe0) != 0x20 || parsed.IsIPv4MappedToIPv6 ||
                (b[0] == 0x20 && b[1] == 1 && b[2] < 2) ||
                (b[0] == 0x20 && b[1] == 1 && b[2] == 0x0d && b[3] == 0xb8) ||
                (b[0] == 0x20 && b[1] == 2) ||
                (b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0)) return false;
        }
        else return false;
        address = parsed; return true;
    }
}
public static class TraceParser
{
    public static IPAddress Parse(string body)
    {
        if (body.Length > SafeHttpTransport.BodyLimit) throw new GuardException("body_too_large");
        if (body.Contains('<') || body.Contains('>') || body.Contains('\0')) throw new GuardException("invalid_trace");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in body.Split('\n'))
        {
            string item = line.TrimEnd('\r'); if (item.Length == 0) continue;
            int index = item.IndexOf('=');
            if (index < 1 || index > 32 || !item[..index].All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ||
                !fields.TryAdd(item[..index], item[(index + 1)..])) throw new GuardException("invalid_trace");
        }
        if (!fields.TryGetValue("ip", out var ip) || !PublicAddress.TryParse(ip, out var address)) throw new GuardException("invalid_trace");
        return address;
    }
}
public static class GeoParser
{
    public static string Parse(string body, IPAddress expected)
    {
        if (body.Length > SafeHttpTransport.BodyLimit) throw new GuardException("body_too_large");
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 12 });
            var root = document.RootElement;
            Unique(root);
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) throw new GuardException("geo_unavailable");
            if (!root.TryGetProperty("ip", out var ip) || ip.ValueKind != JsonValueKind.String || !PublicAddress.TryParse(ip.GetString()!, out var echoed) || !echoed.Equals(expected)) throw new GuardException("geo_ip_mismatch");
            if (!root.TryGetProperty("timezone", out var tz) || tz.ValueKind != JsonValueKind.Object || !tz.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || !TimezoneCatalog.TryGet(id.GetString(), out _)) throw new GuardException("invalid_timezone");
            return id.GetString()!;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { throw new GuardException("invalid_geo"); }
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new GuardException("invalid_geo"); Unique(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var e in element.EnumerateArray()) Unique(e);
    }
}
public sealed record ProbeReply(int Status, string Body);
public interface IProbeTransport { Task<ProbeReply> GetAsync(Uri uri, CancellationToken cancellationToken); }
public sealed record DetectionResult(bool Suggested, string? Timezone, string Code, int Observations, string? AddressFamily = null);
public sealed class EgressDetector(IProbeTransport transport)
{
    public static readonly Uri Primary = new("https://chatgpt.com/cdn-cgi/trace");
    public static readonly Uri Auxiliary = new("https://api.openai.com/cdn-cgi/trace");
    public async Task<DetectionResult> DetectAsync(bool auxiliary, CancellationToken cancellationToken)
    {
        int observations = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(50));
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            var first = TraceParser.Parse(await Fetch(Primary, timeout.Token)); observations++;
            var second = TraceParser.Parse(await Fetch(Primary, timeout.Token)); observations++;
            if (!first.Equals(second)) return new(false, null, "conflicting_egress", observations);
            bool auxUnavailable = false;
            if (auxiliary)
            {
                try
                {
                    var other = TraceParser.Parse(await Fetch(Auxiliary, timeout.Token)); observations++;
                    if (!first.Equals(other)) return new(false, null, "conflicting_egress", observations);
                }
                catch (GuardException e) when (e.Code is "http_403" or "http_404" or "invalid_trace" or "network_failed" or "request_timeout") { auxUnavailable = true; }
            }
            var geo = new Uri("https://ipwho.is/" + first);
            string zone = GeoParser.Parse(await Fetch(geo, timeout.Token), first);
            return new(true, zone, auxUnavailable ? "suggestion_aux_unavailable" : "suggestion", observations, first.AddressFamily == AddressFamily.InterNetwork ? "IPv4" : "IPv6");
        }
        catch (OperationCanceledException) { return new(false, null, cancellationToken.IsCancellationRequested ? "cancelled" : "request_timeout", observations); }
        catch (GuardException e) { return new(false, null, e.Code, observations); }
        catch (HttpRequestException) { return new(false, null, "network_failed", observations); }
    }
    private async Task<string> Fetch(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reply = await transport.GetAsync(uri, cancellationToken);
        if (reply.Status != 200) throw new GuardException("http_" + reply.Status);
        if (Encoding.UTF8.GetByteCount(reply.Body) > SafeHttpTransport.BodyLimit) throw new GuardException("body_too_large");
        return reply.Body;
    }
}
public enum ProxyKind { Direct, Http, Error }
public sealed record ProxyRoute(ProxyKind Kind, Uri? Proxy = null, string Code = "direct");
public interface IProxyResolver { ProxyRoute Resolve(Uri destination); }
public sealed class SafeHttpTransport(IProxyResolver resolver, Func<ProxyRoute, HttpMessageHandler>? handlerFactory = null, TimeSpan? requestTimeout = null) : IProbeTransport
{
    public const int BodyLimit = 16384;
    public static bool IsAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        if (uri == EgressDetector.Primary || uri == EgressDetector.Auxiliary) return true;
        return uri.Host == "ipwho.is" && uri.AbsolutePath.Length > 1 && PublicAddress.TryParse(Uri.UnescapeDataString(uri.AbsolutePath[1..]), out _);
    }
    public async Task<ProbeReply> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!IsAllowed(uri)) throw new GuardException("endpoint_not_allowed");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(12)); var token = deadline.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            // PAC runs on a worker. WaitAsync lets cancellation stop waiting without blocking the UI;
            // the WinHTTP worker finishes on its own timeout, with no shared state or fallback route.
            var route = await Task.Run(() => resolver.Resolve(uri), token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (route.Kind == ProxyKind.Error) throw new GuardException(route.Code);
            using var handler = handlerFactory?.Invoke(route) ?? CreateDefaultHandler(route);
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = BodyLimit };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("CodexTimezoneGuard/0.1"); request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.Content.Headers.ContentLength > BodyLimit) throw new GuardException("body_too_large");
            // Status is returned without reading an HTML challenge, redirect body or error page.
            if (response.StatusCode != HttpStatusCode.OK) return new((int)response.StatusCode, "");
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var memory = new MemoryStream(); byte[] buffer = new byte[2048];
            while (true)
            {
                int read = await stream.ReadAsync(buffer, token); if (read == 0) break;
                if (memory.Length + read > BodyLimit) throw new GuardException("body_too_large");
                memory.Write(buffer, 0, read);
            }
            return new((int)response.StatusCode, Encoding.UTF8.GetString(memory.ToArray()));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new GuardException("request_timeout"); }
        catch (HttpRequestException) { throw new GuardException("network_failed"); }
        catch (IOException) { throw new GuardException("network_failed"); }
    }
    private static HttpClientHandler CreateDefaultHandler(ProxyRoute route)
    {
        if (route.Kind == ProxyKind.Http && (route.Proxy is null || route.Proxy.Scheme is not ("http" or "https") || route.Proxy.UserInfo.Length != 0)) throw new GuardException("proxy_unsupported");
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false, Credentials = null, PreAuthenticate = false, AutomaticDecompression = DecompressionMethods.None, UseProxy = route.Kind == ProxyKind.Http };
        if (route.Kind == ProxyKind.Http) { handler.Proxy = new WebProxy(route.Proxy!) { Credentials = null }; handler.DefaultProxyCredentials = null; }
        return handler;
    }
}

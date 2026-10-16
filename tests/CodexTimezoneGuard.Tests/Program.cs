using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CodexTimezoneGuard.Core;

var results = new List<object>();
int passed = 0, failed = 0;
async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; results.Add(new { name, passed = true }); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e is GuardException g ? g.Code : e.GetType().Name + ": " + e.Message }); Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Check(bool condition, string message = "behavior assertion failed") { if (!condition) throw new InvalidOperationException(message); }
void Error(Action action, string code)
{
    try { action(); } catch (GuardException e) { Check(e.Code == code, $"expected {code}, got {e.Code}"); return; }
    throw new InvalidOperationException($"expected {code}, operation succeeded");
}
Task Sync(Action action) { action(); return Task.CompletedTask; }
string work = Path.Combine(FindRoot(), ".cache", "tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
await Test("IANA accepts region, UTC and catalog", () => Sync(() => {
    Check(TimezoneCatalog.TryGet("America/New_York", out _), "valid IANA rejected");
    Check(TimezoneCatalog.TryGet("Etc/UTC", out _)); Check(TimezoneCatalog.All.Contains("Asia/Shanghai"));
}));
await Test("IANA rejects Windows names, country and offsets", () => Sync(() => {
    foreach (string s in new[] { "China", "Pacific Standard Time", "UTC+8", "Mars/Olympus", "", " Asia/Shanghai", "Asia/Shanghai\n" }) Check(!TimezoneCatalog.TryGet(s, out _));
}));
await Test("DST winter and summer preview", () => Sync(() => {
    Check(TimezoneCatalog.TryGet("America/New_York", out var z));
    Check(z.GetUtcOffset(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) == TimeSpan.FromHours(-5));
    Check(z.GetUtcOffset(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)) == TimeSpan.FromHours(-4));
    Check(TimezoneCatalog.Preview("Asia/Shanghai", DateTimeOffset.UnixEpoch).Contains("08:00"));
}));
await Test("settings roundtrip atomic and no IP fields", () => Sync(() => {
    var store = new SettingsStore(Path.Combine(work, "settings"));
    store.Save(new("manual", "Asia/Shanghai", true));
    Check(store.Load().Settings.Timezone == "Asia/Shanghai", "saved zone not restored");
    Check(!File.ReadAllText(store.FilePath).Contains("ip", StringComparison.OrdinalIgnoreCase));
    store.Save(new("manual", "Europe/London"));
    Check(store.Load().Settings.Timezone == "Europe/London");
    Check(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!).Length == 1);
}));
await Test("corrupt settings fall back explicitly", () => Sync(() => {
    var store = new SettingsStore(Path.Combine(work, "corrupt")); Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
    File.WriteAllText(store.FilePath, "{not-json"); Check(store.Load().Corrupt, "corrupt config not marked");
}));
await Test("invalid settings cannot replace valid saved settings", () => Sync(() => {
    var s = new SettingsStore(Path.Combine(work, "valid")); s.Save(new("manual", "Asia/Tokyo"));
    Error(() => s.Save(new("manual", "bad")), "invalid_timezone"); Check(s.Load().Settings.Timezone == "Asia/Tokyo");
    Error(() => s.Save(new("other", "Asia/Tokyo")), "invalid_settings");
    s.Reset(); Check(!File.Exists(s.FilePath));
}));
foreach (string address in new[] { "8.8.8.8", "1.1.1.1", "2606:4700:4700::1111", "2001:4860:4860::8888" })
    await Test("public address " + address, () => Sync(() => Check(PublicAddress.TryParse(address, out _), "globally routable IP rejected")));
foreach (string address in new[] { "127.0.0.1", "0.1.2.3", "10.0.0.1", "100.64.0.1", "169.254.1.1", "172.16.0.1", "192.168.1.1", "192.0.0.9", "192.0.2.1", "198.18.0.1", "198.51.100.1", "203.0.113.2", "224.0.0.1", "240.1.2.3", "255.255.255.255", "::", "::1", "::ffff:8.8.8.8", "fe80::1", "fc00::1", "ff02::1", "2001:db8::1", "2001::1", "2002:0808:0808::1", "64:ff9b::808:808", "2001:20::1", "3fff::1", "8.8.8.8%1", "001.1.1.1", "https://8.8.8.8", "bad" })
    await Test("reject special IP " + address, () => Sync(() => Check(!PublicAddress.TryParse(address, out _))));
await Test("trace extracts explicit public IP only", () => Sync(() => Check(TraceParser.Parse("fl=123\nip=8.8.8.8\nloc=US\n").Equals(IPAddress.Parse("8.8.8.8")))));
foreach (string body in new[] { "ip=8.8.8.8\nip=1.1.1.1", "<html>ip=8.8.8.8</html>", "loc=US", "ip=10.0.0.1", "ip=garbage", "ip=8.8.8.8\ninvalid" })
    await Test("reject malformed trace " + Array.IndexOf(new[] { "a" }, body) + body.Length, () => Sync(() => Error(() => TraceParser.Parse(body), "invalid_trace")));
await Test("geo verifies echoed queried IP and IANA", () => Sync(() => {
    Check(GeoParser.Parse(Geo("8.8.8.8"), IPAddress.Parse("8.8.8.8")) == "America/New_York");
    Error(() => GeoParser.Parse(Geo("1.1.1.1"), IPAddress.Parse("8.8.8.8")), "geo_ip_mismatch");
    Error(() => GeoParser.Parse(Geo("8.8.8.8", "US"), IPAddress.Parse("8.8.8.8")), "invalid_timezone");
}));
await Test("target twice then explicit observed IP lookup", async () => {
    var f = new FakeTransport([new(200, "ip=8.8.8.8"), new(200, "ip=8.8.8.8"), new(200, Geo("8.8.8.8"))]);
    var r = await new EgressDetector(f).DetectAsync(false, default);
    Check(r.Suggested && r.Timezone == "America/New_York" && r.Observations == 2, "consistent target evidence not suggested");
    Check(f.Requests.Count == 3 && f.Requests[0] == EgressDetector.Primary && f.Requests[1] == EgressDetector.Primary);
    Check(f.Requests[2].AbsoluteUri == "https://ipwho.is/8.8.8.8", "explicit observed IP not used");
});
await Test("conflicting repeated IP forces manual before geo", async () => {
    var f = new FakeTransport([new(200, "ip=8.8.8.8"), new(200, "ip=1.1.1.1")]);
    var r = await new EgressDetector(f).DetectAsync(false, default); Check(!r.Suggested && r.Code == "conflicting_egress" && f.Requests.Count == 2);
});
await Test("conflicting auxiliary forces manual", async () => {
    var f = new FakeTransport([new(200, "ip=8.8.8.8"), new(200, "ip=8.8.8.8"), new(200, "ip=1.1.1.1")]);
    var r = await new EgressDetector(f).DetectAsync(true, default); Check(!r.Suggested && r.Code == "conflicting_egress");
});
await Test("unsupported auxiliary disclosed but primary may suggest", async () => {
    var f = new FakeTransport([new(200, "ip=8.8.8.8"), new(200, "ip=8.8.8.8"), new(404, ""), new(200, Geo("8.8.8.8"))]);
    var r = await new EgressDetector(f).DetectAsync(true, default); Check(r.Suggested && r.Code == "suggestion_aux_unavailable");
});
foreach (int status in new[] { 403, 404, 429, 500, 302 })
    await Test("HTTP " + status + " no generic fallback", async () => {
        var f = new FakeTransport([new(status, "")]); var r = await new EgressDetector(f).DetectAsync(false, default);
        Check(!r.Suggested && r.Code == "http_" + status && f.Requests.Count == 1);
    });
await Test("oversized fake response rejected", async () => {
    var r = await new EgressDetector(new FakeTransport([new(200, new string('x', 20000))])).DetectAsync(false, default);
    Check(!r.Suggested && r.Code == "body_too_large");
});
await Test("cancellation has no suggestion", async () => {
    using var c = new CancellationTokenSource(); c.Cancel();
    var r = await new EgressDetector(new FakeTransport([])).DetectAsync(false, c.Token); Check(!r.Suggested && r.Code == "cancelled");
});
await Test("proxy explicit error never falls back direct", async () => {
    try { await new SafeHttpTransport(new FailingProxy()).GetAsync(EgressDetector.Primary, default); }
    catch (GuardException e) { Check(e.Code == "pac_failed", "proxy failure was not preserved"); return; }
    throw new InvalidOperationException("PAC error silently bypassed");
});
await Test("production URL allowlist before proxy or network", async () => {
    try { await new SafeHttpTransport(new FailingProxy()).GetAsync(new("https://example.org/"), default); }
    catch (GuardException e) { Check(e.Code == "endpoint_not_allowed"); return; } throw new InvalidOperationException("arbitrary URL allowed");
});
await Test("case-insensitive child TZ only and node removed", () => Sync(() => {
    var parent = new Dictionary<string,string> { ["tZ"] = "Asia/Tokyo", ["electron_run_as_node"] = "1", ["PATH"] = "safe", ["HTTPS_PROXY"] = "unchanged" };
    var child = ChildEnvironment.Create(parent, "America/New_York");
    Check(child["TZ"] == "America/New_York" && child.Count == 3, "child environment not minimally isolated");
    Check(!child.ContainsKey("ELECTRON_RUN_AS_NODE") && child["HTTPS_PROXY"] == "unchanged" && parent["tZ"] == "Asia/Tokyo");
}));
await Test("desktop identity rejects CLI and filename-only", () => Sync(() => {
    string d = Path.Combine(work, "cli"); Directory.CreateDirectory(d); string exe = Path.Combine(d, "Codex.exe"); File.WriteAllText(exe, "fixture");
    Check(!DesktopIdentity.IsDesktopLayout(exe));
}));
await Test("running guard refuses existing or uncertain process", () => Sync(() => {
    var l = new FakeLauncher(); var c = new CodexCandidate("not-trusted", "fixture");
    Error(() => new LaunchCoordinator(new FakeRunning(RunningState.Running), l).Launch(c, "Asia/Tokyo"), "already_running");
    Error(() => new LaunchCoordinator(new FakeRunning(RunningState.Uncertain), l).Launch(c, "Asia/Tokyo"), "running_check_uncertain");
    Check(l.Calls == 0);
}));
RunningState ObserveRunning(CodexCandidate selected, RunningProcessObservation[] processes, Func<string, bool>? desktop = null) =>
    new ObservedRunningGuard(() => processes, desktop ?? DesktopIdentity.IsDesktopLayout).Check(selected);
var ordinarySelected = new CodexCandidate(Path.Combine(work, "selected", "Codex.exe"), "ordinary fixture");
var storeSelected = new CodexCandidate(Path.Combine(work, "store-current", "ChatGPT.exe"), "Store fixture", CodexDiscovery.StoreFamily);
string otherDesktop = Path.Combine(work, "other-install", "Codex.exe");
await Test("cross-install Store running blocks ordinary selected", () => Sync(() => {
    var state = ObserveRunning(ordinarySelected, [new(Path.Combine(work, "store-old", "ChatGPT.exe"), CodexDiscovery.StoreFamily)]);
    Check(state == RunningState.Running, $"expected Running for independent Store identity, got {state}");
}));
await Test("cross-install ordinary running blocks Store selected", () => Sync(() => {
    var state = ObserveRunning(storeSelected, [new(otherDesktop, null)], path => path == otherDesktop);
    Check(state == RunningState.Running, $"expected Running for validated ordinary desktop, got {state}");
}));
await Test("cross-install different ordinary desktop blocks ordinary selected", () => Sync(() => {
    var state = ObserveRunning(ordinarySelected, [new(otherDesktop, null)], path => path == otherDesktop);
    Check(state == RunningState.Running, $"expected Running for another validated installation, got {state}");
}));
await Test("cross-install older Store version same family blocks current", () => Sync(() => {
    Check(ObserveRunning(storeSelected, [new(Path.Combine(work, "store-old", "ChatGPT.exe"), CodexDiscovery.StoreFamily)]) == RunningState.Running);
}));
await Test("cross-install actual CLI layout stays ignored", () => Sync(() => {
    string path = Path.Combine(work, "cli", "Codex.exe");
    Check(File.Exists(path) && !DesktopIdentity.IsDesktopLayout(path));
    Check(ObserveRunning(storeSelected, [new(path, null)]) == RunningState.Clear);
    Check(ObserveRunning(ordinarySelected, [new(path, null)]) == RunningState.Clear);
}));
await Test("cross-install unrelated ChatGPT package stays ignored", () => Sync(() => {
    var unrelated = new RunningProcessObservation(Path.Combine(work, "chatgpt", "ChatGPT.exe"), "OpenAI.ChatGPT_unrelated");
    Check(ObserveRunning(storeSelected, [unrelated]) == RunningState.Clear);
    Check(ObserveRunning(ordinarySelected, [unrelated]) == RunningState.Clear);
}));
await Test("cross-install inaccessible process stays uncertain", () => Sync(() => {
    Check(ObserveRunning(ordinarySelected, [new(null, null, false)]) == RunningState.Uncertain);
}));
await Test("cross-install ambiguous package query blocks ordinary selected", () => Sync(() => {
    var state = ObserveRunning(ordinarySelected, [new(Path.Combine(work, "ambiguous", "ChatGPT.exe"), null, false)]);
    Check(state == RunningState.Uncertain, $"expected Uncertain for unreadable package identity, got {state}");
}));
await Test("cross-install exact selected target still blocks on unknown package", () => Sync(() => {
    Check(ObserveRunning(ordinarySelected, [new(ordinarySelected.Path.ToUpperInvariant(), null, false)]) == RunningState.Running);
}));
await Test("cross-install known running takes priority over inaccessible process", () => Sync(() => {
    Check(ObserveRunning(ordinarySelected, [new(null, null, false), new(Path.Combine(work, "store-old", "ChatGPT.exe"), CodexDiscovery.StoreFamily)]) == RunningState.Running);
}));
await Test("cross-install missing desktop metadata permission stays uncertain", () => Sync(() => {
    var state = ObserveRunning(ordinarySelected, [new(otherDesktop, null)], _ => throw new UnauthorizedAccessException());
    Check(state == RunningState.Uncertain, $"expected Uncertain for unreadable desktop identity, got {state}");
}));
await Test("manual path cannot bypass desktop identity", () => Sync(() => {
    var l = new FakeLauncher(); Error(() => new LaunchCoordinator(new FakeRunning(RunningState.Clear), l).Launch(new("codex.exe", "CLI"), "Asia/Tokyo"), "invalid_desktop_identity"); Check(l.Calls == 0);
}));
await Test("new detection and cancellation invalidate older updates", () => Sync(() => {
    var e = new DetectionEpoch(); long a = e.Begin(), b = e.Begin(); Check(!e.IsCurrent(a) && e.IsCurrent(b)); e.Invalidate(); Check(!e.IsCurrent(b));
}));
await Test("fixture proves child environment without parent/global changes", async () => {
    string parent = Environment.GetEnvironmentVariable("TZ") ?? "<null>";
    string sys = TimeZoneInfo.Local.Id;
    string? user = Environment.GetEnvironmentVariable("TZ", EnvironmentVariableTarget.User), machine = Environment.GetEnvironmentVariable("TZ", EnvironmentVariableTarget.Machine);
    string config = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent!.Name;
    string exe = Path.Combine(FindRoot(), "tests", "EnvironmentProbe", "bin", config, "net10.0", "EnvironmentProbe.exe");
    var info = ChildEnvironment.StartInfo(exe, "Pacific/Auckland"); info.RedirectStandardOutput = true; info.Environment["GUARD_FIXTURE_MARKER"] = "inherited";
    using var p = Process.Start(info) ?? throw new InvalidOperationException("fixture creation failed"); string output = await p.StandardOutput.ReadToEndAsync(); await p.WaitForExitAsync();
    using var json = JsonDocument.Parse(output); Check(json.RootElement.GetProperty("tz").GetString() == "Pacific/Auckland"); Check(json.RootElement.GetProperty("node").ValueKind == JsonValueKind.Null);
    Check(json.RootElement.GetProperty("marker").GetString() == "inherited" && p.ExitCode == 0);
    Check((Environment.GetEnvironmentVariable("TZ") ?? "<null>") == parent && TimeZoneInfo.Local.Id == sys);
    Check(Environment.GetEnvironmentVariable("TZ", EnvironmentVariableTarget.User) == user && Environment.GetEnvironmentVariable("TZ", EnvironmentVariableTarget.Machine) == machine);
    var independent = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true };
    using var separate = Process.Start(independent)!; using var plain = JsonDocument.Parse(await separate.StandardOutput.ReadToEndAsync()); await separate.WaitForExitAsync();
    Check(plain.RootElement.GetProperty("tz").GetString() == (parent == "<null>" ? null : parent));
});
await Test("HTTP HTTPS named proxy chooses destination scheme", () => Sync(() => {
    var r = ProxyConfiguration.Select("http=127.0.0.1:8080;https=127.0.0.1:8081", null, EgressDetector.Primary);
    Check(r.Kind == ProxyKind.Http && r.Proxy?.Port == 8081, "scheme-aware proxy not selected");
    Check(ProxyConfiguration.Select("127.0.0.1:7890", null, EgressDetector.Primary).Kind == ProxyKind.Http);
}));
await Test("SOCKS-only and proxy credentials are not silently direct", () => Sync(() => {
    Check(ProxyConfiguration.Select("socks=127.0.0.1:1080", null, EgressDetector.Primary).Code == "proxy_unsupported");
    Check(ProxyConfiguration.Select("http://user:secret@127.0.0.1:8080", null, EgressDetector.Primary).Kind == ProxyKind.Error);
}));
await Test("Windows proxy bypass is destination-aware", () => Sync(() => {
    Check(ProxyConfiguration.Select("127.0.0.1:7890", "*.chatgpt.com;chatgpt.com", EgressDetector.Primary).Kind == ProxyKind.Direct);
    Check(ProxyConfiguration.Select("127.0.0.1:7890", "<local>", EgressDetector.Primary).Kind == ProxyKind.Http);
}));
await Test("empty and ambiguous client discovery never guesses", () => Sync(() => {
    Check(CodexDiscovery.Unique([]) is null);
    var a = new CodexCandidate("A", "A"); var b = new CodexCandidate("B", "B");
    Check(CodexDiscovery.Unique([a]) == a, "single valid candidate not selected"); Check(CodexDiscovery.Unique([a,b]) is null);
}));
await Test("real transport contract anonymous GET and fixed target", async () => {
    var handler = new StubHandler((request, token) => {
        Check(request.Method == HttpMethod.Get && request.RequestUri == EgressDetector.Primary);
        Check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ip=8.8.8.8") });
    });
    var response = await new SafeHttpTransport(new DirectProxy(), _ => handler).GetAsync(EgressDetector.Primary, default);
    Check(response.Status == 200 && response.Body == "ip=8.8.8.8");
});
await Test("real transport bounds streaming body", async () => {
    var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new byte[20000]) }));
    try { await new SafeHttpTransport(new DirectProxy(), _ => handler).GetAsync(EgressDetector.Primary, default); }
    catch (GuardException e) { Check(e.Code == "body_too_large", "streaming limit not enforced"); return; } throw new InvalidOperationException("oversized streaming body accepted");
});
await Test("transport timeout includes slow body not only headers", async () => {
    var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NeverEndingStream()) }));
    try { await new SafeHttpTransport(new DirectProxy(), _ => handler, TimeSpan.FromMilliseconds(50)).GetAsync(EgressDetector.Primary, default); }
    catch (GuardException e) { Check(e.Code == "request_timeout", "body timeout not classified"); return; } throw new InvalidOperationException("body deadline absent");
});
await Test("transport cancellation during headers propagates", async () => {
    using var cts = new CancellationTokenSource(50);
    var handler = new StubHandler(async (_, t) => { await Task.Delay(10000, t); return new HttpResponseMessage(HttpStatusCode.OK); });
    try { await new SafeHttpTransport(new DirectProxy(), _ => handler).GetAsync(EgressDetector.Primary, cts.Token); }
    catch (OperationCanceledException) { return; } throw new InvalidOperationException("caller cancellation not preserved");
});
await Test("transport 429 stops without reading error body", async () => {
    var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)429) { Content = new StreamContent(new NeverEndingStream()) }));
    var reply = await new SafeHttpTransport(new DirectProxy(), _ => handler).GetAsync(EgressDetector.Primary, default); Check(reply.Status == 429 && reply.Body == "");
});
await Test("cross-address-family disagreement forces manual", async () => {
    var fake = new FakeTransport([new(200, "ip=8.8.8.8"), new(200, "ip=2606:4700:4700::1111")]);
    var result = await new EgressDetector(fake).DetectAsync(false, default); Check(!result.Suggested && result.Code == "conflicting_egress");
});
await Test("malformed and duplicate geo responses are rejected", () => Sync(() => {
    Error(() => GeoParser.Parse("not json", IPAddress.Parse("8.8.8.8")), "invalid_geo");
    Error(() => GeoParser.Parse("{\"success\":true,\"ip\":\"8.8.8.8\",\"ip\":\"1.1.1.1\"}", IPAddress.Parse("8.8.8.8")), "invalid_geo");
}));
await Test("window fits 1080p at 150 percent and 1366x768", () => Sync(() => {
    Check(WindowPlacement.Fit(1672, 1350, 1920, 1040) == (1672, 1040));
    Check(WindowPlacement.Fit(1672, 1350, 1366, 728) == (1366, 728));
    Check(WindowPlacement.Fit(1100, 862, 2560, 1400) == (1100,862));
}));
Console.WriteLine($"RESULT passed={passed} failed={failed} total={passed + failed}");
int reportIndex = Array.IndexOf(args, "--report");
if (reportIndex >= 0 && reportIndex + 1 < args.Length) { string path = Path.GetFullPath(args[reportIndex + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(new { success = failed == 0, passed, failed, total = passed + failed, results }, new JsonSerializerOptions { WriteIndented = true })); }
return failed == 0 ? 0 : 1;
static string Geo(string ip, string zone = "America/New_York") => JsonSerializer.Serialize(new { success = true, ip, timezone = new { id = zone } });
static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "goal.md"))) d = d.Parent; return d?.FullName ?? throw new InvalidOperationException("repository not found"); }
sealed class FakeTransport(ProbeReply[] responses) : IProbeTransport
{
    private readonly Queue<ProbeReply> replies = new(responses);
    public List<Uri> Requests { get; } = [];
    public Task<ProbeReply> GetAsync(Uri u, CancellationToken c) { c.ThrowIfCancellationRequested(); Requests.Add(u); return Task.FromResult(replies.Dequeue()); }
}
sealed class FailingProxy : IProxyResolver { public ProxyRoute Resolve(Uri u) => new(ProxyKind.Error, null, "pac_failed"); }
sealed class FakeRunning(RunningState state) : IRunningGuard { public RunningState Check(CodexCandidate c) => state; }
sealed class FakeLauncher : IChildLauncher { public int Calls; public int Launch(CodexCandidate c, string z) { Calls++; return 123; } }
sealed class DirectProxy : IProxyResolver { public ProxyRoute Resolve(Uri u) => new(ProxyKind.Direct); }
sealed class StubHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => send(r,c); }
sealed class UnknownLengthContent(byte[] data) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(data).AsTask();
}
sealed class NeverEndingStream : Stream
{
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { } public override int Read(byte[] buffer,int offset,int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(10000, token); return 0; }
    public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer,int offset,int count) => throw new NotSupportedException();
}

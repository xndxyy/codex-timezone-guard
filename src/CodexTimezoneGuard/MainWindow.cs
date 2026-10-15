using System.Text.Json;
using CodexTimezoneGuard.Core;

namespace CodexTimezoneGuard;

/// <summary>Foreground-only launcher. Preview performs no config, discovery, network or launch operations.</summary>
public sealed class MainWindow : Form
{
    private static readonly Color Ink = Color.FromArgb(22, 36, 51), Muted = Color.FromArgb(95, 111, 126), Accent = Color.FromArgb(0, 111, 103), Canvas = Color.FromArgb(242, 245, 248);
    private readonly bool preview;
    private readonly SettingsStore? store;
    private GuardSettings settings = new();
    private readonly DetectionEpoch epoch = new();
    private CancellationTokenSource? detectionCancellation;
    private DetectionResult? latest;
    private DiscoveryResult discovery = new([], "not_checked");
    private readonly RadioButton automatic = new() { Name = "automatic", Text = "自动 · 基于目标域名探测", AutoSize = true };
    private readonly RadioButton manual = new() { Name = "manual", Text = "手动 · 离线可用", AutoSize = true, Checked = true };
    private readonly CheckBox consent = new() { Name = "consent", AutoSize = false, Text = "我已了解并同意上述匿名外部请求（不会发送 Codex 登录信息或聊天内容）", Height = 29 };
    private readonly ComboBox timezone = new() { Name = "timezone", DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, Dock = DockStyle.Fill, IntegralHeight = false, DropDownHeight = 250 };
    private readonly ComboBox client = new() { Name = "client", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button detect = ActionButton("重新检测", false), cancel = ActionButton("取消", false), launch = ActionButton("保存并启动 Codex  →", true), browse = ActionButton("手动定位…", false), refresh = ActionButton("刷新发现", false), reset = ActionButton("重置设置", false), copy = ActionButton("复制脱敏诊断", false);
    private readonly Label status = BodyLabel("尚未检测。手动选择一个 IANA 时区即可离线启动。", Ink);
    private readonly Label clientStatus = BodyLabel("正在准备客户端列表…", Muted);
    private readonly Label systemClock = new(), targetClock = new(), timezoneHint = BodyLabel("请选择地区时区；会按具体日期处理夏令时，不按国家或固定 UTC 偏移猜测。", Muted);
    private readonly System.Windows.Forms.Timer clockTimer = new() { Interval = 1000 };
    private bool busy, applyingSuggestion;

    public MainWindow(bool preview = false)
    {
        this.preview = preview;
        Text = "Codex Timezone Guard · 时区启动器（预发布）";
        Name = "MainWindow"; ClientSize = new(1100, 862); MinimumSize = new(760, 480);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Canvas; ForeColor = Ink;
        Font = new Font("Microsoft YaHei UI", 9.5f); AutoScaleMode = AutoScaleMode.Dpi;
        launch.Name = "launch"; cancel.Enabled = false;
        BuildLayout(); AutoScaleDimensions = new SizeF(96, 96); timezone.Items.AddRange(TimezoneCatalog.All.Cast<object>().ToArray());
        if (!preview)
        {
            store = new(SettingsStore.DefaultDirectory); var loaded = store.Load(); settings = loaded.Settings;
            if (loaded.Corrupt) SetStatus("配置损坏或不可读取，已使用安全默认值；未覆盖原文件。请重新选择后保存。", true);
            automatic.Checked = settings.Mode == "auto"; manual.Checked = !automatic.Checked; consent.Checked = settings.NetworkConsent;
        }
        timezone.Text = settings.Timezone;
        if (automatic.Checked) SetStatus("自动模式尚未取得本轮证据。已保存时区仅供参考，启动前必须重新检测并明确确认。", false);
        UpdateClocks();
        timezone.TextChanged += (_, _) => { UpdateClocks(); if (!applyingSuggestion && automatic.Checked && latest?.Suggested == true) timezoneHint.Text = "你修改了建议值。自动启动将重新探测；要使用这个选择，请切换手动模式。"; };
        manual.CheckedChanged += (_, _) => { if (manual.Checked) { CancelDetection(); SetStatus("手动模式：不会发出网络请求。请自行确认时区与客户端。", false); } };
        automatic.CheckedChanged += (_, _) => { if (automatic.Checked) { latest = null; SetStatus("自动模式：点击重新检测，或在启动前获取新的探测建议。不会静默使用旧结果。", false); } };
        detect.Click += async (_, _) => { if (!preview) await DetectAsync(); };
        cancel.Click += (_, _) => { CancelDetection(); SetStatus("已取消。本轮旧结果不会更新界面，仍可使用手动模式。", false); };
        browse.Click += (_, _) => SelectManualClient(); refresh.Click += async (_, _) => { if (!preview) await DiscoverAsync(); };
        launch.Click += async (_, _) => await SaveAndLaunchAsync(); reset.Click += (_, _) => ResetSettings(); copy.Click += (_, _) => CopyDiagnostics();
        clockTimer.Tick += (_, _) => UpdateClocks(); clockTimer.Start();
        FormClosing += (_, _) => { epoch.Invalidate(); detectionCancellation?.Cancel(); clockTimer.Stop(); };
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            var fit = WindowPlacement.Fit(Width, Height, area.Width, area.Height);
            Size = new(fit.Width, fit.Height);
            Location = new(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        };
        if (preview) { clientStatus.Text = "预览模式 · 不扫描客户端、不访问网络、不读取或写入用户设置"; SetStatus("尚未检测 · 可先手动选择，或同意披露后探测目标域名。", false); }
        else Shown += async (_, _) => await DiscoverAsync();
    }
    private void BuildLayout()
    {
        var viewport = new Panel { Name = "content", Dock = DockStyle.Fill, AutoScroll = true, BackColor = Canvas };
        var root = new TableLayoutPanel { Name = "layout", Dock = DockStyle.Top, Height = 820, ColumnCount = 1, RowCount = 7, Padding = new(26, 20, 26, 12), BackColor = Canvas };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int h in new[] { 94, 88, 252, 112, 132, 67, 43 }) root.RowStyles.Add(new(SizeType.Absolute, h)); viewport.Controls.Add(root); Controls.Add(viewport);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Ink, Padding = new(18, 9, 18, 8), Margin = new(0, 0, 0, 10) };
        header.ColumnStyles.Add(new(SizeType.Percent, 85)); header.ColumnStyles.Add(new(SizeType.Percent, 15));
        header.RowStyles.Add(new(SizeType.Absolute, 19)); header.RowStyles.Add(new(SizeType.Absolute, 32)); header.RowStyles.Add(new(SizeType.Absolute, 22));
        header.Controls.Add(new Label { Text = "CODEX  /  TIMEZONE GUARD", ForeColor = Color.FromArgb(107, 219, 198), Font = new("Segoe UI", 9, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        header.Controls.Add(new Label { Text = "让 Codex 使用你选择的时区", ForeColor = Color.White, Font = new("Microsoft YaHei UI", 18, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 1);
        header.Controls.Add(new Label { Text = "只影响本次新启动的 Codex 及其子进程 · 不改 Windows 时区或全局环境", ForeColor = Color.FromArgb(191, 205, 216), Dock = DockStyle.Fill, Font = new(Font.FontFamily, 9) }, 0, 2);
        var badge = new Label { Text = "v" + AppVersion.Value + "\n预发布 · 未签名", ForeColor = Color.FromArgb(191, 205, 216), TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill, Font = new(Font.FontFamily, 9) };
        header.Controls.Add(badge, 1, 0); header.SetRowSpan(badge, 3); root.Controls.Add(header, 0, 0);
        var clocks = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0, 0, 0, 10) };
        clocks.ColumnStyles.Add(new(SizeType.Percent, 50)); clocks.ColumnStyles.Add(new(SizeType.Percent, 50));
        clocks.Controls.Add(ClockCard("WINDOWS · 保持不变", systemClock, false), 0, 0); clocks.Controls.Add(ClockCard("目标时区 · 启动前可见", targetClock, true), 1, 0); root.Controls.Add(clocks, 0, 1);
        var network = Card("01   网络证据 / 自动建议", [31, 35, 49, 48, 33, 43]);
        var modeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new(0), Padding = new(0, 2, 0, 0) };
        manual.Margin = new(0, 3, 22, 0); automatic.Margin = new(0, 3, 20, 0); detect.Margin = new(0, 0, 8, 0); cancel.Margin = new(0);
        modeRow.Controls.AddRange([manual, automatic, detect, cancel]); network.Controls.Add(modeRow, 0, 1);
        status.Padding = new(9, 6, 8, 4); status.BackColor = Color.FromArgb(235, 246, 244); status.Dock = DockStyle.Fill; network.Controls.Add(status, 0, 2);
        var disclosure = BodyLabel("联网披露：chatgpt.com（重复两次）及 api.openai.com 会接收无认证 trace 请求；ipwho.is 会收到明确观察到的公网 IP 与查询出口 IP。免费接口可能拒绝或限流；不跟随重定向、不绕过 TLS。", Muted); disclosure.Dock = DockStyle.Fill; disclosure.Padding = new(0, 6, 0, 0); network.Controls.Add(disclosure, 0, 3);
        consent.Dock = DockStyle.Fill; network.Controls.Add(consent, 0, 4);
        var caveat = BodyLabel("重要：这是目标域名探测推断，不证明真实 Codex 的全部出口。只读使用系统代理/PAC/TUN；代理环境变量、按进程规则可能不同，SOCKS-only 不支持。", Muted); caveat.Dock = DockStyle.Fill; network.Controls.Add(caveat, 0, 5); root.Controls.Add(network, 0, 2);
        var time = Card("02   IANA 地区时区 / 支持搜索与夏令时", [31, 33, 27]);
        time.Controls.Add(timezone, 0, 1); timezoneHint.Dock = DockStyle.Fill; time.Controls.Add(timezoneHint, 0, 2); root.Controls.Add(time, 0, 3);
        var target = Card("03   Codex 桌面客户端 / 不支持同名 CLI", [31, 34, 45]);
        var targetRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new(0) };
        targetRow.ColumnStyles.Add(new(SizeType.Percent, 100)); targetRow.ColumnStyles.Add(new(SizeType.Absolute, 115)); targetRow.ColumnStyles.Add(new(SizeType.Absolute, 103));
        targetRow.Controls.Add(client, 0, 0); targetRow.Controls.Add(browse, 1, 0); targetRow.Controls.Add(refresh, 2, 0); target.Controls.Add(targetRow, 0, 1);
        clientStatus.Dock = DockStyle.Fill; clientStatus.Padding = new(0, 5, 0, 0); target.Controls.Add(clientStatus, 0, 2); root.Controls.Add(target, 0, 4);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new(0, 10, 0, 7), Margin = new(0) };
        actions.ColumnStyles.Add(new(SizeType.Absolute, 146)); actions.ColumnStyles.Add(new(SizeType.Absolute, 115)); actions.ColumnStyles.Add(new(SizeType.Percent, 100)); actions.ColumnStyles.Add(new(SizeType.Absolute, 274));
        copy.Dock = reset.Dock = launch.Dock = DockStyle.Fill; actions.Controls.Add(copy, 0, 0); actions.Controls.Add(reset, 1, 0); actions.Controls.Add(launch, 3, 0); root.Controls.Add(actions, 0, 5);
        var footer = BodyLabel("现有 Codex 不会被结束或重启；请自行保存工作并退出后启动。创建进程 ≠ 时区已验证。\nStore/实际 Codex 的运行时兼容性尚未验证 · 不常驻托盘 · 无遥测、服务或开机启动", Muted); footer.Dock = DockStyle.Fill; footer.Font = new(Font.FontFamily, 8.5f); root.Controls.Add(footer, 0, 6);
    }
    private static Button ActionButton(string text, bool primary) => new() { Text = text, AutoSize = false, Width = primary ? 260 : 100, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = new(4, 0, 0, 0), UseVisualStyleBackColor = false };
    private static Label BodyLabel(string text, Color color) => new() { Text = text, ForeColor = color, AutoSize = false, Margin = new(0), Font = new("Microsoft YaHei UI", 9) };
    private static TableLayoutPanel Card(string title, int[] rows)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = 1, RowCount = rows.Length, Padding = new(15, 0, 15, 0), Margin = new(0, 0, 0, 10) };
        foreach (int row in rows) card.RowStyles.Add(new(SizeType.Absolute, row));
        card.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new("Microsoft YaHei UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink, Margin = new(0) }, 0, 0); return card;
    }
    private static TableLayoutPanel ClockCard(string title, Label value, bool target)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White, Padding = new(15, 8, 15, 7), Margin = target ? new(5, 0, 0, 0) : new(0, 0, 5, 0) };
        panel.RowStyles.Add(new(SizeType.Absolute, 24)); panel.RowStyles.Add(new(SizeType.Percent, 100));
        value.Dock = DockStyle.Fill; value.Margin = new(0); value.TextAlign = ContentAlignment.MiddleLeft; value.Font = new("Consolas", 15, FontStyle.Bold); value.ForeColor = target ? Accent : Ink;
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Muted, Font = new("Microsoft YaHei UI", 9), Margin = new(0) }, 0, 0); panel.Controls.Add(value, 0, 1); return panel;
    }
    private void UpdateClocks()
    {
        systemClock.Text = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        targetClock.Text = TimezoneCatalog.TryGet(timezone.Text, out _) ? TimezoneCatalog.Preview(timezone.Text, DateTimeOffset.UtcNow) : "请选择有效的 IANA ID";
    }
    private void SetStatus(string message, bool error) { status.Text = message; status.BackColor = error ? Color.FromArgb(255, 241, 229) : Color.FromArgb(235, 246, 244); }
    private void SetBusy(bool value)
    {
        busy = value; launch.Enabled = detect.Enabled = browse.Enabled = refresh.Enabled = reset.Enabled = !value;
        manual.Enabled = automatic.Enabled = timezone.Enabled = client.Enabled = consent.Enabled = !value; cancel.Enabled = value && detectionCancellation is not null; UseWaitCursor = value;
    }
    private bool EnsureConsent()
    {
        if (consent.Checked) return true;
        const string text = "将访问固定 HTTPS 目标 chatgpt.com、api.openai.com 的匿名 trace 端点，并将观察到的公网 IP 交给 ipwho.is 查询 IANA 时区。各服务会看到网络请求和相应出口 IP。\n\n不发送 Codex 登录令牌、Cookie、聊天内容或 Windows 凭据；不保存原始 IP。接口可能失败，探测并不证明真实 Codex 的全部流量出口。代理环境变量与系统代理可能不同，本工具只读使用系统代理/PAC/TUN；SOCKS-only 不支持。\n\n是否同意本次匿名外部请求？不同意仍可离线手动选择。";
        if (MessageBox.Show(this, text, "首次联网与第三方披露", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
        consent.Checked = true; return true;
    }
    private async Task<DetectionResult?> DetectAsync()
    {
        if (preview || busy || !EnsureConsent()) return null;
        automatic.Checked = true;
        CancelDetection(); var cts = new CancellationTokenSource(); detectionCancellation = cts; long version = epoch.Begin();
        latest = null; SetBusy(true); SetStatus("检测中：只读解析系统代理/PAC → 目标域名重复探测 → 明确 IP 定位。可随时取消。", false);
        try
        {
            var result = await new EgressDetector(new SafeHttpTransport(new WindowsProxyResolver())).DetectAsync(true, cts.Token);
            if (IsDisposed || !epoch.IsCurrent(version)) return null; latest = result;
            if (result.Suggested && result.Timezone is not null)
            {
                applyingSuggestion = true; timezone.Text = result.Timezone; applyingSuggestion = false;
                timezoneHint.Text = "本轮建议：" + result.Timezone + " · IANA 规则随日期处理夏令时。启动前将重新检测并让你确认。";
                SetStatus($"基于目标域名探测的建议 · {result.Observations} 次一致观察 · {result.AddressFamily}\n{result.Timezone}" + (result.Code == "suggestion_aux_unavailable" ? " · 辅助端点不可用，仅主目标重复证据" : " · 主目标与辅助端点一致"), false);
            }
            else SetStatus("无法形成自动建议：" + SafeMessages.ForCode(result.Code) + "\n请重试或切换手动模式，不会套用固定地区或旧缓存。", true);
            return result;
        }
        catch { if (!IsDisposed && epoch.IsCurrent(version)) SetStatus("检测未完成。未保存响应或异常原文，请使用手动模式。", true); return null; }
        finally { if (!IsDisposed && epoch.IsCurrent(version)) SetBusy(false); if (ReferenceEquals(detectionCancellation, cts)) detectionCancellation = null; cts.Dispose(); }
    }
    private void CancelDetection() { epoch.Invalidate(); detectionCancellation?.Cancel(); latest = null; if (!IsDisposed) SetBusy(false); }
    private async Task DiscoverAsync()
    {
        if (preview) return; refresh.Enabled = browse.Enabled = false;
        try
        {
            var found = await Task.Run(CodexDiscovery.Discover); if (IsDisposed) return;
            discovery = found; client.Items.Clear(); client.Items.AddRange(found.Candidates.Cast<object>().ToArray());
            var saved = found.Candidates.FirstOrDefault(c => settings.PackageFamily is not null ? c.PackageFamily == settings.PackageFamily : c.Path.Equals(settings.ClientPath, StringComparison.OrdinalIgnoreCase));
            if (saved is null && settings.PackageFamily is null && settings.ClientPath is not null) saved = await Task.Run(() => CodexDiscovery.ValidateManual(settings.ClientPath));
            if (IsDisposed) return; if (saved is not null && !client.Items.Contains(saved)) client.Items.Add(saved);
            if (saved is not null && found.Candidates.Count <= 1) client.SelectedItem = saved;
            else if (CodexDiscovery.Unique(found.Candidates) is { } single) client.SelectedItem = single;
            clientStatus.Text = found.Candidates.Count == 0 && saved is null ? "未发现已验证桌面客户端。可手动选择安装目录中的 EXE；同名 CLI 不能通过验证。" : found.Candidates.Count > 1 ? "发现多个候选，请明确选择。启动前还会检查运行中实例与当前包身份。" : "已验证桌面资源/标识；Store 启动还将以挂起进程核对包身份。实际时区运行时仍未验证。";
            if (found.Code == "discovery_partial") clientStatus.Text += " 部分包发现受限，不会修复 ACL 或自动提权。";
        }
        catch { if (!IsDisposed) clientStatus.Text = "客户端发现受限。请手动定位；不会更改系统权限或提升管理员。"; }
        finally { if (!IsDisposed) refresh.Enabled = browse.Enabled = !busy; }
    }
    private void SelectManualClient()
    {
        if (preview || busy) return;
        using var dialog = new OpenFileDialog { Title = "选择 Codex 桌面安装目录中的 EXE（不是 CLI）", Filter = "应用程序 (*.exe)|*.exe", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var selected = CodexDiscovery.ValidateManual(dialog.FileName);
            if (selected is null) { MessageBox.Show(this, "无法验证为 Codex 桌面客户端。必须具有预期 Electron 资源和产品标识；Store 必须有当前用户注册的准确包身份。手动选择不会绕过验证。", "客户端不受信任", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!client.Items.Contains(selected)) client.Items.Add(selected); client.SelectedItem = selected; clientStatus.Text = "手动路径已验证；启动前重新核验。不会修改或读取客户端账户数据。";
        }
        catch { MessageBox.Show(this, "选择路径不可读取或身份不明确。请确认安装渠道；不会自动提升权限或修复 ACL。", "无法选择"); }
    }
    private async Task SaveAndLaunchAsync()
    {
        if (preview || busy) return;
        if (client.SelectedItem is not CodexCandidate candidate) { SetStatus("请先选择一个已验证的 Codex 桌面客户端。没有候选时，可手动定位；不会猜测 CLI。", true); return; }
        string? zone;
        if (automatic.Checked) { var evidence = await DetectAsync(); if (evidence?.Suggested != true) return; zone = evidence.Timezone; }
        else zone = timezone.Text;
        if (!TimezoneCatalog.TryGet(zone, out _)) { SetStatus("请输入有效的 IANA 地区时区，例如 Asia/Shanghai、America/Phoenix。", true); return; }
        string warning = $"本次新启动的 Codex 及其后代将继承：\n\nTZ = {zone}\n\nWindows 时区、用户/机器环境和独立应用不变。\n已有 Codex 请先保存工作并自行退出，本工具绝不结束现有进程。\n\n创建进程不等于 Codex 全部组件已经采用该时区；实际运行时尚未验证。是否保存并启动？";
        if (MessageBox.Show(this, warning, "确认本次启动时区", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        SetBusy(true);
        try
        {
            candidate = await Task.Run(() => CodexDiscovery.ResolveCurrent(candidate));
            settings = new(automatic.Checked ? "auto" : "manual", zone!, consent.Checked, candidate.PackageFamily is null ? candidate.Path : null, candidate.PackageFamily); store!.Save(settings);
            int pid = await Task.Run(() => new LaunchCoordinator(new WindowsRunningGuard(), new WindowsChildLauncher()).Launch(candidate, zone!));
            SetStatus($"设置已保存，目标进程已创建（PID {pid}）。TZ 已传入本次子进程环境，但真实 Codex 日期运行时尚未验证。", false);
        }
        catch (GuardException e) { SetStatus("未完成启动：" + SafeMessages.ForCode(e.Code), true); }
        catch { SetStatus("未完成启动：发生受控错误。未记录路径、环境或异常原文；请复制脱敏诊断。", true); }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    private void ResetSettings()
    {
        if (preview || busy || MessageBox.Show(this, "只删除启动器自己的设置；不更改 Windows、Codex 数据或代理。下次联网需要重新同意。", "重置设置", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try { store!.Reset(); settings = new(); manual.Checked = true; consent.Checked = false; timezone.Text = settings.Timezone; latest = null; SetStatus("启动器设置已重置。没有系统设置需要恢复，因为从未更改系统设置。", false); }
        catch (GuardException e) { SetStatus(SafeMessages.ForCode(e.Code), true); }
    }
    private void CopyDiagnostics()
    {
        try { Clipboard.SetText(JsonSerializer.Serialize(DiagnosticReport.Snapshot(latest, discovery), new JsonSerializerOptions { WriteIndented = true })); SetStatus("脱敏诊断已复制：不含原始 IP、代理地址/凭据、客户端完整路径、响应体或账号信息。", false); }
        catch { SetStatus("剪贴板当前不可用。可稍后重试；未写入其他日志。", true); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { clockTimer.Dispose(); epoch.Invalidate(); detectionCancellation?.Cancel(); }
        base.Dispose(disposing);
    }
}

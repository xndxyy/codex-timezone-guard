using System.Text.Json;
using CodexTimezoneGuard.Core;

namespace CodexTimezoneGuard;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--diagnose")
        {
            try { return DiagnosticReport.DiagnoseAsync(args[1]).GetAwaiter().GetResult(); } catch { return 1; }
        }
        if (args.Length == 2 && args[0] == "--self-test") return SelfTest(args[1]);
        if (args.Length == 2 && args[0] == "--render-preview")
        {
            using var form = new MainWindow(preview: true); form.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!); bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); return 0;
        }
        if (args.Length != 0) return 2;
        using var mutex = new Mutex(true, @"Local\CodexTimezoneGuard-Desktop-v1", out bool owns);
        if (!owns) { MessageBox.Show("启动器已打开，请使用已有窗口。", "Codex Timezone Guard"); return 1; }
        try { Application.Run(new MainWindow()); } finally { mutex.ReleaseMutex(); }
        return 0;
    }
    private static int SelfTest(string path)
    {
        var checks = new Dictionary<string, bool>(); var layout = new List<object>();
        try
        {
            using var form = new MainWindow(preview: true); form.CreateControl();
            checks["ui_has_timezone_selector"] = form.Controls.Find("timezone", true).Length == 1;
            checks["ui_has_manual_offline_mode"] = form.Controls.Find("manual", true).Length == 1;
            checks["ui_has_launch_action"] = form.Controls.Find("launch", true).Length == 1;
            checks["ui_has_privacy_disclosure"] = form.Controls.Find("consent", true).Length == 1;
            checks["iana_and_dst"] = TimezoneCatalog.TryGet("America/New_York", out var z) && z.GetUtcOffset(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)) == TimeSpan.FromHours(-4);
            checks["public_address_policy"] = !PublicAddress.TryParse("127.0.0.1", out _) && PublicAddress.TryParse("8.8.8.8", out _);
            checks["child_environment_isolation"] = ChildEnvironment.Create(new Dictionary<string, string> { ["TZ"] = "Asia/Tokyo", ["ELECTRON_RUN_AS_NODE"] = "1" }, "Europe/London") is var env && env["TZ"] == "Europe/London" && env.Count == 1;
            form.Show(); Application.DoEvents();
            form.MinimumSize = Size.Empty; form.Size = new Size(1366, 728); Application.DoEvents();
            checks["small_screen_scrolls_to_launch"] = form.Controls.Find("content", true).FirstOrDefault() is ScrollableControl content && content.AutoScroll && content.VerticalScroll.Visible;
            if (form.Controls.Find("content", true).FirstOrDefault() is ScrollableControl scroller)
            {
                var launchButton = form.Controls.Find("launch", true)[0]; scroller.AutoScrollPosition = new Point(0, scroller.VerticalScroll.Maximum); Application.DoEvents();
                var position = scroller.PointToClient(launchButton.PointToScreen(Point.Empty));
                checks["launch_reachable_after_scroll"] = scroller.ClientRectangle.Contains(new Rectangle(position, launchButton.Size));
                layout.Add(new { viewport = scroller.ClientSize.ToString(), launchPosition = position.ToString(), launchSize = launchButton.Size.ToString(), scroll = scroller.AutoScrollPosition.ToString(), maximum = scroller.VerticalScroll.Maximum });
            }
            checks["offline_without_consent"] = form.Controls.Find("manual", true).FirstOrDefault() is RadioButton manual && manual.Checked && form.Controls.Find("consent", true).FirstOrDefault() is CheckBox consent && !consent.Checked && form.Controls.Find("launch", true)[0].Enabled;
            if (form.Controls.Find("timezone", true).FirstOrDefault() is ComboBox timezone && form.Controls.Find("automatic", true).FirstOrDefault() is RadioButton automatic && form.Controls.Find("manual", true).FirstOrDefault() is RadioButton offline)
            {
                timezone.Text = "Asia/Tokyo"; automatic.Checked = true; offline.Checked = true;
                checks["switch_to_manual_preserves_selected_timezone"] = timezone.Text == "Asia/Tokyo" && timezone.Enabled && !((CheckBox)form.Controls.Find("consent", true)[0]).Checked;
            }
            var lifecycle = new LaunchLifecycle();
            using var closingForm = new MainWindow(preview: true, launchLifecycle: lifecycle);
            closingForm.Show(); Application.DoEvents();
            var finishLaunch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task fakeLaunch = lifecycle.RunAsync(() => finishLaunch.Task);
            closingForm.Close(); Application.DoEvents();
            checks["normal_close_vetoed_during_launch"] = !closingForm.IsDisposed && closingForm.Visible;
            finishLaunch.SetResult(); fakeLaunch.GetAwaiter().GetResult();
            closingForm.Close(); Application.DoEvents();
            checks["normal_close_allowed_after_launch_cleanup"] = lifecycle.CanCloseNormally && closingForm.IsDisposed;
            using var idleClosingForm = new MainWindow(preview: true);
            idleClosingForm.Show(); Application.DoEvents(); idleClosingForm.Close(); Application.DoEvents();
            checks["normal_close_allowed_without_launch"] = idleClosingForm.IsDisposed;
        }
        catch { checks["unhandled_failure"] = false; }
        bool success = checks.Count > 0 && checks.Values.All(v => v);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { success, passed = checks.Values.Count(v => v), failed = checks.Values.Count(v => !v), checks, layout, offline = true, realCodexVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}

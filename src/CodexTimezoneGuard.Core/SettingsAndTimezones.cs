using System.Globalization;
using System.Text.Json;

namespace CodexTimezoneGuard.Core;

/// <summary>Only stable, non-secret error codes cross the presentation boundary.</summary>
public sealed class GuardException(string code) : Exception(code) { public string Code { get; } = code; }

public static class TimezoneCatalog
{
    private static readonly Lazy<IReadOnlyList<string>> Zones = new(() =>
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal) { "Etc/UTC", "UTC" };
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            if (zone.HasIanaId) ids.Add(zone.Id);
            else if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana)) ids.Add(iana);
        }
        return ids.ToArray();
    });
    public static IReadOnlyList<string> All => Zones.Value;
    public static bool TryGet(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrEmpty(id) || id.Length > 100 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '+'))) return false;
        // Do not accept Windows display names, country names, or guessed numeric offsets.
        if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _)) return false;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
    public static string Preview(string id, DateTimeOffset instant)
    {
        if (!TryGet(id, out var zone)) throw new GuardException("invalid_timezone");
        return TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }
}

public sealed record GuardSettings(string Mode = "manual", string Timezone = "Etc/UTC", bool NetworkConsent = false, string? ClientPath = null, string? PackageFamily = null);
public sealed record SettingsLoad(GuardSettings Settings, bool Corrupt = false);
public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };
    public string FilePath => Path.Combine(directory, "settings.json");
    public static string DefaultDirectory => Environment.GetEnvironmentVariable("CODEX_TZ_GUARD_DATA_DIR") is { Length: > 0 } relocated
        ? Path.GetFullPath(relocated)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexTimezoneGuard");
    public SettingsLoad Load()
    {
        if (!File.Exists(FilePath)) return new(new());
        try
        {
            if (new FileInfo(FilePath).Length > 16384) return new(new(), true);
            var settings = JsonSerializer.Deserialize<GuardSettings>(File.ReadAllText(FilePath), JsonOptions);
            if (settings is null) return new(new(), true);
            Validate(settings);
            return new(settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or GuardException or ArgumentException)
        { return new(new(), true); }
    }
    public void Save(GuardSettings settings)
    {
        Validate(settings);
        string temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(data); stream.Flush(true); }
            // Same-directory rename/replacement: readers see the old or complete new document.
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new GuardException("settings_save_failed"); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public void Reset()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new GuardException("settings_reset_failed"); }
    }
    private static void Validate(GuardSettings settings)
    {
        if (!TimezoneCatalog.TryGet(settings.Timezone, out _)) throw new GuardException("invalid_timezone");
        if (settings.Mode is not ("manual" or "auto") || settings.ClientPath?.Length > 2048 || settings.PackageFamily?.Length > 255) throw new GuardException("invalid_settings");
    }
}

/// <summary>A monotonically increasing token prevents cancelled or older UI tasks publishing results.</summary>
public sealed class DetectionEpoch
{
    private long epoch;
    public long Begin() => Interlocked.Increment(ref epoch);
    public bool IsCurrent(long version) => Interlocked.Read(ref epoch) == version;
    public void Invalidate() => Interlocked.Increment(ref epoch);
}

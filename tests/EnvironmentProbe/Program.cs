using System.Text.Json;
Console.WriteLine(JsonSerializer.Serialize(new {
    tz = Environment.GetEnvironmentVariable("TZ"),
    node = Environment.GetEnvironmentVariable("ELECTRON_RUN_AS_NODE"),
    marker = Environment.GetEnvironmentVariable("GUARD_FIXTURE_MARKER"),
    pid = Environment.ProcessId,
    systemZone = TimeZoneInfo.Local.Id
}));

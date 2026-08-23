using FreeFlight.CabinControl.Core.Configuration;
using FreeFlight.CabinControl.Core.Content;
using FreeFlight.CabinControl.Core.Persistence;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Settings round-trip", SettingsRoundTripAsync),
    ("Valid airline pack", ValidAirlinePackAsync),
    ("Traversal asset rejected", TraversalAssetRejectedAsync),
    ("Executable asset rejected", ExecutableAssetRejectedAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} checks passed.");
return failures.Count == 0 ? 0 : 1;

static async Task SettingsRoundTripAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"freeflight-tests-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "settings.json");
    var store = new JsonSettingsStore(path);

    try
    {
        var expected = new AppSettings
        {
            UserDisplayName = "Test User",
            MasterVolume = 63,
            AudioOutputDeviceId = "test-endpoint",
            AudioOutputDeviceName = "Test speakers",
            ActiveAirlinePackId = "test.airline",
            ActiveAirlineId = "custom.tst",
            CustomAirlineProfiles =
            [
                new CustomAirlineProfileSettings
                {
                    Id = "custom.tst",
                    Name = "Test Virtual",
                    Icao = "TST",
                    SoundPackName = "Test pack"
                }
            ]
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        AssertEqual("Test User", actual.UserDisplayName, "Display name was not persisted.");
        AssertEqual(63, actual.MasterVolume, "Master volume was not persisted.");
        AssertEqual("test-endpoint", actual.AudioOutputDeviceId, "Audio endpoint id was not persisted.");
        AssertEqual("test.airline", actual.ActiveAirlinePackId, "Airline pack id was not persisted.");
        AssertEqual("custom.tst", actual.ActiveAirlineId, "Active airline id was not persisted.");
        AssertEqual("Test Virtual", actual.CustomAirlineProfiles.Single().Name, "Custom airline was not persisted.");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}

static Task ValidAirlinePackAsync()
{
    var manifest = CreateManifest([
        new AirlinePackAsset { Type = "announcement", Path = "audio/boarding.ogg", Licence = "Created by pack author" }
    ]);
    var result = new AirlinePackValidator().Validate(manifest, Path.Combine(Path.GetTempPath(), "pack"));
    Assert(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    return Task.CompletedTask;
}

static Task TraversalAssetRejectedAsync()
{
    var manifest = CreateManifest([
        new AirlinePackAsset { Type = "video", Path = "../outside.mp4", Licence = "Test" }
    ]);
    var result = new AirlinePackValidator().Validate(manifest, Path.Combine(Path.GetTempPath(), "pack"));
    Assert(!result.IsValid, "A path outside the pack directory was accepted.");
    return Task.CompletedTask;
}

static Task ExecutableAssetRejectedAsync()
{
    var manifest = CreateManifest([
        new AirlinePackAsset { Type = "unknown", Path = "payload.exe", Licence = "Test" }
    ]);
    var result = new AirlinePackValidator().Validate(manifest, Path.Combine(Path.GetTempPath(), "pack"));
    Assert(!result.IsValid, "An executable asset was accepted.");
    return Task.CompletedTask;
}

static AirlinePackManifest CreateManifest(IReadOnlyList<AirlinePackAsset> assets) => new()
{
    SchemaVersion = AirlinePackManifest.CurrentSchemaVersion,
    Id = "test.airline",
    Version = "1.0.0",
    DisplayName = "Test Airline",
    Author = "Test Author",
    Licence = "Test metadata",
    AircraftAdapters = ["generic.preview"],
    Assets = assets
};

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }
}

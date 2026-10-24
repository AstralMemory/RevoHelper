using System.Text.Json;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Monitoring;

public sealed class MonitorProgramStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "RevoHelper.MonitorProgram.Tests." + Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(directory, "monitor-programs.json");
    private static readonly DateTimeOffset Added = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Selected = Added.AddMinutes(10);

    [Fact]
    public void MissingStore_LoadsEmpty_WithoutCreatingRecordsOrFiles()
    {
        var store = new MonitorProgramStore(StorePath);

        Assert.Empty(store.Load());
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void ExplicitAdd_PersistsOnlyChosenProgramAndActionTimes()
    {
        var store = new MonitorProgramStore(StorePath);

        var entry = store.Add(@"C:\Games\Minecraft\javaw.exe", Added);
        var loaded = Assert.Single(new MonitorProgramStore(StorePath).Load());

        Assert.Equal(entry, loaded);
        Assert.Equal("javaw.exe", loaded.DisplayName);
        Assert.Equal(Added, loaded.AddedAtUtc);
        Assert.Null(loaded.LastSelectedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Id));
        using var document = JsonDocument.Parse(File.ReadAllText(StorePath));
        Assert.Equal(new[] { "AddedAtUtc", "DisplayName", "ExecutablePath", "Id", "LastSelectedAtUtc" },
            document.RootElement[0].EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public void DuplicateFullPath_ReusesIdAndOriginalActionTimes()
    {
        var store = new MonitorProgramStore(StorePath);
        var original = store.Add(@"C:\Games\Minecraft\javaw.exe", Added);
        store.MarkSelected(original.Id, Selected);

        var duplicate = store.Add(@"c:\GAMES\Minecraft\sub\..\JAVAW.EXE", Selected.AddHours(1));

        Assert.Equal(original.Id, duplicate.Id);
        Assert.Equal(Added, duplicate.AddedAtUtc);
        Assert.Equal(Selected, duplicate.LastSelectedAtUtc);
        Assert.Single(store.Load());
    }

    [Fact]
    public void MarkSelected_UpdatesOnlySelectedRecord_AndKeepsAddedTime()
    {
        var store = new MonitorProgramStore(StorePath);
        var first = store.Add(@"C:\Games\game.exe", Added);
        var second = store.Add(@"C:\Audio\MiniMeters.exe", Added.AddMinutes(1));

        var selected = store.MarkSelected(first.Id, Selected);

        Assert.Equal(Selected, selected!.LastSelectedAtUtc);
        Assert.Equal(Added, selected.AddedAtUtc);
        Assert.Null(store.Load().Single(entry => entry.Id == second.Id).LastSelectedAtUtc);
        Assert.Null(store.MarkSelected("not-saved", Selected));
        Assert.Equal(2, store.Load().Count);
    }

    [Fact]
    public void Remove_DeletesOnlyRecord_NeverExecutable()
    {
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "chosen.exe");
        File.WriteAllText(executable, "unit test marker, never executed");
        var store = new MonitorProgramStore(StorePath);
        var entry = store.Add(executable, Added);

        store.Remove(entry.Id);
        store.Remove("not-saved");

        Assert.Empty(store.Load());
        Assert.Equal("unit test marker, never executed", File.ReadAllText(executable));
    }

    [Theory]
    [InlineData("{broken JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    public void CorruptOrWrongShapeStore_LoadsEmptyWithoutDestructiveRewrite(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(StorePath, json);

        Assert.Empty(new MonitorProgramStore(StorePath).Load());
        Assert.Equal(json, File.ReadAllText(StorePath));
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData(@"C:\Games\game.bat")]
    [InlineData(@"C:\Games\game.exe:alternate.exe")]
    [InlineData(@"C:\Games\*.exe")]
    [InlineData("C:\\Games\\bad\u0000.exe")]
    public void Add_RejectsUnsafeOrNonExecutablePaths(string path)
    {
        var store = new MonitorProgramStore(StorePath);

        Assert.Throws<ArgumentException>(() => store.Add(path, Added));
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void Store_IsBoundedAt64Records_WithoutDroppingOlderChoices()
    {
        var store = new MonitorProgramStore(StorePath);
        for (var index = 0; index < 64; index++) store.Add($@"C:\Games\game{index}.exe", Added.AddMinutes(index));

        Assert.Throws<InvalidOperationException>(() => store.Add(@"C:\Games\extra.exe", Added.AddDays(1)));
        Assert.Equal(64, store.Load().Count);
        Assert.Equal("game0.exe", store.Load()[0].DisplayName);
    }

    [Fact]
    public void Load_KeepsMissingProgramRecord_AndNormalizesDatesToUtc()
    {
        var store = new MonitorProgramStore(StorePath);
        var entry = store.Add(@"C:\Missing\not-running.exe", Added.ToOffset(TimeSpan.FromHours(8)));

        var loaded = Assert.Single(store.Load());

        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal(TimeSpan.Zero, loaded.AddedAtUtc.Offset);
        Assert.Equal(Added, loaded.AddedAtUtc);
    }

    [Fact]
    public void AtomicSave_DoesNotLeaveTemporaryFiles()
    {
        new MonitorProgramStore(StorePath).Add(@"C:\Games\game.exe", Added);

        Assert.Equal(new[] { StorePath }, Directory.GetFiles(directory));
    }

    [Fact]
    public void Load_FiltersInvalidAndDuplicateRecords_AndBoundsImportedList()
    {
        Directory.CreateDirectory(directory);
        var entries = new List<MonitorProgramEntry?>
        {
            null,
            new("relative", "game.exe", "invalid relative path", Added),
            new("first", @"C:\Games\game.exe", "a window title must not be retained", Added),
            new("duplicate-path", @"c:\GAMES\GAME.EXE", "duplicate", Added),
            new("first", @"C:\Games\other.exe", "duplicate ID", Added)
        };
        entries.AddRange(Enumerable.Range(0, 70).Select(index =>
            new MonitorProgramEntry("entry" + index, $@"C:\Games\extra{index}.exe", "extra", Added)));
        File.WriteAllText(StorePath, JsonSerializer.Serialize(entries));

        var loaded = new MonitorProgramStore(StorePath).Load();

        Assert.Equal(64, loaded.Count);
        Assert.Equal("game.exe", loaded[0].DisplayName);
        Assert.Equal(1, loaded.Count(entry => entry.ExecutablePath.Equals(@"C:\Games\game.exe", StringComparison.OrdinalIgnoreCase)));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

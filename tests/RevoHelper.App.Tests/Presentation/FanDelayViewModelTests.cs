using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Fans;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class FanDelayViewModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void TransitionDelayPreservesMillisecondsAndIncludesHalfPercentUnit(int milliseconds)
    {
        var viewModel = CreateViewModel();

        viewModel.FanTransitionSensitivity = milliseconds;

        Assert.Equal(milliseconds, viewModel.FanTransitionSensitivity);
        Assert.Equal($"{milliseconds} ms/0.5%", viewModel.FanTransitionSensitivityText);
    }

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(6000, 5000)]
    public void TransitionDelayClampsToSupportedMillisecondRange(int input, int expected)
    {
        var viewModel = CreateViewModel();

        viewModel.FanTransitionSensitivity = input;

        Assert.Equal(expected, viewModel.FanTransitionSensitivity);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(24)]
    public async Task OpeningFanDrawerPreservesOfficialReadbackWithoutWritingOrQuantizing(int milliseconds)
    {
        var fan = new RecordingFanController(milliseconds);
        var viewModel = CreateViewModel(fan);
        viewModel.SelectProfile("custom");

        await viewModel.OpenFanCurveAsync(CancellationToken.None);

        Assert.Equal(milliseconds, viewModel.FanTransitionSensitivity);
        Assert.Null(fan.LastAppliedResponse);
    }

    [Fact]
    public async Task ApplyingFanDelayPassesRawMillisecondsToController()
    {
        var fan = new RecordingFanController(300);
        var viewModel = CreateViewModel(fan);
        viewModel.SelectProfile("custom");
        await viewModel.OpenFanCurveAsync(CancellationToken.None);
        viewModel.FanTransitionSensitivity = 5000;

        await viewModel.ApplyFanCurveAsync(CancellationToken.None);

        Assert.Equal(5000, fan.LastAppliedResponse?.TransitionSensitivity);
    }

    private static MainWindowViewModel CreateViewModel(IOemFanController? fan = null) => new(
        HomeDashboardFactory.Create(new MachineIdentity(
            "MECHREVO", "YAOSHI Series", "YAOSHI Series-X6AR55xY",
            "Intel(R) Core(TM) Ultra 9 275HX", "N.1.18MRO24", 24, 24),
            ["Intel(R) Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]),
        new ApplicationSettings(), new MemorySettingsStore(), new NoStartupRegistration(),
        fanController: fan, lightingProfileStore: new EmptyLightingProfileStore());

    private sealed class MemorySettingsStore : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NoStartupRegistration : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }

    private sealed class EmptyLightingProfileStore : ILightingProfileStore
    {
        public LightingProfile? Load() => null;
        public void Save(LightingProfile profile) { }
        public LightingProfile Import(string path) => throw new NotSupportedException();
        public void Export(string path, LightingProfile profile) => throw new NotSupportedException();
    }

    private sealed class RecordingFanController(int milliseconds) : IOemFanController
    {
        private FanResponseSettings response = new(true, true, milliseconds);
        private readonly FanCurveTable table = CreateTable();
        public FanResponseSettings? LastAppliedResponse { get; private set; }

        public ValueTask<FanCurveTable?> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<FanCurveTable?>(table);
        public ValueTask<FanResponseSettings?> ReadResponseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<FanResponseSettings?>(response);
        public ValueTask<ControlResult> ApplyAsync(FanCurveTable target, FanResponseSettings targetResponse,
            CancellationToken cancellationToken)
        {
            response = targetResponse;
            LastAppliedResponse = targetResponse;
            return ValueTask.FromResult(new ControlResult(true, "风扇设置已写入并回读确认。"));
        }
        public ValueTask<FanRestoreResult> RestoreAsync(string profileName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static FanCurveTable CreateTable()
        {
            var points = Enumerable.Range(0, 16).Select(index => new FanCurvePoint(
                index, 30 + index * 4, 25 + index * 4, index == 15 ? 100 : index * 6)).ToArray();
            return new("Profile1", true, points, points);
        }
    }
}

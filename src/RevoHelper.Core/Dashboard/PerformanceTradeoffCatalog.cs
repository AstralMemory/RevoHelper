namespace RevoHelper.Core.Dashboard;

public static class PerformanceTradeoffCatalog
{
    public static PerformanceTradeoff Get(string profileId, TurboStrategy turboStrategy) =>
        profileId switch
        {
            "office" => new(50, 30, 15),
            "balanced" => new(80, 65, 50),
            "turbo" when turboStrategy is TurboStrategy.HighEfficiency => new(100, 70, 80),
            "turbo" => new(92, 80, 60),
            "custom" => new(0, 0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(profileId), profileId, "Unknown profile.")
        };
}

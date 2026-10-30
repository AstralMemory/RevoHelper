namespace RevoHelper.App.Charging;

/// <summary>Explicit one-launch opt-in only. Never read from saved settings or Windows startup.</summary>
public static class ManualExperimentalChargeLaunch
{
    public static int? ReadLimit(string[] args) => args switch
    {
        ["--manual-charge-limit-accept-platform-risk", "60"] => 60,
        ["--manual-charge-limit-accept-platform-risk", "80"] => 80,
        ["--manual-charge-limit-accept-platform-risk", "100"] => 100,
        _ => null
    };
}

namespace RevoHelper.App.Charging;

public sealed record ChargeRecoveryIntents(bool Threshold, bool Platform, string? Error = null)
{
    public static ChargeRecoveryIntents ReadOrConservativeFallback(Func<string, bool> read)
    {
        try { return new(read("ThresholdApplyIntent"), read("PlatformApplyIntent")); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
            or KeyNotFoundException or InvalidOperationException or System.Text.Json.JsonException)
        { return new(true, true, ex.Message); }
    }
}

namespace RevoHelper.Hardware.Windows.OemStatus;

public static class GcuControlSessionConflictDetector
{
    public static bool IsKnownConflictingProcessName(string processName) =>
        string.Equals(processName, "CCUWinUI", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "ControlCenterU", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "GamingCenterU", StringComparison.OrdinalIgnoreCase) ||
        processName.StartsWith("L-Mechrevo", StringComparison.OrdinalIgnoreCase);

}

namespace RevoHelper.Core.Control;

public sealed record PerformanceSchedule(
    PlatformModeSelection Platform,
    CpuPolicy? Cpu,
    ProcessorBoostPolicy Boost,
    ProcessorFrequencyLimitPolicy Frequency);

public sealed record PerformanceScheduleResult(
    bool Succeeded,
    string Reason,
    PerformanceSchedule? Readback = null);

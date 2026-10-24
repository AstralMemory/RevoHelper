using System.Text.Json;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Observation;
using RevoHelper.Core.Safety;
using RevoHelper.Hardware.Windows.CpuPower;
using RevoHelper.Hardware.Windows.Discovery;
using RevoHelper.Hardware.Windows.Observation;
using RevoHelper.Hardware.Windows.OemControl;
using RevoHelper.Hardware.Windows.Telemetry;
using RevoHelper.Hardware.Windows.OemStatus;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

if (args.Length == 3 && args[0] == "gcu" && args[1] == "coexist-read")
{
    Environment.ExitCode = await GcuCoexistenceProbe.RunAsync(args[2]);
    return;
}

if (args.Length == 2 && args[0] == "gcu" && args[1] == "coexist-mode-trial")
{
    Environment.ExitCode = await GcuCoexistenceProbe.RunManagedTrialAsync();
    return;
}

if (args.Length == 3 &&
    args[0].Equals("cpu-power", StringComparison.OrdinalIgnoreCase) &&
    args[1].Equals("monitor", StringComparison.OrdinalIgnoreCase) &&
    int.TryParse(args[2], out var monitorSeconds))
{
    if (monitorSeconds is < 5 or > 60)
    {
        Console.Error.WriteLine("Monitor duration must be 5-60 seconds.");
        Environment.ExitCode = 2;
        return;
    }

    var query = new SystemManagementWmiQuery();
    var identity = new WindowsMachineIdentityReader(query).Read();
    using var access = new PawnIoMsrAccess(WindowsPawnIoModule.LoadSignedIntelMsrModule());
    if (!access.TryRead(0x606, out var unit) ||
        !access.TryRead(0x611, out var firstEnergy) ||
        !access.TryRead(0x613, out var firstPerf))
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            ReadOnly = true,
            access.AvailabilityReason,
            Error = "Unable to read the initial RAPL counters."
        }, jsonOptions));
        Environment.ExitCode = 5;
        return;
    }

    var samples = new List<object>();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    var lastEnergy = firstEnergy;
    var lastPerf = firstPerf;
    var completed = true;
    for (var second = 0; second <= monitorSeconds; second++)
    {
        if (second > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        if (!access.TryRead(0x611, out lastEnergy) ||
            !access.TryRead(0x613, out lastPerf) ||
            !access.TryRead(0x610, out var packageLimit) ||
            !access.TryRead(0x1B1, out var packageThermalStatus))
        {
            completed = false;
            break;
        }

        samples.Add(new
        {
            ElapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 3),
            PackageLimitRaw = $"0x{packageLimit:X16}",
            PackageLimit = IntelRaplPowerLimitCodec.Decode(unit, packageLimit),
            PackageThermalStatusRaw = $"0x{packageThermalStatus:X16}",
            PackageThermalStatus = IntelPackageThermalStatusDecoder.DecodeCurrent(packageThermalStatus)
        });
    }

    timer.Stop();
    var energyDelta = IntelRaplCounterCodec.Delta32(firstEnergy, lastEnergy);
    var perfDelta = IntelRaplCounterCodec.Delta32(firstPerf, lastPerf);
    var joules = IntelRaplCounterCodec.ToJoules(unit, energyDelta);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ReadOnly = true,
        Completed = completed,
        identity.Manufacturer,
        identity.ProductName,
        CpuName = identity.ProcessorName,
        identity.BiosVersion,
        RequestedDurationSeconds = monitorSeconds,
        ActualDurationSeconds = Math.Round(timer.Elapsed.TotalSeconds, 3),
        AveragePackagePowerWatts = timer.Elapsed.TotalSeconds > 0
            ? Math.Round(joules / timer.Elapsed.TotalSeconds, 3)
            : 0,
        EnergyCounter = new
        {
            Before = $"0x{firstEnergy:X16}",
            After = $"0x{lastEnergy:X16}",
            Delta = energyDelta,
            Joules = Math.Round(joules, 6)
        },
        PackagePerformanceCounter = new
        {
            Before = $"0x{firstPerf:X16}",
            After = $"0x{lastPerf:X16}",
            Delta = perfDelta
        },
        Samples = samples
    }, jsonOptions));
    Environment.ExitCode = completed ? 0 : 5;
    return;
}

if (args.Length == 2 &&
    args[0].Equals("cpu-power", StringComparison.OrdinalIgnoreCase) &&
    args[1].Equals("layers", StringComparison.OrdinalIgnoreCase))
{
    var query = new SystemManagementWmiQuery();
    var identity = new WindowsMachineIdentityReader(query).Read();

    bool msrUnitRead;
    bool msrLimitRead;
    bool platformLimitRead;
    bool perfLimitReasonsRead;
    bool iaPlaneLimitRead;
    bool vrCurrentConfigRead;
    bool packageThermalStatusRead;
    bool packagePowerInfoRead;
    bool packagePerfStatusRead;
    ulong msrUnit;
    ulong msrLimit;
    ulong platformLimit;
    ulong perfLimitReasons;
    ulong iaPlaneLimit;
    ulong vrCurrentConfig;
    ulong packageThermalStatus;
    ulong packagePowerInfo;
    ulong packagePerfStatus;
    string msrReason;
    using (var access = new PawnIoMsrAccess(WindowsPawnIoModule.LoadSignedIntelMsrModule()))
    {
        msrReason = access.AvailabilityReason;
        msrUnitRead = access.TryRead(0x606, out msrUnit);
        msrLimitRead = access.TryRead(0x610, out msrLimit);
        platformLimitRead = access.TryRead(0x65C, out platformLimit);
        perfLimitReasonsRead = access.TryRead(0x64F, out perfLimitReasons);
        iaPlaneLimitRead = access.TryRead(0x638, out iaPlaneLimit);
        vrCurrentConfigRead = access.TryRead(0x601, out vrCurrentConfig);
        packageThermalStatusRead = access.TryRead(0x1B1, out packageThermalStatus);
        packagePowerInfoRead = access.TryRead(0x614, out packagePowerInfo);
        packagePerfStatusRead = access.TryRead(0x613, out packagePerfStatus);
    }

    bool mchbarBaseRead;
    bool mchbarLimitRead;
    ulong mchbarBase;
    ulong mchbarLimit;
    string mchbarReason;
    using (var access = new PawnIoMchbarAccess(WindowsPawnIoModule.LoadSignedIntelMchbarModule()))
    {
        mchbarReason = access.AvailabilityReason;
        mchbarBaseRead = access.TryReadBaseAddress(out mchbarBase);
        mchbarLimitRead = access.TryReadPackageRaplLimit(out mchbarLimit);
    }

    var serviceNames = new HashSet<string>(["ipfsvc", "dptftcs", "XtuService"], StringComparer.OrdinalIgnoreCase);
    var driverNames = new HashSet<string>(["ipf_cpu", "ipf_acpi", "ipf_smbus"], StringComparer.OrdinalIgnoreCase);
    var services = ReadWmiState(query, "Win32_Service", serviceNames);
    var drivers = ReadWmiState(query, "Win32_SystemDriver", driverNames);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ReadOnly = true,
        identity.Manufacturer,
        identity.ProductName,
        CpuName = identity.ProcessorName,
        identity.BiosVersion,
        Msr = new
        {
            AvailabilityReason = msrReason,
            PowerUnitRead = msrUnitRead,
            PowerUnitRaw = msrUnitRead ? $"0x{msrUnit:X16}" : null,
            PackageLimitRead = msrLimitRead,
            PackageLimitRaw = msrLimitRead ? $"0x{msrLimit:X16}" : null,
            Decoded = msrUnitRead && msrLimitRead
                ? IntelRaplPowerLimitCodec.Decode(msrUnit, msrLimit)
                : null
        },
        Mchbar = new
        {
            AvailabilityReason = mchbarReason,
            BaseAddressRead = mchbarBaseRead,
            BaseAddress = mchbarBaseRead ? $"0x{mchbarBase:X16}" : null,
            PackageLimitOffset = $"0x{PawnIoMchbarAccess.PackageRaplLimitOffset:X}",
            PackageLimitRead = mchbarLimitRead,
            PackageLimitRaw = mchbarLimitRead ? $"0x{mchbarLimit:X16}" : null,
            Decoded = msrUnitRead && mchbarLimitRead
                ? IntelRaplPowerLimitCodec.Decode(msrUnit, mchbarLimit)
                : null
        },
        PlatformPowerLimit = new
        {
            Register = "MSR 0x65C",
            SignedModuleAllowListed = false,
            Read = platformLimitRead,
            Raw = platformLimitRead ? $"0x{platformLimit:X16}" : null,
            Decoded = msrUnitRead && platformLimitRead
                ? IntelPlatformPowerLimitCodec.Decode(msrUnit, platformLimit)
                : null
        },
        CorePerformanceLimitReasons = new
        {
            Register = "MSR 0x64F",
            SignedModuleAllowListed = false,
            Read = perfLimitReasonsRead,
            Raw = perfLimitReasonsRead ? $"0x{perfLimitReasons:X16}" : null,
            Current = perfLimitReasonsRead
                ? IntelCorePerfLimitReasonDecoder.DecodeCurrent(perfLimitReasons)
                : null
        },
        IaPlanePowerLimit = new
        {
            Register = "MSR 0x638",
            Read = iaPlaneLimitRead,
            Raw = iaPlaneLimitRead ? $"0x{iaPlaneLimit:X16}" : null,
            Decoded = msrUnitRead && iaPlaneLimitRead
                ? IntelIaPlanePowerLimitCodec.Decode(msrUnit, iaPlaneLimit)
                : null
        },
        VrCurrentConfig = new
        {
            Register = "MSR 0x601",
            Read = vrCurrentConfigRead,
            Raw = vrCurrentConfigRead ? $"0x{vrCurrentConfig:X16}" : null,
            Decoded = vrCurrentConfigRead
                ? IntelVrCurrentConfigCodec.Decode(vrCurrentConfig)
                : null
        },
        PackageThermalStatus = new
        {
            Register = "MSR 0x1B1",
            Read = packageThermalStatusRead,
            Raw = packageThermalStatusRead ? $"0x{packageThermalStatus:X16}" : null,
            Current = packageThermalStatusRead
                ? IntelPackageThermalStatusDecoder.DecodeCurrent(packageThermalStatus)
                : null
        },
        PackagePowerInfo = new
        {
            Register = "MSR 0x614",
            Read = packagePowerInfoRead,
            Raw = packagePowerInfoRead ? $"0x{packagePowerInfo:X16}" : null,
            Decoded = msrUnitRead && packagePowerInfoRead
                ? IntelPackagePowerInfoCodec.Decode(msrUnit, packagePowerInfo)
                : null
        },
        PackagePerformanceStatus = new
        {
            Register = "MSR 0x613",
            Read = packagePerfStatusRead,
            Raw = packagePerfStatusRead ? $"0x{packagePerfStatus:X16}" : null
        },
        Services = services,
        Drivers = drivers
    }, jsonOptions));
    Environment.ExitCode = msrUnitRead && msrLimitRead && mchbarLimitRead ? 0 : 5;
    return;
}

if (args.FirstOrDefault()?.Equals("cpu-power", StringComparison.OrdinalIgnoreCase) is true)
{
    var identity = new WindowsMachineIdentityReader(new SystemManagementWmiQuery()).Read();
    var probe = await new DirectCpuPolicyBackendProbe().ProbeAsync(
        identity.Cpu,
        AppContext.BaseDirectory,
        CancellationToken.None);

    try
    {
        var current = probe.Controller is null
            ? null
            : await probe.Controller.ReadCpuPolicyAsync(CancellationToken.None);

        if (args.Length == 2 && args[1].Equals("probe", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                identity.Manufacturer,
                identity.ProductName,
                CpuName = identity.ProcessorName,
                Cpu = identity.Cpu,
                probe.Capability,
                CurrentPowerLimits = current
            }, jsonOptions));
            Environment.ExitCode = probe.Controller is null ? 5 : 0;
            return;
        }

        if (args.Length == 5 &&
            args[1].Equals("apply", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[2], out var pl1) &&
            int.TryParse(args[3], out var pl2) &&
            args[4].Equals("--confirm-direct-write", StringComparison.OrdinalIgnoreCase))
        {
            if (probe.Controller is null)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    identity.Manufacturer,
                    identity.ProductName,
                    CpuName = identity.ProcessorName,
                    probe.Capability,
                    Error = "No verified direct CPU power backend is available."
                }, jsonOptions));
                Environment.ExitCode = 5;
                return;
            }

            if (pl1 is < 5 or > 250 || pl2 is < 5 or > 250 || pl1 > pl2)
            {
                Console.Error.WriteLine("PL1/PL2 must be 5-250 W and PL1 must not exceed PL2.");
                Environment.ExitCode = 2;
                return;
            }

            var result = await probe.Controller.ApplyCpuPolicyAsync(
                new CpuPolicy(pl1, pl2, null),
                CancellationToken.None);
            var readback = await probe.Controller.ReadCpuPolicyAsync(CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                identity.Manufacturer,
                identity.ProductName,
                CpuName = identity.ProcessorName,
                probe.Capability,
                Before = current,
                Requested = new CpuPolicy(pl1, pl2, null),
                Result = result,
                Readback = readback
            }, jsonOptions));
            Environment.ExitCode = result.Succeeded ? 0 : 6;
            return;
        }

        if (args.Length == 6 &&
            args[1].Equals("test", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[2], out var testPl1) &&
            int.TryParse(args[3], out var testPl2) &&
            int.TryParse(args[4], out var seconds) &&
            args[5].Equals("--confirm-direct-write", StringComparison.OrdinalIgnoreCase))
        {
            if (probe.Controller is null || current is null)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    identity.Manufacturer,
                    identity.ProductName,
                    CpuName = identity.ProcessorName,
                    probe.Capability,
                    Error = "No verified direct CPU power backend or restorable baseline is available."
                }, jsonOptions));
                Environment.ExitCode = 5;
                return;
            }

            if (testPl1 is < 5 or > 250 || testPl2 is < 5 or > 250 || testPl1 > testPl2 ||
                seconds is < 5 or > 120)
            {
                Console.Error.WriteLine(
                    "PL1/PL2 must be 5-250 W, PL1 must not exceed PL2, and duration must be 5-120 seconds.");
                Environment.ExitCode = 2;
                return;
            }

            var requested = new CpuPolicy(testPl1, testPl2, null);
            var applied = await probe.Controller.ApplyCpuPolicyAsync(requested, CancellationToken.None);
            var activeReadback = await probe.Controller.ReadCpuPolicyAsync(CancellationToken.None);
            if (applied.Succeeded)
            {
                Console.WriteLine($"Direct PL test is active for {seconds} seconds. Start the workload now.");
                await Task.Delay(TimeSpan.FromSeconds(seconds));
            }

            var restored = applied.Succeeded
                ? await probe.Controller.ApplyCpuPolicyAsync(current, CancellationToken.None)
                : ControlResult.Rejected("Test write failed; restoration was not required.");
            var restoredReadback = await probe.Controller.ReadCpuPolicyAsync(CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                identity.Manufacturer,
                identity.ProductName,
                CpuName = identity.ProcessorName,
                probe.Capability,
                Before = current,
                Requested = requested,
                ApplyResult = applied,
                ActiveReadback = activeReadback,
                RestoreResult = restored,
                RestoredReadback = restoredReadback
            }, jsonOptions));
            Environment.ExitCode = applied.Succeeded && restored.Succeeded ? 0 : 6;
            return;
        }

        Console.Error.WriteLine(
            "Usage: cpu-power layers | cpu-power monitor <seconds> | cpu-power probe | " +
            "cpu-power apply <PL1-W> <PL2-W> --confirm-direct-write | " +
            "cpu-power test <PL1-W> <PL2-W> <seconds> --confirm-direct-write");
        Environment.ExitCode = 2;
        return;
    }
    finally
    {
        if (probe.Controller is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

if (args.Length == 2 &&
    args[0].Equals("transition", StringComparison.OrdinalIgnoreCase) &&
    args[1].Equals("dry-run", StringComparison.OrdinalIgnoreCase))
{
    var transitionQuery = new RetryingWmiQuery(
        new SystemManagementWmiQuery(),
        maxAttempts: 3,
        delay: TimeSpan.FromMilliseconds(100));
    var identity = new WindowsMachineIdentityReader(transitionQuery).Read();
    var assessment = MachineSupportClassifier.Classify(identity);
    await using var transitionTelemetry = new LibreHardwareTelemetrySource();
    var observation = await new WindowsModeObservationReader(
        transitionQuery,
        transitionTelemetry).CaptureAsync("transition-dry-run", CancellationToken.None);
    var mode = observation.OemOperatingMode switch
    {
        0 => PlatformMode.Office,
        1 => PlatformMode.Balanced,
        2 => PlatformMode.TurboQuiet,
        3 => PlatformMode.Custom,
        _ => (PlatformMode?)null
    };
    var highLoad = observation.Telemetry?.Devices.Any(device =>
        device.UsagePercent is > 25) is true;
    var knownWorkloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "YuanShen",
        "GenshinImpact",
        "StarRail",
        "ZenlessZoneZero",
        "GamePPLite",
        "prime95",
        "furmark"
    };
    var activeWorkloads = System.Diagnostics.Process.GetProcesses()
        .Select(process => process.ProcessName)
        .Where(knownWorkloads.Contains)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var context = new ControlledTransitionContext(
        assessment.Status,
        ExactBiosSupported: identity.BiosVersion.Equals("N.1.18MRO24", StringComparison.OrdinalIgnoreCase),
        AdapterVerified: false,
        observation.IsAcOnline,
        mode,
        mode,
        observation.Telemetry,
        highLoad,
        activeWorkloads.Length > 0);
    var decision = ControlledTransitionGate.Evaluate(context);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        identity.ProductName,
        identity.BaseboardProduct,
        identity.BiosVersion,
        CurrentOemMode = observation.OemOperatingMode,
        InterpretedMode = mode?.ToString(),
        ActiveWorkloads = activeWorkloads,
        Decision = decision
    }, jsonOptions));
    Environment.ExitCode = decision.Allowed ? 0 : 4;
    return;
}

if (args.FirstOrDefault()?.Equals("observe", StringComparison.OrdinalIgnoreCase) is true)
{
    if (args.Length >= 3 && args[1].Equals("capture", StringComparison.OrdinalIgnoreCase))
    {
        var label = args[2];
        if (label.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            Console.Error.WriteLine("Observation labels may contain only letters, digits, '-' and '_'.");
            Environment.ExitCode = 2;
            return;
        }

        var outputIndex = Array.FindIndex(args, argument =>
            argument.Equals("--output", StringComparison.OrdinalIgnoreCase));
        var outputPath = outputIndex >= 0 && outputIndex + 1 < args.Length
            ? Path.GetFullPath(args[outputIndex + 1])
            : null;
        if (outputPath is not null && File.Exists(outputPath))
        {
            Console.Error.WriteLine($"Refusing to overwrite existing observation: {outputPath}");
            Environment.ExitCode = 3;
            return;
        }

        var observationQuery = new RetryingWmiQuery(
            new SystemManagementWmiQuery(),
            maxAttempts: 3,
            delay: TimeSpan.FromMilliseconds(100));
        await using var observationTelemetry = new LibreHardwareTelemetrySource();
        var observation = await new WindowsModeObservationReader(
            observationQuery,
            observationTelemetry).CaptureAsync(label, CancellationToken.None);
        var json = JsonSerializer.Serialize(observation, jsonOptions);
        if (outputPath is null)
        {
            Console.WriteLine(json);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine(outputPath);
        }

        return;
    }

    if (args.Length == 4 && args[1].Equals("diff", StringComparison.OrdinalIgnoreCase))
    {
        var before = JsonSerializer.Deserialize<ModeObservationSnapshot>(
            await File.ReadAllTextAsync(args[2]), jsonOptions)
            ?? throw new InvalidDataException("Before observation is empty.");
        var after = JsonSerializer.Deserialize<ModeObservationSnapshot>(
            await File.ReadAllTextAsync(args[3]), jsonOptions)
            ?? throw new InvalidDataException("After observation is empty.");
        Console.WriteLine(JsonSerializer.Serialize(ModeObservationDiffer.Diff(before, after), jsonOptions));
        return;
    }

    Console.Error.WriteLine("Usage: observe capture <label> [--output <file>] | observe diff <before> <after>");
    Environment.ExitCode = 2;
    return;
}

if (args.FirstOrDefault()?.Equals("telemetry", StringComparison.OrdinalIgnoreCase) is true)
{
    var samples = 1;
    var samplesIndex = Array.FindIndex(args, argument =>
        argument.Equals("--samples", StringComparison.OrdinalIgnoreCase));
    if (samplesIndex >= 0 &&
        samplesIndex + 1 < args.Length &&
        int.TryParse(args[samplesIndex + 1], out var parsedSamples))
    {
        samples = Math.Clamp(parsedSamples, 1, 30);
    }

    await using var source = new LibreHardwareTelemetrySource();
    if (args.Any(argument => argument.Equals("--raw", StringComparison.OrdinalIgnoreCase)))
    {
        var catalog = await source.ReadSensorCatalogAsync(CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(catalog, jsonOptions));
        return;
    }

    for (var index = 0; index < samples; index++)
    {
        var snapshot = await source.ReadAsync(CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(snapshot, jsonOptions));
        if (index + 1 < samples)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    return;
}

if (args.FirstOrDefault()?.Equals("capabilities", StringComparison.OrdinalIgnoreCase) is true)
{
    var inventoryQuery = new RetryingWmiQuery(
        new SystemManagementWmiQuery(),
        maxAttempts: 3,
        delay: TimeSpan.FromMilliseconds(100));
    var inventory = new OemControlInventoryReader(
        new WindowsOemEnvironmentProbe(inventoryQuery)).Read();
    Console.WriteLine(JsonSerializer.Serialize(inventory, jsonOptions));
    return;
}

if (args.SequenceEqual(new[] { "gpu-performance", "driver-policy" }))
{
    Console.WriteLine(JsonSerializer.Serialize(NvidiaPowerPolicyProbe.Read(), jsonOptions));
    return;
}

if (args.Length == 2 && args[0].Equals("gpu-performance", StringComparison.OrdinalIgnoreCase) &&
    args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    await using var source = new GcuMqttStatusSource();
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var runTask = source.RunAsync(update =>
    {
        if (update is OemControlSessionUpdate) connected.TrySetResult();
        return ValueTask.CompletedTask;
    }, error => { Console.Error.WriteLine(error.Message); return ValueTask.CompletedTask; }, timeout.Token);
    try
    {
        await connected.Task.WaitAsync(timeout.Token);
        var payload = await source.ReadCustomPerformanceStatusAsync(timeout.Token);
        using var document = JsonDocument.Parse(payload);
        var selected = document.RootElement.EnumerateObject()
            .Where(property => property.Name.StartsWith("GPU_", StringComparison.Ordinal) ||
                property.Name.StartsWith("CPU_", StringComparison.Ordinal) ||
                property.Name.Contains("OverClock", StringComparison.OrdinalIgnoreCase) ||
                property.Name is "OperatingMode" or "TjMax")
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        Console.WriteLine(JsonSerializer.Serialize(new { ReadOnly = true, Status = selected }, jsonOptions));
    }
    finally
    {
        timeout.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }
    }
    return;
}

if (args.Length == 4 && args[0].Equals("gpu-performance", StringComparison.OrdinalIgnoreCase) &&
    args[1].Equals("trial", StringComparison.OrdinalIgnoreCase) && args[3] == "--execute")
{
    var activeApp = System.Diagnostics.Process.GetProcesses()
        .FirstOrDefault(process => process.Id != Environment.ProcessId &&
            process.ProcessName.Contains("RevoHelper", StringComparison.OrdinalIgnoreCase) &&
            !process.ProcessName.Contains("Probe", StringComparison.OrdinalIgnoreCase));
    if (activeApp is not null)
    {
        Console.Error.WriteLine("Exit RevoHelper before an exclusive GPU trial.");
        Environment.ExitCode = 2;
        return;
    }

    var setting = args[2] switch
    {
        "dynamic-switch" => CustomPerformanceSetting.GpuDynamicBoostEnabled,
        "dynamic-managed" => CustomPerformanceSetting.GpuDynamicBoostEnabled,
        "dynamic-managed-watts" => CustomPerformanceSetting.GpuDynamicBoostWatts,
        "dynamic-zero" => CustomPerformanceSetting.GpuDynamicBoostEnabled,
        "dynamic-compensated" => CustomPerformanceSetting.GpuDynamicBoostEnabled,
        "dynamic-watts-refresh" => CustomPerformanceSetting.GpuDynamicBoostWatts,
        "dynamic-watts" => CustomPerformanceSetting.GpuDynamicBoostWatts,
        "negative-core" => CustomPerformanceSetting.GpuCoreOffsetMhz,
        "negative-core-100" => CustomPerformanceSetting.GpuCoreOffsetMhz,
        "negative-core-300" => CustomPerformanceSetting.GpuCoreOffsetMhz,
        _ => CustomPerformanceSetting.GpuMemoryOffsetMhz
    };
    if (setting == CustomPerformanceSetting.GpuMemoryOffsetMhz)
    {
        Console.Error.WriteLine("Trial must be dynamic-switch, dynamic-managed, dynamic-watts, or negative-core[-100|-300].");
        Environment.ExitCode = 2;
        return;
    }

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await using var source = new GcuMqttStatusSource();
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var runTask = source.RunAsync(update =>
    {
        if (update is OemControlSessionUpdate) connected.TrySetResult();
        return ValueTask.CompletedTask;
    }, error => { Console.Error.WriteLine(error.Message); return ValueTask.CompletedTask; }, timeout.Token);
    try
    {
        await connected.Task.WaitAsync(timeout.Token);
        static int? Field(string raw, string name)
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.TryGetProperty(name, out var value) && int.TryParse(value.ToString(), out var number)
                ? number : null;
        }
        var initial = await source.ReadCustomPerformanceStatusAsync(timeout.Token);
        if (Field(initial, "OperatingMode") != 3 || Field(initial, "OverClockingSwitch") != 1)
            throw new InvalidOperationException("Trial requires custom mode with GPU super performance on.");
        var fieldName = setting switch
        {
            CustomPerformanceSetting.GpuDynamicBoostEnabled => "GPU_DynamicBoostSwitch",
            CustomPerformanceSetting.GpuDynamicBoostWatts => "GPU_DynamicBoost",
            CustomPerformanceSetting.GpuCoreOffsetMhz => "GPU_CoreClockOffsetOC",
            _ => throw new ArgumentOutOfRangeException(nameof(setting))
        };
        var baseline = Field(initial, fieldName)
            ?? throw new InvalidOperationException("OEM omitted the selected field.");
        var baselineDynamicWatts = Field(initial, "GPU_DynamicBoost");
        var zeroBoostTrial = args[2] is "dynamic-zero" or "dynamic-compensated";
        var refreshWattsTrial = args[2] == "dynamic-watts-refresh";
        var baselineTgp = Field(initial, "GPU_ConfigurableTGPTarget");
        if (zeroBoostTrial && (baselineDynamicWatts is null || baselineTgp is null))
            throw new InvalidOperationException("Zero-boost trial requires live DB watts and TGP.");
        var managed = args[2] is "dynamic-managed" or "dynamic-managed-watts";
        var managedController = managed
            ? new GcuCustomPerformanceController(source,
                verificationDelay: null, readEnforcedGpuPowerLimit: NvidiaGpuPowerLimitReader.ReadAsync,
                powerBudget: new GpuPowerBudgetController(source,
                    new GcuPlatformModeController(source).ReadPlatformModeAsync,
                    NvidiaGpuPowerLimitReader.ReadAsync, new ProbePowerBudgetStore()))
            : null;
        var minimumDynamicBoost = Field(initial, "GPU_DynamicBoostMinimum");
        var target = setting switch
        {
            CustomPerformanceSetting.GpuDynamicBoostEnabled when baseline == 1 => 0,
            CustomPerformanceSetting.GpuDynamicBoostWatts when minimumDynamicBoost < baseline => minimumDynamicBoost.Value,
            CustomPerformanceSetting.GpuCoreOffsetMhz when baseline == 0 && args[2] == "negative-core-100" => -100,
            CustomPerformanceSetting.GpuCoreOffsetMhz when baseline == 0 && args[2] == "negative-core-300" => -300,
            CustomPerformanceSetting.GpuCoreOffsetMhz when baseline == 0 => -20,
            _ => throw new InvalidOperationException("Unexpected baseline; trial declined without writing.")
        };
        static string Command(CustomPerformanceSetting field, int value) => field switch
        {
            CustomPerformanceSetting.GpuDynamicBoostEnabled => GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(value == 1),
            CustomPerformanceSetting.GpuDynamicBoostWatts => GcuCustomPerformanceCommand.GpuDynamicBoost(value),
            CustomPerformanceSetting.GpuCoreOffsetMhz => GcuCustomPerformanceCommand.GpuCoreOffset(value),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        static async Task<object> SnapshotAsync(string raw, string phase)
        {
            var gpuCeiling = string.Empty;
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=enforced.power.limit --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process is not null)
                {
                    gpuCeiling = (await process.StandardOutput.ReadToEndAsync()).Trim();
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception exception) { gpuCeiling = $"unavailable: {exception.GetType().Name}"; }
            return new
            {
                Phase = phase,
                Mode = Field(raw, "OperatingMode"),
                SuperPerformance = Field(raw, "OverClockingSwitch"),
                Tgp = Field(raw, "GPU_ConfigurableTGPTarget"),
                DynamicSwitch = Field(raw, "GPU_DynamicBoostSwitch"),
                DynamicWatts = Field(raw, "GPU_DynamicBoost"),
                CoreOffset = Field(raw, "GPU_CoreClockOffsetOC"),
                CoreMinimum = GcuCustomPerformanceStatusParser.ParseGpuClockOffsets(raw)?.Limits?.CoreMinimum,
                NvidiaEnforcedPowerLimitWatts = gpuCeiling
            };
        }

        var samples = new List<object> { await SnapshotAsync(initial, "before") };
        try
        {
            if (managedController is not null)
            {
                var applied = await managedController.ApplySettingAsync(setting, target, timeout.Token);
                if (!applied.Succeeded) throw new InvalidOperationException(applied.Reason);
            }
            else await source.WriteCustomPerformanceAsync(Command(setting, target), timeout.Token);
            if (zeroBoostTrial)
            {
                await source.WriteCustomPerformanceAsync(GcuCustomPerformanceCommand.GpuDynamicBoost(0), timeout.Token);
                await source.ReadCustomPerformanceStatusAsync(timeout.Token);
                await Task.Delay(350, timeout.Token);
                await source.WriteCustomPerformanceAsync(GcuCustomPerformanceCommand.GpuConfigurableTgp(
                    baselineTgp!.Value - (args[2] == "dynamic-compensated" ? 5 : 0)), timeout.Token);
            }
            if (refreshWattsTrial)
            {
                await source.ReadCustomPerformanceStatusAsync(timeout.Token);
                await Task.Delay(350, timeout.Token);
                await source.WriteCustomPerformanceAsync(GcuCustomPerformanceCommand.GpuConfigurableTgp(baselineTgp!.Value), timeout.Token);
            }
            foreach (var (pause, phase) in new[]
            {
                (200, "after-200ms"), (800, "after-1s"), (2000, "after-3s")
            })
            {
                await Task.Delay(pause, timeout.Token);
                samples.Add(await SnapshotAsync(await source.ReadCustomPerformanceStatusAsync(timeout.Token), phase));
            }
        }
        finally
        {
            // The baseline came from the live OEM status, never from a saved UI draft.
            if (managedController is not null)
            {
                var restored = await managedController.ApplySettingAsync(setting, baseline, CancellationToken.None);
                if (!restored.Succeeded)
                    Console.Error.WriteLine($"Managed restoration not confirmed: {restored.Reason}");
            }
            else
                await source.WriteCustomPerformanceAsync(Command(setting, baseline), CancellationToken.None);
            if (managedController is null && setting == CustomPerformanceSetting.GpuDynamicBoostEnabled && baseline == 1 &&
                baselineDynamicWatts is { } watts)
                await source.WriteCustomPerformanceAsync(GcuCustomPerformanceCommand.GpuDynamicBoost(watts), CancellationToken.None);
            if (zeroBoostTrial || refreshWattsTrial)
                await source.WriteCustomPerformanceAsync(GcuCustomPerformanceCommand.GpuConfigurableTgp(baselineTgp!.Value), CancellationToken.None);
            await Task.Delay(250);
            samples.Add(await SnapshotAsync(await source.ReadCustomPerformanceStatusAsync(CancellationToken.None), "restored"));
            await Task.Delay(1000);
            samples.Add(await SnapshotAsync(await source.ReadCustomPerformanceStatusAsync(CancellationToken.None), "restored-after-1s"));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Setting = setting.ToString(), Baseline = baseline, Target = target, Samples = samples }, jsonOptions));
        var final = await source.ReadCustomPerformanceStatusAsync(timeout.Token);
        if (Field(final, fieldName) != baseline)
            throw new InvalidOperationException("OEM did not confirm restoration of the baseline value.");
        if (zeroBoostTrial && (Field(final, "GPU_DynamicBoost") != baselineDynamicWatts ||
            Field(final, "GPU_ConfigurableTGPTarget") != baselineTgp))
            throw new InvalidOperationException("OEM did not confirm restoration of DB watts and TGP.");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"GPU trial failed: {exception.GetType().Name}: {exception.Message}");
        Environment.ExitCode = 5;
    }
    finally
    {
        timeout.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }
    }
    return;
}

if (args.Length == 2 &&
    args[0].Equals("gpu-output", StringComparison.OrdinalIgnoreCase) &&
    args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    await using var source = new GcuMqttStatusSource();
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var errors = new List<string>();
    var runTask = source.RunAsync(
        update =>
        {
            if (update is OemControlSessionUpdate) connected.TrySetResult();
            return ValueTask.CompletedTask;
        },
        error =>
        {
            errors.Add($"{error.GetType().Name}: {error.Message}");
            return ValueTask.CompletedTask;
        },
        timeout.Token);

    try
    {
        await connected.Task.WaitAsync(timeout.Token);
        var gpuChannel = (IGcuGpuOutputModeChannel)source;
        var status = await gpuChannel.ReadStatusAsync(timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { ReadOnly = true, ConnectionErrors = errors, Status = status }, jsonOptions));
    }
    finally
    {
        timeout.Cancel();
        try { await runTask; }
        catch (OperationCanceledException) { }
    }

    return;
}

if (args.Length == 2 &&
    args[0].Equals("lighting", StringComparison.OrdinalIgnoreCase) &&
    args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await using var source = new GcuMqttStatusSource();
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var connectionErrors = new List<string>();
    var runTask = source.RunAsync(
        update =>
        {
            if (update is OemControlSessionUpdate) connected.TrySetResult();
            return ValueTask.CompletedTask;
        },
        error =>
        {
            connectionErrors.Add($"{error.GetType().Name}: {error.Message}");
            return ValueTask.CompletedTask;
        },
        timeout.Token);

    try
    {
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5), timeout.Token);
        var results = new Dictionary<string, object?>();
        foreach (var zone in Enum.GetValues<LightingZone>())
        {
            try
            {
                var raw = await source.ReadLightingStatusAsync(zone, timeout.Token);
                results[zone.ToString()] = new
                {
                    Raw = raw,
                    Json = JsonDocument.Parse(raw).RootElement.Clone()
                };
            }
            catch (Exception exception)
            {
                results[zone.ToString()] = new
                {
                    Error = $"{exception.GetType().Name}: {exception.Message}"
                };
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ReadOnly = true,
            ConnectionErrors = connectionErrors,
            Zones = results
        }, jsonOptions));
    }
    finally
    {
        timeout.Cancel();
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    return;
}

if (args.FirstOrDefault()?.Equals("oem-status", StringComparison.OrdinalIgnoreCase) is true)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    await using var source = new GcuMqttStatusSource();
    await source.RunAsync(
        update =>
        {
            Console.WriteLine(JsonSerializer.Serialize(update, update.GetType(), jsonOptions));
            return ValueTask.CompletedTask;
        },
        error =>
        {
            Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}");
            return ValueTask.CompletedTask;
        },
        timeout.Token);
    return;
}

var identityReader = new WindowsMachineIdentityReader(new SystemManagementWmiQuery());
var report = CapabilityReportFactory.Create(identityReader.Read());
Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));

static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadWmiState(
    IWmiQuery query,
    string className,
    IReadOnlySet<string> selectedNames)
{
    try
    {
        return query.ReadAll(className)
            .Where(row => row.TryGetValue("Name", out var name) && selectedNames.Contains(name))
            .Select(row => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = row.GetValueOrDefault("Name", string.Empty),
                ["State"] = row.GetValueOrDefault("State", string.Empty),
                ["StartMode"] = row.GetValueOrDefault("StartMode", string.Empty)
            })
            .OrderBy(row => row["Name"], StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    catch (Exception exception)
    {
        return
        [
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Error"] = exception.Message
            }
        ];
    }
}

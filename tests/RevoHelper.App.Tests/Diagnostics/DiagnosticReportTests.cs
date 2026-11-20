using System.IO;
using System.Text;
using RevoHelper.App.Diagnostics;
using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Diagnostics;

public sealed class DiagnosticReportTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 19, 23, 10, 0, TimeSpan.FromHours(8));
    private static readonly DiagnosticPrivacy NoNames = new(null, null, null, null);
    private readonly string folder = Directory.CreateTempSubdirectory("revo-diagnostic-").FullName;

    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void Brief_HasTheMachineTheGpuAndTheStateInOneShortText()
    {
        var text = DiagnosticReportFormatter.Brief(App(), Facts(), At, NoNames);

        Assert.InRange(text.Length, 1, DiagnosticReportFormatter.BriefLimit);
        Assert.StartsWith("RevoHelper 简短诊断 · 1.0.7-beta.5 · 2026-11-19 23:10", text);
        Assert.Contains("机型：MECHREVO YAOSHI16 Ultra", text);
        Assert.Contains("BIOS N.1.08MRO24 · EC 1.8", text);
        Assert.Contains("CPU Intel Core Ultra 9 275HX（24 核 24 线程）", text);
        Assert.Contains("显卡：NVIDIA GeForce RTX 5090 Laptop GPU · 驱动 621.14 · 上限 150/175 W", text);
        Assert.Contains("状态：模式 平衡 · 接电 · 电量 80% · 限充 开 80%", text);
        Assert.Contains("未确认：水冷（未找到水冷设备）", text);
        Assert.Contains("读数：CPU 20% 69 °C 35 W 4104 MHz｜独显 N/A 休眠 N/A N/A", text);
        Assert.Contains("近 24 小时失败 3 条：gcu-connection×2，charge-hold×1", text);
        Assert.DoesNotContain("[显卡功耗", text);                     // the EC table is in the full report only
    }

    [Fact]
    public void Brief_StaysWithinTheLimitWhenEveryNameIsLong()
    {
        var name = new string('长', 300);
        var app = App() with
        {
            OemConnection = name,
            Features = Enumerable.Range(0, 9).Select(index => new DiagnosticFeature($"功能{index}", "不可用", name)).ToArray(),
            Devices = Enumerable.Range(0, 6).Select(_ => new DeviceStatus(DeviceKind.DiscreteGpu, name, name, name, name, name, name, name)).ToArray()
        };
        var facts = Facts() with
        {
            Machine = new(name, name, name, name, name, name, name, name),
            Mode = name,
            Gpus = [new(name, name, name, name, name)],
            RuntimeLog = Enumerable.Range(0, 400).Select(index => $"2026-11-19T10:00:00+08:00 area{index}: 失败").ToArray()
        };

        var text = DiagnosticReportFormatter.Brief(app, facts, At, NoNames);

        Assert.InRange(text.Length, 1, DiagnosticReportFormatter.BriefLimit);
        Assert.StartsWith("RevoHelper 简短诊断", text);
    }

    [Fact]
    public void Brief_DropsTheReadingsBeforeTheFailures()
    {
        // Lengthen the readings until the whole text is one character too long.
        var fits = DiagnosticReportFormatter.Brief(App(), Facts(), At, NoNames);
        var extra = DiagnosticReportFormatter.BriefLimit - fits.Length + 1;
        var app = App() with { Devices = [Cpu(), App().Devices[1], .. Enumerable.Range(0, extra / 20 + 1).Select(_ => Cpu())] };

        var text = DiagnosticReportFormatter.Brief(app, Facts(), At, NoNames);

        Assert.InRange(text.Length, 1, DiagnosticReportFormatter.BriefLimit);
        Assert.DoesNotContain("读数：", text);
        Assert.Contains("近 24 小时失败 3 条", text);
        Assert.Contains("未确认：水冷", text);
    }

    [Theory]
    [InlineData(DiagnosticLogState.Unreadable, "运行日志：没能读取")]
    [InlineData(DiagnosticLogState.NoFile, "运行日志：没有日志文件")]
    [InlineData(DiagnosticLogState.PartlyRead, "近 24 小时失败 3 条（日志只读到一部分）")]
    public void Brief_SaysWhenTheLogWasNotRead(DiagnosticLogState state, string expected)
    {
        var facts = Facts() with { RuntimeLog = state == DiagnosticLogState.PartlyRead ? Facts().RuntimeLog : [], RuntimeLogState = state };
        Assert.Contains(expected, DiagnosticReportFormatter.Brief(App(), facts, At, NoNames));
    }

    [Fact]
    public void Brief_NamesTheGpusWithoutNvidia()
    {
        var app = App() with
        {
            Devices = [Cpu(), new DeviceStatus(DeviceKind.IntegratedGpu, "核显", "AMD Radeon 780M", "", "3%", "N/A", "N/A", "N/A")]
        };
        var facts = Facts() with { Gpus = [], GpuFailure = "未找到 nvidia-smi（没有 NVIDIA 独显驱动）" };

        Assert.Contains("显卡：核显 AMD Radeon 780M（未找到 nvidia-smi（没有 NVIDIA 独显驱动））",
            DiagnosticReportFormatter.Brief(app, facts, At, NoNames));
    }

    [Fact]
    public void Full_HasEverySectionTheGpuReportAndTheLatestLogLines()
    {
        var log = Enumerable.Range(0, 200).Select(index => $"2026-11-19T10:{index / 60:00}:{index % 60:00}+08:00 line{index}: ok").ToArray();
        var facts = Facts() with
        {
            RuntimeLog = log,
            ChargeLog = ["hold-log-20261119-064111.jsonl", "11-19 07:11:15 soc=-1 hold=1 thr=0 timer=0/0 flags=0 ac=1 chg=0 rate=0 retry(access-denied)"],
            GpuPowerReport = "EC 表（每行前 8 字节）：\r\n91C0 32 05"
        };

        var text = DiagnosticReportFormatter.Full(App(), facts, At, NoNames);

        foreach (var section in new[] { "[机器]", "[运行状态]", "[功能检测]", "[设备读数]", "[可能冲突的服务]", "[显卡驱动]", "[显卡功耗（只读 EC）]", "[限充日志（最近 1 条）]" })
            Assert.Contains(section, text);
        Assert.Contains("运行日志（近 24 小时，已读取，最后 150 / 共 200 行）", text);
        Assert.Contains("91C0 32 05", text);
        Assert.Contains("retry(access-denied)", text);
        Assert.Contains("水冷：未确认 · 未找到水冷设备", text);
        Assert.Contains("CPU 控制 Intel RAPL：可用 · PawnIO 已加载", text);
        Assert.Contains("XtuService（Intel XTU）：运行中", text);
        Assert.Contains("line199: ok", text);
        Assert.DoesNotContain("line49: ok", text);                       // only the last 150 lines
    }

    [Fact]
    public void Full_CutsLongLogLines()
    {
        var facts = Facts() with { RuntimeLog = ["2026-11-19T10:00:00+08:00 unhandled-ui: " + new string('x', 2000)] };
        var text = DiagnosticReportFormatter.Full(App(), facts, At, NoNames);
        Assert.DoesNotContain(new string('x', DiagnosticReportFormatter.RuntimeLineLength), text);
    }

    // The cut comes after the check: a path cut in half at 300 characters would no longer look like one.
    [Fact]
    public void Full_CleansALogLineBeforeCuttingIt()
    {
        var line = "2026-11-19T10:00:00+08:00 unhandled-ui: " + new string('x', 245) + @" Could not find 'C:\Users\Jane Doe\Desktop\notes.txt'";
        var text = DiagnosticReportFormatter.Full(App(), Facts() with { RuntimeLog = [line] }, At, NoNames);
        Assert.DoesNotContain("Jane", text);
        Assert.DoesNotContain("Doe", text);
    }

    [Fact]
    public void Brief_KeepsAShortFailureCountWhenTheModulesDoNotFit()
    {
        var areas = Enumerable.Range(0, 4).Select(index => $"2026-11-19T10:00:0{index}+08:00 {new string((char)('a' + index), 40)}: 失败");
        var app = App() with
        {
            Features = Enumerable.Range(0, 6).Select(index => new DiagnosticFeature($"功能{index}", "不可用", new string('因', 20))).ToArray(),
            Devices = Enumerable.Range(0, 4).Select(_ => Cpu()).ToArray()
        };

        var text = DiagnosticReportFormatter.Brief(app, Facts() with { RuntimeLog = areas.ToArray() }, At, NoNames);

        Assert.InRange(text.Length, 1, DiagnosticReportFormatter.BriefLimit);
        Assert.DoesNotContain("读数：", text);
        Assert.Contains("近 24 小时失败 4 条", text);
    }

    [Fact]
    public void Privacy_RemovesFoldersNamesAndAddresses()
    {
        var privacy = new DiagnosticPrivacy("Zephy", "ZEPHY-LAPTOP", "WORKGROUP", @"C:\Users\Zephy");

        var text = privacy.Clean(string.Join("\r\n",
            @"settings: C:\Users\Zephy\AppData\Local\RevoHelper\settings.json",
            "other C:/Users/Zephy/x",
            @"other C:\Users\Someone\Desktop",
            "user=zephy host ZEPHY-LAPTOP mail a.b@example.com payroll@workgroup.com",
            "mac 3C:A6:F6:01:02:03 ip 192.168.1.20 lan 10.0.0.8 dns 8.8.8.8",
            "v6 fe80::1c2b:3a4d and 2001:db8:85a3:0:0:8a2e:370:7334",
            "cooler BluetoothLE#BluetoothLEe0:d4:64:11:22:33-c8:47:8c:44:55:66"));

        foreach (var secret in new[] { "Zephy", "LAPTOP", "Someone", "AppData", "example.com", "workgroup", "3C:A6", "e0:d4", "c8:47",
                     "192.168", "10.0.0.8", "8.8.8.8", "fe80", "2001:db8" })
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"settings: <路径>\settings.json", text);
        Assert.Contains("user=<已隐藏> host <已隐藏> mail <邮箱> <邮箱>", text);
    }

    [Theory]
    // JSON-escaped, as the charge-limit log writes an exception message.
    [InlineData(@"{""Note"":""retry: Access to the path 'C:\\Users\\\u5F20\u4E09\\AppData\\x.json' is denied.""}", @"\u5F20")]
    // A user folder with a space in it, and a folder that is not under Users at all.
    [InlineData(@"Could not find C:\Users\John Smith\Desktop\私人\RevoHelper.exe now", "Smith")]
    [InlineData(@"loaded D:\张三的文件\工具\RevoHelper.exe", "张三")]
    [InlineData(@"at RevoHelper.App.X() in E:\dev\who\RevoHelper\App.xaml.cs:line 88", "who")]
    [InlineData(@"share \\HOME-NAS\photos\a.png", "HOME-NAS")]
    [InlineData(@"long \\?\C:\Users\someone\x.txt", "someone")]
    [InlineData(@"long \\?\UNC\PRIVATE-NAS\share\file.txt", "PRIVATE-NAS")]
    // A file name can be personal too, and an unquoted one may have spaces: only RevoHelper's own names stay.
    [InlineData(@"open D:\Cases\Jane Doe budget.xlsx failed", "Doe")]
    [InlineData(@"open 'D:\Cases\Jane Doe budget.xlsx' failed", "Doe")]
    [InlineData(@"open D:\Cases\Jane.Doe.json", "Jane")]
    public void Privacy_RemovesFoldersInEveryForm(string line, string secret)
    {
        var text = NoNames.Clean(line);
        Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<路径>", text);
    }

    [Fact]
    public void Privacy_KeepsAPlainFileNameAndALineNumber()
    {
        Assert.Equal(@"in <路径>\App.xaml.cs:line 88", NoNames.Clean(@"in E:\dev\who\RevoHelper\App.xaml.cs:line 88"));
        Assert.Equal(@"读取 <路径>\RevoHelper.exe。", NoNames.Clean(@"读取 C:\Program Files\RevoHelper\RevoHelper.exe。"));
        Assert.Equal(@"Could not find file '<路径>\settings.json'.",
            NoNames.Clean(@"Could not find file 'C:\Users\Jane Doe\AppData\Local\RevoHelper\settings.json'."));
    }

    // App.xaml.cs logs the first stack frame of an unhandled exception; with symbols its path has no quotes.
    [Theory]
    [InlineData(@"at RevoHelper.App.MainWindow.X() in D:\Clients\Family、PRIVATE\MainWindow.xaml.cs:line 88",
        @"at RevoHelper.App.MainWindow.X() in <路径>\MainWindow.xaml.cs:line 88")]
    [InlineData(@"at RevoHelper.App.MainWindow.X() in D:\Clients\O'Connor\PRIVATE\MainWindow.xaml.cs:line 88",
        @"at RevoHelper.App.MainWindow.X() in <路径>\MainWindow.xaml.cs:line 88")]
    [InlineData(@"at A.B() in D:\x\y\First.cs:line 1 at C.D() in \\NAS\Jane's work\Second.cs:line 2",
        @"at A.B() in <路径>\First.cs:line 1 at C.D() in <路径>\Second.cs:line 2")]
    [InlineData(@"at A.B() in D:\x\Jane Doe notes.txt:line 3", @"at A.B() in <路径>:line 3")]
    public void Privacy_KeepsOnlyTheSourceFileAndLineOfAStackFrame(string line, string expected) =>
        Assert.Equal(expected, NoNames.Clean(line));

    [Fact]
    public void Privacy_DoesNotFailOnFullWidthDigits() =>
        Assert.Equal("地址 １２７.０.０.１", NoNames.Clean("地址 １２７.０.０.１"));

    [Fact]
    public void Privacy_HidesAChineseUserNameInsideChineseText()
    {
        var privacy = new DiagnosticPrivacy("张三", null, null, @"C:\Users\张三");
        Assert.Equal("<已隐藏>的电脑", privacy.Clean("张三的电脑"));
    }

    [Theory]
    [InlineData("Windows 10.0.26300.0")]
    [InlineData("1.0.7-beta.5+20261120")]
    [InlineData("driver 621.14 · EC 1.8 · 4104 MHz · 69 °C · 20%")]
    [InlineData("11-19 23:35:09 soc=100 hold=1 thr=100 timer=0/21 flags=24 ac=1 chg=0 rate=0 hold")]
    [InlineData("EC 下发/镜像：09EA=37/0788=37 09EC=05/07D5=05")]
    [InlineData("YAOSHI16 Ultra")]
    [InlineData("2026-11-19T10:00:00.0000000+08:00 gcu-connection: connected: GCU 控制通道已连接。")]
    [InlineData("https://github.com/AstralMemory/RevoHelper")]
    public void Privacy_KeepsVersionsTimesAndModelNames(string text) =>
        Assert.Equal(text, new DiagnosticPrivacy("Al", "PC", null, @"C:\Users\Al").Clean(text));

    [Fact]
    public void Privacy_DoesNotEatPartsOfOtherWords()
    {
        // A user called "Ultra" must not turn "YAOSHI16 Ultra2" into a broken name, but the word alone goes.
        var privacy = new DiagnosticPrivacy("Ultra", null, null, null);
        Assert.Equal("YAOSHI16 Ultra2 · <已隐藏>", privacy.Clean("YAOSHI16 Ultra2 · Ultra"));
    }

    [Fact]
    public void ChargeLog_KeepsNumbersAndAFixedReasonButNotTheMessage()
    {
        var line = WindowsDiagnosticReportSource.ProjectChargeLine(
            @"{""At"":""2026-11-16T07:11:15.6678334+00:00"",""Soc"":-1,""Holding"":true,""Threshold"":0,""TimerHigh"":0,""TimerLow"":0," +
            @"""Flags"":0,""PowerOnline"":true,""Charging"":false,""ChargeRateMw"":0,""Note"":""retry: Access to the path 'C:\\Users\\x\\a' is denied."",""Extra"":""secret""}");

        Assert.Equal("11-16 07:11:15 soc=-1 hold=1 thr=0 timer=0/0 flags=0 ac=1 chg=0 rate=0 retry(access-denied)", line);
    }

    [Theory]
    [InlineData(@"{""Note"":""hold""}", "hold")]
    [InlineData(@"{""Note"":""retry: EC method returned the full failure sentinel.""}", "retry(ec-busy)")]
    [InlineData(@"{""Note"":""restore-retry: The EC byte changed externally; the owned write was not issued.""}", "restore-retry(ec-race)")]
    [InlineData(@"{""Note"":""status-write: something new""}", "status-write(other)")]
    [InlineData(@"{""Note"":""anything else""}", "other")]
    public void ChargeLog_NamesTheEvent(string json, string expected) =>
        Assert.EndsWith(" " + expected, WindowsDiagnosticReportSource.ProjectChargeLine(json));

    [Fact]
    public void ChargeLog_SkipsWhatIsNotJson() =>
        Assert.Equal("（无法识别的行）", WindowsDiagnosticReportSource.ProjectChargeLine("C:\\Users\\x not json"));

    [Theory]
    [InlineData("nvidia-smi 退出码 9：NVIDIA-SMI has failed at C:\\Users\\x", "nvidia-smi 退出码 9")]
    [InlineData("未找到 nvidia-smi（没有 NVIDIA 独显驱动）", "未找到 nvidia-smi（没有 NVIDIA 独显驱动）")]
    [InlineData("nvidia-smi 超时", "nvidia-smi 超时")]
    [InlineData("anything else at all", "nvidia-smi 读取失败")]
    public void GpuFailure_IsOneOfAFewFixedReasons(string failure, string expected) =>
        Assert.Equal(expected, WindowsDiagnosticReportSource.SafeGpuFailure(failure));

    [Fact]
    public void Problems_CountsFailuresByArea()
    {
        var problems = DiagnosticReportFormatter.Problems(
        [
            "2026-11-19T10:00:00+08:00 gcu-connection: 连接失败",
            "2026-11-19T10:00:01+08:00 gcu-connection: Exception: x",
            "2026-11-19T10:00:02+08:00 startup: version=1.0.7",
            "2026-11-19T10:00:03+08:00 charge-hold: hold failed",
            "2026-11-19T10:00:04+08:00 performance-schedule: stage=final-readback result=unconfirmed actual=(…)",
            "2026-11-19T10:00:05+08:00 performance-schedule: stage=frequency result=rollback-needed",
            "2026-11-19T10:00:06+08:00 performance-schedule: stage=complete result=success",
            "2026-11-19T10:00:07+08:00 unhandled-ui: NullReferenceException"
        ]);

        Assert.Equal([("gcu-connection", 2), ("performance-schedule", 2), ("charge-hold", 1), ("unhandled-ui", 1)], problems);
    }

    [Fact]
    public void Clip_NeverSplitsASurrogatePair()
    {
        var text = new string('a', 8) + "😀😀";
        var cut = DiagnosticReportFormatter.Clip(text, 10);
        Assert.Equal(new string('a', 8) + "…", cut);
    }

    [Fact]
    public void RuntimeLog_TakesTheLastDayFromBothFiles()
    {
        File.WriteAllLines(Path.Combine(folder, "runtime.log.previous"),
        [
            "2026-11-18T22:00:00.0000000+08:00 old: too old",
            "2026-11-19T00:00:00.0000000+08:00 kept: from previous"
        ]);
        File.WriteAllLines(Path.Combine(folder, "runtime.log"),
        [
            "2026-11-19T23:00:00.0000000+08:00 kept: from current",
            "not a log line"
        ]);

        var (lines, state) = WindowsDiagnosticReportSource.ReadRuntimeLog(folder, At);

        Assert.Equal(DiagnosticLogState.Read, state);
        Assert.Equal(["2026-11-19T00:00:00.0000000+08:00 kept: from previous", "2026-11-19T23:00:00.0000000+08:00 kept: from current"], lines);
    }

    [Fact]
    public void RuntimeLog_SaysWhenThereIsNoFile()
    {
        var (lines, state) = WindowsDiagnosticReportSource.ReadRuntimeLog(Path.Combine(folder, "missing"), At);
        Assert.Empty(lines);
        Assert.Equal(DiagnosticLogState.NoFile, state);
    }

    [Fact]
    public void ChargeLog_TakesTheTailOfTheNewestFile()
    {
        var older = Path.Combine(folder, "hold-log-20261118-010101.jsonl");
        File.WriteAllLines(older, [@"{""Soc"":1,""Note"":""hold""}"]);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-5));
        File.WriteAllLines(Path.Combine(folder, "hold-log-20261119-064111.jsonl"),
            Enumerable.Range(0, 40).Select(index => $@"{{""Soc"":{index},""Note"":""hold""}}"));

        var lines = WindowsDiagnosticReportSource.ReadChargeLog(folder);

        Assert.Equal("hold-log-20261119-064111.jsonl", lines[0]);
        Assert.Equal(WindowsDiagnosticReportSource.ChargeLogLines + 1, lines.Count);
        Assert.Contains("soc=39 ", lines[^1]);
        Assert.Contains("soc=10 ", lines[1]);
    }

    [Fact]
    public async Task ViewModel_ShowsTheReportWithoutCopyingAndHidesTheCurrentUser()
    {
        var source = new FakeSource
        {
            Facts = Facts() with
            {
                RuntimeLog = [$"2026-11-19T10:00:00+08:00 settings: by {Environment.UserName} on {Environment.MachineName} " +
                              $"to {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\AppData\\Local\\RevoHelper\\settings.json"]
            }
        };
        var vm = ViewModel(source);
        Assert.True(vm.CanGenerateDiagnosticReport);
        Assert.False(vm.HasDiagnosticReport);

        Assert.True(await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Full, CancellationToken.None));

        Assert.Equal([DiagnosticReportDetail.Full], source.Requests);
        Assert.StartsWith("RevoHelper 完整诊断", vm.DiagnosticReportText);
        Assert.Contains(@" to <路径>\settings.json", vm.DiagnosticReportText);
        Assert.DoesNotContain("AppData", vm.DiagnosticReportText);
        if (Environment.UserName.Length >= 2) Assert.DoesNotContain(" by " + Environment.UserName, vm.DiagnosticReportText);
        if (Environment.MachineName.Length >= 2) Assert.DoesNotContain(" on " + Environment.MachineName, vm.DiagnosticReportText);
        Assert.Equal($"完整报告 · {vm.DiagnosticReportText.Length} 字", vm.DiagnosticReportSummary);
        Assert.Equal("已生成，还没有复制。请先看一遍内容，再选择复制或保存为文件。", vm.DiagnosticReportStatusText);
        Assert.Matches(@"^RevoHelper-full-report-\d{8}-\d{4}\.txt$", vm.DiagnosticReportFileName);
    }

    [Fact]
    public async Task ViewModel_BriefReportFitsOneMessage()
    {
        var vm = ViewModel(new FakeSource { Facts = Facts() });
        await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Brief, CancellationToken.None);
        Assert.InRange(vm.DiagnosticReportText.Length, 1, DiagnosticReportFormatter.BriefLimit);
        Assert.StartsWith("简短报告 · ", vm.DiagnosticReportSummary);
    }

    [Fact]
    public async Task ViewModel_MakesOneReportAtATime()
    {
        var source = new FakeSource { Facts = Facts(), Gate = new TaskCompletionSource() };
        var vm = ViewModel(source);

        var first = vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Full, CancellationToken.None);
        Assert.False(vm.CanGenerateDiagnosticReport);
        Assert.Equal("正在生成完整报告，读取显卡功耗大约需要 10 秒…", vm.DiagnosticReportStatusText);
        Assert.False(await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Brief, CancellationToken.None));

        source.Gate.SetResult();
        Assert.True(await first);
        Assert.Single(source.Requests);
        Assert.True(vm.CanGenerateDiagnosticReport);
    }

    [Fact]
    public async Task ViewModel_FailureKeepsTheLastReport()
    {
        var source = new FakeSource { Facts = Facts() };
        var vm = ViewModel(source);
        await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Brief, CancellationToken.None);
        var shown = vm.DiagnosticReportText;
        source.Failure = new InvalidOperationException("WMI 不可用");

        Assert.False(await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Full, CancellationToken.None));

        Assert.Equal("生成失败：WMI 不可用", vm.DiagnosticReportStatusText);
        Assert.Equal(shown, vm.DiagnosticReportText);
        Assert.StartsWith("简短报告", vm.DiagnosticReportSummary);
    }

    [Fact]
    public async Task ViewModel_ShuttingDownCancelsAReportAndKeepsTheLastOne()
    {
        var source = new FakeSource { Facts = Facts() };
        var vm = ViewModel(source);
        await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Brief, CancellationToken.None);
        var shown = vm.DiagnosticReportText;
        source.WaitForCancellation = true;

        var running = vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Full, CancellationToken.None);
        vm.BeginShutdown();

        Assert.False(await running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(shown, vm.DiagnosticReportText);
        Assert.Equal("已取消生成。", vm.DiagnosticReportStatusText);
        Assert.False(vm.IsDiagnosticReportBusy);
        Assert.False(vm.CanGenerateDiagnosticReport);
    }

    [Fact]
    public async Task ViewModel_UnavailableWithoutASource()
    {
        var vm = ViewModel(null);
        Assert.False(vm.CanGenerateDiagnosticReport);
        Assert.False(await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Brief, CancellationToken.None));
    }

    [Fact]
    public async Task ViewModel_SavesTheReportThatWasShownWhenSaveWasPressed()
    {
        var vm = ViewModel(new FakeSource { Facts = Facts() });
        await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Brief, CancellationToken.None);
        var shown = vm.DiagnosticReportText;
        // A full report finishes while the save dialog is open.
        await vm.GenerateDiagnosticReportAsync(DiagnosticReportDetail.Full, CancellationToken.None);
        Assert.NotEqual(shown, vm.DiagnosticReportText);
        var path = Path.Combine(folder, "report.txt");

        vm.SaveDiagnosticReport(path, shown);

        Assert.Equal(shown, File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(path)[..3]);
        Assert.Equal("已保存：report.txt", vm.DiagnosticReportStatusText);

        vm.SaveDiagnosticReport(Path.Combine(folder, "missing", "report.txt"), shown);
        Assert.StartsWith("保存失败：", vm.DiagnosticReportStatusText);
    }

    [Theory]
    [InlineData("诊断报告", "Diagnostic report")]
    [InlineData("故障排查", "Troubleshooting")]
    [InlineData("简短报告 · 312 字", "Brief report · 312 characters")]
    [InlineData("已保存：report.txt", "Saved: report.txt")]
    [InlineData("已取消生成。", "Report cancelled.")]
    [InlineData("已生成，还没有复制。请先看一遍内容，再选择复制或保存为文件。", "Created, not copied yet. Read it first, then copy it or save it as a file.")]
    public void EnglishInterfaceHasTheNewText(string chinese, string english) =>
        Assert.Equal(english, UiTextCatalog.Translate(chinese, "en-US"));

    private static MainWindowViewModel ViewModel(IDiagnosticReportSource? source) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "YAOSHI", "X", "CPU", "BIOS", 24, 32), []),
        new ApplicationSettings(), new NullStore(), new NullStartup(),
        diagnosticReport: source);

    private static DiagnosticAppState App() => new(
        "1.0.7-beta.5",
        "balanced",
        80,
        "开 80%",
        "限充运行中：已达上限，当前未充电。",
        true,
        "GCU 控制通道已连接。",
        true,
        [
            new("整机模式", "可用", "已通过当前设备的运行时探测。"),
            new("水冷", "未确认", "未找到水冷设备")
        ],
        [new("Intel RAPL", "可用", "PawnIO 已加载")],
        [Cpu(), new DeviceStatus(DeviceKind.DiscreteGpu, "独立显卡", "NVIDIA GeForce RTX 5090 Laptop GPU", "", "N/A", "休眠", "N/A", "N/A")],
        ["XtuService（Intel XTU）：运行中"],
        24,
        24);

    private static DeviceStatus Cpu() =>
        new(DeviceKind.Cpu, "CPU", "Intel(R) Core(TM) Ultra 9 275HX", "", "20%", "69 °C", "35 W", "4104 MHz");

    private static DiagnosticSystemFacts Facts() => new(
        new("MECHREVO", "YAOSHI16 Ultra", "YAOSHI", "MRO24", "N.1.08MRO24", "1.8", "Intel(R) Core(TM) Ultra 9 275HX", "10.0.26300.0"),
        "平衡",
        true,
        [new("NVIDIA GeForce RTX 5090 Laptop GPU", "621.14", "150", "95", "175")],
        null,
        [
            "2026-11-19T10:00:00+08:00 gcu-connection: 连接失败",
            "2026-11-19T10:00:01+08:00 gcu-connection: 超时",
            "2026-11-19T10:00:02+08:00 charge-hold: hold failed",
            "2026-11-19T10:00:03+08:00 startup: version=1.0.7-beta.5"
        ],
        DiagnosticLogState.Read,
        [],
        null);

    private sealed class FakeSource : IDiagnosticReportSource
    {
        public required DiagnosticSystemFacts Facts { get; init; }
        public TaskCompletionSource? Gate { get; init; }
        public bool WaitForCancellation { get; set; }
        public Exception? Failure { get; set; }
        public List<DiagnosticReportDetail> Requests { get; } = [];

        public async Task<DiagnosticSystemFacts> ReadAsync(DiagnosticReportDetail detail, CancellationToken cancellationToken)
        {
            Requests.Add(detail);
            if (Gate is not null) await Gate.Task;
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Failure is not null) throw Failure;
            return Facts;
        }
    }

    private sealed class NullStore : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class NullStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
}

using System;
using RevoHelper.App.Settings;

namespace RevoHelper.App;

public static class Program
{
    /// <summary>
    /// The pre-sign-in cooler task runs this same executable with <c>--boot</c>. That path
    /// returns before any window code is loaded: it runs as SYSTEM with no desktop.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (BootWaterHost.IsBoot(args)) return BootWaterHost.RunAsync(args).GetAwaiter().GetResult();
        App.Main();
        return Environment.ExitCode;
    }
}

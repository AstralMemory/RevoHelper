using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RevoHelper.Hardware.Windows.Monitoring;

internal static class PresentMonProcessLifetime
{
    /// <summary>A crash of the main application must not leave its FPS collector running.</summary>
    public static SafeFileHandle AttachKillOnCloseJob(Process process)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        var limits = new ExtendedLimitInformation
        {
            BasicLimitInformation = new BasicLimitInformation { LimitFlags = 0x00002000 }
        };
        if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>()) ||
            !AssignProcessToJobObject(job, process.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error);
        }
        return job;
    }

    /// <summary>Only the unique session created by this source is eligible for cleanup. No global termination.</summary>
    public static void StopOwnedTraceSession(string sessionName)
    {
        if (!sessionName.StartsWith("RevoHelper.FPS.", StringComparison.Ordinal)) return;
        var propertySize = Marshal.SizeOf<EventTraceProperties>();
        var bufferSize = propertySize + ((sessionName.Length + 1) * 2);
        var memory = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var properties = new EventTraceProperties
            {
                Wnode = new WnodeHeader { BufferSize = (uint)bufferSize },
                LoggerNameOffset = (uint)propertySize
            };
            Marshal.StructureToPtr(properties, memory, false);
            // ControlTrace uses the supplied sessionName, never a wildcard or a default PresentMon session.
            _ = ControlTrace(0, sessionName, memory, 1);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WnodeHeader
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTraceProperties
    {
        public WnodeHeader Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimitInformation information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")]
    private static extern uint ControlTrace(ulong traceHandle, string instanceName, IntPtr properties, uint controlCode);
}

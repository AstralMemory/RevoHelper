using System.Buffers.Binary;
using System.Runtime.InteropServices;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.PowerPolicy;

/// <summary>
/// Reads the efficiency class Windows assigns to each physical core
/// (GetLogicalProcessorInformationEx with RelationProcessorCore). Read only.
/// </summary>
public static class WindowsProcessorCoreClassReader
{
    private const int RelationProcessorCore = 0;
    private const int ErrorInsufficientBuffer = 122;

    // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship (4 bytes), Size (4 bytes), then
    // PROCESSOR_RELATIONSHIP: Flags (1 byte), EfficiencyClass (1 byte), ...
    private const int EfficiencyClassOffset = 9;

    public static ProcessorCoreClasses? TryRead()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            uint length = 0;
            if (GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length) ||
                Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer ||
                length == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                {
                    return null;
                }

                var bytes = new byte[length];
                Marshal.Copy(buffer, bytes, 0, (int)length);
                return Parse(bytes);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            // Without a reliable answer the caller keeps one cap for all cores.
            return null;
        }
    }

    /// <summary>
    /// Collects the efficiency class of every RelationProcessorCore record. Returns null for
    /// a malformed buffer instead of guessing.
    /// </summary>
    public static ProcessorCoreClasses? Parse(ReadOnlySpan<byte> buffer)
    {
        var classes = new List<int>();
        var offset = 0;
        while (offset < buffer.Length)
        {
            if (buffer.Length - offset < 8)
            {
                return null;
            }

            var relationship = BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(buffer[(offset + 4)..]);
            if (size <= EfficiencyClassOffset || size > buffer.Length - offset)
            {
                return null;
            }

            if (relationship == RelationProcessorCore)
            {
                classes.Add(buffer[offset + EfficiencyClassOffset]);
            }

            offset += size;
        }

        return ProcessorCoreClasses.FromCoreEfficiencyClasses(classes);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref uint returnedLength);
}

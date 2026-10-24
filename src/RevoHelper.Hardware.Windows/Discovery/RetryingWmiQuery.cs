using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class RetryingWmiQuery(
    IWmiQuery inner,
    int maxAttempts = 3,
    TimeSpan? delay = null) : IWmiQuery
{
    private readonly int attempts = maxAttempts > 0
        ? maxAttempts
        : throw new ArgumentOutOfRangeException(nameof(maxAttempts));
    private readonly TimeSpan retryDelay = delay ?? TimeSpan.FromMilliseconds(150);

    public IReadOnlyDictionary<string, string> ReadFirst(string className) =>
        Execute(() => inner.ReadFirst(className));

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className) =>
        Execute(() => inner.ReadAll(className));

    private T Execute<T>(Func<T> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < attempts)
            {
                if (retryDelay > TimeSpan.Zero)
                {
                    Thread.Sleep(retryDelay);
                }
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        COMException { HResult: unchecked((int)0x800700A4) } => true,
        COMException { HResult: unchecked((int)0x8007000E) } => true,
        ManagementException { ErrorCode: ManagementStatus.OutOfMemory } => true,
        _ => false
    };
}

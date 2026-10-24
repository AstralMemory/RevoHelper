using System.Runtime.InteropServices;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Tests.Discovery;

public sealed class RetryingWmiQueryTests
{
    [Fact]
    public void ReadFirst_WhenComTemporarilyFails_RetriesUntilSuccessful()
    {
        var inner = new FlakyWmiQuery(failuresBeforeSuccess: 2);
        var query = new RetryingWmiQuery(inner, maxAttempts: 3, delay: TimeSpan.Zero);

        var result = query.ReadFirst("Win32_Processor");

        Assert.Equal("Intel Processor", result["Name"]);
        Assert.Equal(3, inner.Attempts);
    }

    [Fact]
    public void ReadFirst_WhenFailureIsNotTransient_DoesNotRetry()
    {
        var inner = new InvalidQuery();
        var query = new RetryingWmiQuery(inner, maxAttempts: 3, delay: TimeSpan.Zero);

        Assert.Throws<InvalidOperationException>(() => query.ReadFirst("Invalid_Class"));
        Assert.Equal(1, inner.Attempts);
    }

    [Fact]
    public void ReadFirst_WhenComFailureIsAccessDenied_DoesNotRetry()
    {
        var inner = new FlakyWmiQuery(failuresBeforeSuccess: 2, hresult: unchecked((int)0x80070005));
        var query = new RetryingWmiQuery(inner, maxAttempts: 3, delay: TimeSpan.Zero);

        Assert.Throws<COMException>(() => query.ReadFirst("Win32_Processor"));
        Assert.Equal(1, inner.Attempts);
    }

    [Fact]
    public void ReadAll_WhenComTemporarilyFails_RetriesUntilSuccessful()
    {
        var inner = new FlakyWmiQuery(failuresBeforeSuccess: 1);
        var query = new RetryingWmiQuery(inner, maxAttempts: 2, delay: TimeSpan.Zero);

        var result = query.ReadAll("Win32_VideoController");

        Assert.Single(result);
        Assert.Equal(2, inner.Attempts);
    }

    private sealed class FlakyWmiQuery(
        int failuresBeforeSuccess,
        int hresult = unchecked((int)0x800700A4)) : IWmiQuery
    {
        public int Attempts { get; private set; }

        public IReadOnlyDictionary<string, string> ReadFirst(string className)
        {
            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
            {
                throw new COMException("WMI failure", hresult);
            }

            return new Dictionary<string, string> { ["Name"] = "Intel Processor" };
        }

        public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className) =>
            [ReadFirst(className)];
    }

    private sealed class InvalidQuery : IWmiQuery
    {
        public int Attempts { get; private set; }

        public IReadOnlyDictionary<string, string> ReadFirst(string className)
        {
            Attempts++;
            throw new InvalidOperationException("Invalid query");
        }

        public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className) =>
            throw new InvalidOperationException("Invalid query");
    }
}

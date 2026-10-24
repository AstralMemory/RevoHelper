using System.Runtime.InteropServices;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Settings;

public sealed class BootWaterXmlSchemaTests
{
    [Fact]
    public void WindowsAcceptsDefinitionInMemoryWithoutRegisteringOrRunningATask()
    {
        object? service = null; object? definition = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!);
            ((dynamic)service!).Connect();
            definition = ((dynamic)service).NewTask(0);
            ((dynamic)definition).XmlText = BootWaterTaskDefinition.Create(@"C:\ProgramData\RevoHelper\BootWater\RevoHelper.BootWater.exe");
            string normalized = ((dynamic)definition).XmlText;
            Assert.True(BootWaterTaskDefinition.IsOwned(normalized, @"C:\ProgramData\RevoHelper\BootWater\RevoHelper.BootWater.exe"));
            Assert.True(BootWaterTaskDefinition.IsEnabledAndConfigured(normalized));
        }
        finally
        {
            if (definition is not null) Marshal.FinalReleaseComObject(definition);
            if (service is not null) Marshal.FinalReleaseComObject(service);
        }
    }
}

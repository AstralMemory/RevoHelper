using System.IO;
using System.Runtime.InteropServices;

namespace RevoHelper.App.Settings;

/// <summary>
/// The root folder of Task Scheduler, reached through late-bound COM (<c>dynamic</c>).
/// The C# runtime binder turns a failing HRESULT into the matching .NET exception, so a task that
/// does not exist (0x80070002) arrives as <see cref="FileNotFoundException"/>, not as
/// <see cref="COMException"/>. Catching only the latter made "no task yet" a failure, and start
/// with Windows could not be switched on where it had never been on.
/// </summary>
public static class ScheduledTaskFolder
{
    private const int NotFound = unchecked((int)0x80070002);

    public static bool IsMissingTask(Exception exception) =>
        (exception is COMException or FileNotFoundException) && exception.HResult == NotFound;

    /// <returns>The task's XML, or null when there is no such task.</returns>
    public static string? ReadTaskXml(string taskName) => With<string?>(folder =>
    {
        object? task = null;
        try
        {
            task = folder.GetTask(taskName);
            return (string)((dynamic)task).Xml;
        }
        catch (Exception exception) when (IsMissingTask(exception))
        {
            return null;
        }
        finally
        {
            Release(task);
        }
    });

    /// <summary>
    /// Deletes the task. One already gone (removed elsewhere since it was read) is left as it is;
    /// the caller's read-back decides. Any other failure is raised.
    /// </summary>
    public static void DeleteTask(string taskName) => With<object?>(folder =>
    {
        try
        {
            folder.DeleteTask(taskName, 0);
        }
        catch (Exception exception) when (IsMissingTask(exception))
        {
        }
        return null;
    });

    public static T With<T>(Func<dynamic, T> action)
    {
        object? service = null;
        object? folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!);
            ((dynamic)service!).Connect();
            folder = ((dynamic)service).GetFolder("\\");
            return action(folder);
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    public static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}

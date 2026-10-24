namespace RevoHelper.App.Lifecycle;

public static class BoundedShutdown
{
    public static bool Run(Func<Task> cleanup, TimeSpan timeout)
    {
        var operation = Task.Run(cleanup);
        _ = operation.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { return operation.Wait(timeout); }
        catch (AggregateException) { return false; }
    }
}

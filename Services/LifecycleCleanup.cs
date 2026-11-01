namespace ElyProxy.Services;

public static class LifecycleCleanup
{
    public static async Task RunAsync(params Func<Task>[] steps)
    {
        var errors = new List<Exception>();
        foreach (var step in steps)
        {
            try { await step().ConfigureAwait(false); }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("Очистка соединения завершилась с ошибками.", errors);
    }
}

namespace Orbit.Core;

/// <summary>One piece of work on a short-lived background thread of a chosen apartment, for COM objects that must stay on one thread.</summary>
public static class OwnThread
{
    public static Task<T> RunAsync<T>(ApartmentState apartment, string name, Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (OperationCanceledException)
            {
                done.SetCanceled();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }) { IsBackground = true, Name = name };
        thread.SetApartmentState(apartment);
        thread.Start();
        return done.Task;
    }
}

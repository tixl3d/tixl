namespace Mediapipe.Extension;

internal static class TaskExtension
{
    // polyfill for Task.WaitAsync
    public static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        TaskCompletionSource<T> tcs = new();
        using (cancellationToken.Register(state => ((TaskCompletionSource<T>)state!).TrySetCanceled(), tcs))
        {
            return await (await Task.WhenAny(task, tcs.Task).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}
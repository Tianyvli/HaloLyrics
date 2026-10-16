#nullable enable
using System.Runtime.CompilerServices;
using Windows.Foundation;

namespace HaloLyrics;

// The embedded projection intentionally omits the full Windows SDK async adapters.
internal static class WinRtAsync
{
    public static TaskAwaiter<T> GetAwaiter<T>(this IAsyncOperation<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Completed = (op, _) =>
        {
            try { completion.TrySetResult(op.GetResults()); }
            catch (Exception e) { completion.TrySetException(e); }
            finally { op.Close(); }
        };
        return completion.Task.GetAwaiter();
    }
}

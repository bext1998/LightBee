using System.Diagnostics;

namespace Wcalss.AmbientBrightness;

/// <summary>
/// 執行一個可能不會即時返回的非同步工作，最多等待指定時間。逾時後不取消底層工作
/// （WinRT 的 <c>StopAsync</c>／<c>IClosable</c> 不保證可取消），只是停止等待它，
/// 好讓呼叫端後續的資源釋放（<c>Dispose</c>）一定執行得到。
///
/// 動機見 docs/spike-report.md §13.3／§16：AmbientLightSensor.SampleOnceAsync 的 finally
/// 原本直接 <c>await reader.StopAsync()</c>，一旦它卡住不返回，後面的 <c>mediaCapture.Dispose()</c>
/// 就永遠跑不到，相機控制代碼被 App 持有到 process 結束（症狀：一直取樣失敗、關掉 App 相機才恢復）。
/// </summary>
internal static class AsyncGuard
{
    public static async Task<AsyncGuardResult> RunAsync(Func<Task> operation, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        Task work;
        try
        {
            work = operation();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new AsyncGuardResult(true, stopwatch.ElapsedMilliseconds, ex);
        }

        using var cts = new CancellationTokenSource();
        var delay = Task.Delay(timeout, cts.Token);
        var finished = await Task.WhenAny(work, delay).ConfigureAwait(false);
        stopwatch.Stop();

        if (ReferenceEquals(finished, work))
        {
            cts.Cancel();
            return new AsyncGuardResult(true, stopwatch.ElapsedMilliseconds, work.Exception?.GetBaseException());
        }

        // 逾時：不再等待 work，但觀察它日後可能出現的例外，避免 UnobservedTaskException。
        _ = work.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
        return new AsyncGuardResult(false, stopwatch.ElapsedMilliseconds, null);
    }
}

/// <param name="Completed">工作是否在逾時前結束（拋例外也算結束，不是卡住）。</param>
/// <param name="ElapsedMs">實際等待時間。</param>
/// <param name="Error">工作拋出的例外（若有）；逾時的情況為 <c>null</c>。</param>
internal readonly record struct AsyncGuardResult(bool Completed, long ElapsedMs, Exception? Error);

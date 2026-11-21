using Lib.Utils;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using Xunit;

namespace Lib.Tests.Utils;

public class BackgroundJobTests
{
    private readonly record struct Key(int Value);

    /** Generous: the test runner shares the thread pool with the physics tests running alongside. */
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void AQuickFirstJobIsPublishedOnTheFrameItStarts()
    {
        var output = new Slot<int>();
        var job = new BackgroundJob<Key, int>(null, "test", key => key.Value * 2, output);

        Assert.Equal(BackgroundJobOutcome.Ready, job.Poll(false, new Key(21), out var result));
        Assert.Equal(42, result);
        Assert.Equal(BackgroundJobOutcome.Unchanged, job.Poll(false, new Key(21), out _));
        Assert.Equal(DirtyFlagTrigger.None, output.DirtyFlag.Trigger);
    }

    [Fact]
    public void ASlowJobNeverHoldsUpTheFrameAndKeepsTheOutputPolling()
    {
        using var release = new ManualResetEventSlim();
        var output = new Slot<int>();
        var job = new BackgroundJob<Key, int>(null, "test", key =>
                                                             {
                                                                 release.Wait();
                                                                 return key.Value;
                                                             }, output);

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Assert.Equal(BackgroundJobOutcome.Unchanged, job.Poll(false, new Key(1), out _));
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 200, "polling must not wait for the job");
        Assert.True(job.IsBusy);
        Assert.Equal(DirtyFlagTrigger.Animated, output.DirtyFlag.Trigger);

        release.Set();
        var result = PollUntilReady(job, new Key(1));
        Assert.Equal(1, result);
        Assert.Equal(DirtyFlagTrigger.None, output.DirtyFlag.Trigger);
    }

    [Fact]
    public void TheLatestKeyWinsAndTheRunningJobIsStillPublished()
    {
        using var release = new ManualResetEventSlim();
        var runs = 0;
        var job = new BackgroundJob<Key, int>(null, "test", key =>
                                                             {
                                                                 Interlocked.Increment(ref runs);
                                                                 release.Wait();
                                                                 return key.Value;
                                                             }, new Slot<int>());

        job.Poll(false, new Key(1), out _);
        job.Poll(false, new Key(2), out _);
        job.Poll(false, new Key(3), out _);
        release.Set();

        // The job for 1 finishes first and is shown; 2 was overtaken while waiting and never runs.
        var published = new List<int>();
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline && (published.Count == 0 || published[^1] != 3))
        {
            if (job.Poll(false, new Key(3), out var value) == BackgroundJobOutcome.Ready)
                published.Add(value);
            Thread.Sleep(2);
        }

        Assert.Equal([1, 3], published);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void AFailingKeyIsTriedOnceUntilTheInputChanges()
    {
        var runs = 0;
        var job = new BackgroundJob<Key, int>(null, "test", key =>
                                                             {
                                                                 Interlocked.Increment(ref runs);
                                                                 return key.Value < 0 ? throw new ArgumentException("negative") : key.Value;
                                                             }, new Slot<int>());

        Assert.Equal(BackgroundJobOutcome.Failed, PollUntilDone(job, new Key(-1)));
        for (var frame = 0; frame < 10; frame++)
            Assert.Equal(BackgroundJobOutcome.Unchanged, job.Poll(false, new Key(-1), out _));

        Assert.Equal(1, runs);
        Assert.Equal("negative", job.LastError);
        Assert.Equal(BackgroundJobOutcome.Ready, PollUntilDone(job, new Key(5)));
        Assert.Null(job.LastError);
    }

    [Fact]
    public void RenderingToAFileWaitsForEveryResult()
    {
        var job = new BackgroundJob<Key, int>(null, "test", key =>
                                                             {
                                                                 Thread.Sleep(60);
                                                                 return key.Value;
                                                             }, new Slot<int>());

        // The first one is published inline whatever the mode; every later one too, when rendering to a file.
        Assert.Equal(BackgroundJobOutcome.Ready, job.Poll(true, new Key(1), out var first));
        Assert.Equal(1, first);
        Assert.Equal(BackgroundJobOutcome.Ready, job.Poll(true, new Key(2), out var second));
        Assert.Equal(2, second);
    }

    [Fact]
    public void NaNInAKeyStillMatchesItself()
    {
        var runs = 0;
        var job = new BackgroundJob<(float, int), int>(null, "test", _ => Interlocked.Increment(ref runs), new Slot<int>());
        PollUntilDone(job, (float.NaN, 1));
        for (var frame = 0; frame < 5; frame++)
            job.Poll(false, (float.NaN, 1), out _);

        Assert.Equal(1, runs);
    }

    private static int PollUntilReady(BackgroundJob<Key, int> job, Key key)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (job.Poll(false, key, out var result) == BackgroundJobOutcome.Ready)
                return result;
            Thread.Sleep(2);
        }

        throw new TimeoutException("the job never finished");
    }

    private static BackgroundJobOutcome PollUntilDone<TKey>(BackgroundJob<TKey, int> job, TKey key) where TKey : IEquatable<TKey>
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var outcome = job.Poll(false, key, out _);
            if (outcome != BackgroundJobOutcome.Unchanged)
                return outcome;
            Thread.Sleep(2);
        }

        throw new TimeoutException("the job never finished");
    }
}

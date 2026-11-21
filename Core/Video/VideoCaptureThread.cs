using System;
using System.Threading;

namespace T3.Core.Video;

public sealed class VideoFrameReceivedEventArgs : EventArgs
{
    private VideoFrame frame;

    public VideoFrame Frame => this.frame;

    public VideoFrameReceivedEventArgs(VideoFrame frame)
    {
        this.frame = frame;
    }
}

public abstract class VideoCaptureThread
{
    private Thread captureThread;
    private volatile bool isRunning;
    public int ThreadSleepTime = 1;

    public EventHandler<VideoFrameReceivedEventArgs> FrameReceived;

    public void Start()
    {
        if (this.isRunning)
            return;

        this.isRunning = true;
        this.Configure();
        this.captureThread = new Thread(new ThreadStart(this.Run)) { IsBackground = true };
        this.captureThread.Start();
    }

    public void Stop()
    {
        if (!this.isRunning)
            return;

        // The flag has to drop first: the loop only ends on it, so joining before would wait forever.
        this.isRunning = false;
        this.captureThread.Join();
    }

    private void Run()
    {
        while (this.isRunning)
        {
            VideoFrame? frame = this.Capture();

            if (frame.HasValue)
            {
                this.FrameReceived?.Invoke(this, new VideoFrameReceivedEventArgs(frame.Value));
            }
            else
            {
                if (this.ThreadSleepTime > 0)
                    Thread.Sleep(this.ThreadSleepTime);
            }
        }
    }

    protected abstract void Configure();
    protected abstract VideoFrame? Capture();

    public void Dispose()
    {
        this.Stop();
    }
}
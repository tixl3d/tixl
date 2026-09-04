namespace SilkWindows;

public abstract class AsyncImguiDrawer<T> : IImguiDrawer<T>
{
    private T? _result;
    public T? Result
    {
        get => _result;
        protected set
        {
            _result = value;
            
            _resultEvent.Set();
            _resultEvent.WaitOne();
            _resultEvent.Reset();
            
            if (CloseOnResult)
            {
                _cts.Cancel();
            }
        }
    }
    
    private readonly AutoResetEvent _resultEvent = new(false);
    
    public async IAsyncEnumerable<T?> GetResults()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Run(() =>
                               {
                                   _resultEvent.WaitOne();
                                   _resultEvent.Set();
                               }, _cts.Token)
                          .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            
            yield return Result;
        }
    }
    
    public abstract void Init();
    
    public abstract void OnRender(string windowName, double deltaSeconds, ImFonts fonts);
    
    public void OnWindowUpdate(double deltaSeconds, out bool shouldClose)
    {
        OnWindowUpdateImpl(deltaSeconds);
        shouldClose = _cts.IsCancellationRequested;
    }
    
    public void OnClose()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
        
        _cts.Dispose();
        _resultEvent.Reset();
        
        ClosingCallback?.Invoke();
    }
    
    public abstract void OnFileDrop(string[] filePaths);
    
    public abstract void OnWindowFocusChanged(bool changedTo);
    
    private readonly CancellationTokenSource _cts = new();
    protected abstract void OnWindowUpdateImpl(double deltaSeconds);
    
    public bool CloseOnResult { get; init; } = true;
    
    public Action? ClosingCallback { get; init; }
    
    public void ForceClose() => _cts.Cancel();
}
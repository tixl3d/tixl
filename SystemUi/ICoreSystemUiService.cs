namespace T3.SystemUi;

public interface ICoreSystemUiService
{
    public void OpenWithDefaultApplication(string uri);
    public void ExitApplication();
    public void ExitThread();
    
    public ICursor Cursor { get; }
    void SetUnhandledExceptionMode(bool throwException);
}

public interface IMessageBoxProvider
{
    public T? ShowMessageBox<T>(string text, string title, Func<T, string> toString, params T[] buttons);
    public string? ShowMessageBox(string text, string title, params string[] buttons) => ShowMessageBox(text, title, str => str, buttons);
    public void ShowMessageBox(string text, string title);
    public void ShowMessageBox(string message);
}

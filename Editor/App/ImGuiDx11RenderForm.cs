using System.Windows.Forms;
using ImGuiNET;
using SharpDX.Windows;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
namespace T3.Editor.App;

/// <summary>
/// A RenderForm class that maps windows RenderForm events and to ImGui 
/// </summary>
internal class ImGuiDx11RenderForm : RenderForm
{
    internal static IWindowsFormsMessageHandler[] InputMethods = [];

    internal ImGuiDx11RenderForm(string title): base(title)
    {
        AllowDrop = true;

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;

        MouseMove += (_, e) =>
        {
            if (this != ProgramWindows.Viewer?.Form) // Ignore mouse updates from Viewer
            {
                ImGui.GetIO().MousePos = new System.Numerics.Vector2(e.X, e.Y);
            }
        };
    }

    #region WM Message Ids

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_RBUTTONDBLCLK = 0x0206;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MBUTTONDBLCLK = 0x0209;

    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_CHAR = 0x0102;
    private const int WM_SETCURSOR = 0x0020;

    private const int WM_SETFOCUS = 0x0007;
    private const int WM_ACTIVATEAPP = 0x001C;

    #endregion

    #region VK constants

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_ALT = 0x12;

    #endregion

    public static event Action<string[], Vector2> FilesDropped;

    private void OnDragEnter(object s, DragEventArgs e)
    {
        if (this == ProgramWindows.Viewer?.Form)
        {
            e.Effect = DragDropEffects.None;
            return;
        } // optional

        if (e.Data == null)
        {
            return;
        }

        e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ||
                   e.Data.GetDataPresent(DataFormats.UnicodeText)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        DragAndDropHandling.StartExternalDrag(DragAndDropHandling.DragTypes.ExternalFile,
            "External Files");
    }

    private static void OnDragLeave(object s, EventArgs eventArgs)
    {
        DragAndDropHandling.CancelExternalDrag();
    }

    private void OnDragDrop(object s, DragEventArgs e)
    {
        if (this == ProgramWindows.Viewer?.Form || e.Data == null)
            return; // optional

        var p = PointToClient(new System.Drawing.Point(e.X, e.Y));
        var pos = new Vector2(p.X, p.Y);

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[]) e.Data.GetData(DataFormats.FileDrop)!;

            // A single dropped folder opens the import dialog instead of the per-file drop zones
            if (files.Length == 1 && System.IO.Directory.Exists(files[0]))
            {
                DragAndDropHandling.CancelExternalDrag();
                Gui.Windows.AssetLib.FolderImportDialog.ShowForFolder(files[0]);
                return;
            }

            FilesDropped?.Invoke(files, pos);
            DragAndDropHandling.CompleteExternalDrop(DragAndDropHandling.DragTypes.ExternalFile,
                string.Join("|", files));
        }
        else if (e.Data.GetDataPresent(DataFormats.UnicodeText))
        {
            var t = ((string) e.Data.GetData(DataFormats.UnicodeText)!).Trim('"');
            if (System.IO.Path.IsPathRooted(t))
            {
                DragAndDropHandling.CompleteExternalDrop(DragAndDropHandling.DragTypes.ExternalFile, t);
                FilesDropped?.Invoke(new[] {t}, pos);
            }
        }
    }

    private void OnDragOver(object s, DragEventArgs e)
    {
        var p = PointToClient(new System.Drawing.Point(e.X, e.Y));
        ImGui.GetIO().MousePos = new System.Numerics.Vector2(p.X, p.Y);

        e.Effect = DragDropEffects.Copy;
    }

    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        try
        {
            var filterAltKeyToPreventFocusLoss =
                (m.Msg == WM_SYSKEYDOWN || m.Msg == WM_SYSKEYUP) && (int) m.WParam == VK_ALT;
            if (!filterAltKeyToPreventFocusLoss)
                base.WndProc(ref m);

            // The form's HWND can receive messages before WindowsUiContentDrawer.Initialize()
            // calls ImGui.CreateContext(). ImGui 1.91 hard-asserts on GetIO() when no context
            // exists, where 1.89 was lax. Bail out for those very early messages.
            if (ImGui.GetCurrentContext() == IntPtr.Zero)
                return;

            foreach (var inputMethod in InputMethods)
                inputMethod.ProcessMessage(m);

            var isViewer = this == ProgramWindows.Viewer?.Form;

            ImGuiIOPtr io = ImGui.GetIO();

            switch (m.Msg)
            {
                case WM_LBUTTONDOWN:
                case WM_LBUTTONDBLCLK:
                case WM_RBUTTONDOWN:
                case WM_RBUTTONDBLCLK:
                case WM_MBUTTONDOWN:
                case WM_MBUTTONDBLCLK:
                {
                    if (isViewer)
                        return;

                    int button = 0;
                    if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK) button = 0;
                    if (m.Msg == WM_RBUTTONDOWN || m.Msg == WM_RBUTTONDBLCLK) button = 1;
                    if (m.Msg == WM_MBUTTONDOWN || m.Msg == WM_MBUTTONDBLCLK) button = 2;
                    // TODO
                    //if (!ImGui.IsAnyMouseDown() && ::GetCapture() == NULL)
                    //    ::SetCapture(hwnd);
                    io.MouseDown[button] = true;
                    return;
                }
                case WM_LBUTTONUP:
                case WM_RBUTTONUP:
                case WM_MBUTTONUP:
                {
                    int button = 0;
                    if (m.Msg == WM_LBUTTONUP) button = 0;
                    if (m.Msg == WM_RBUTTONUP) button = 1;
                    if (m.Msg == WM_MBUTTONUP) button = 2;
                    io.MouseDown[button] = false;
                    // TODO
                    //if (!ImGui::IsAnyMouseDown() && ::GetCapture() == hwnd)
                    //    ::ReleaseCapture();
                    return;
                }
                case WM_MOUSEWHEEL:
                case WM_MOUSEHWHEEL:
                {
                    MouseWheelPanning.ProcessMouseWheelInput(m, io);
                    return;
                }

                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                    HandleKeyEvent(io, (int)m.WParam, down: true);
                    return;
                case WM_KEYUP:
                case WM_SYSKEYUP:
                    HandleKeyEvent(io, (int)m.WParam, down: false);
                    return;
                case WM_CHAR:
                    // You can also use ToAscii()+GetKeyboardState() to retrieve characters.
                    if ((int) m.WParam > 0 && (int) m.WParam < 0x10000)
                        io.AddInputCharacter((ushort) m.WParam);
                    return;
                case WM_SETCURSOR:
                    if ((((int) m.LParam & 0xFFFF) == 1) && UpdateMouseCursor())
                        m.Result = 1;
                    return;
                case WM_SETFOCUS:
                    io.AddFocusEvent(true);
                    SyncModifiersFromSystemState(io);
                    break;

                case WM_ACTIVATEAPP:
                    if (m.WParam.ToInt64() == 0) /* Being deactivated */
                    {
                        // Clear all key state to avoid stuck keys when focus is lost
                        // (especially Alt, which can desync via Alt+Tab).
                        io.AddFocusEvent(false);
                        io.ClearInputKeys();
                        KeyHandler.ReleaseAllKeys();
                    }
                    else
                    {
                        SyncModifiersFromSystemState(io);
                    }

                    break;
            }
        }
        catch (NullReferenceException)
        {
            Log.Warning("Detected invalid event message that would trigger null-reference exception");
        }
    }

    /// <summary>
    /// Forwards a single Win32 keyboard event to ImGui's new input system,
    /// updates ImGui's modifier flags, and keeps TiXL's <see cref="KeyHandler"/>
    /// state in sync for the Alt key (used by the WndProc filter above).
    /// </summary>
    private static void HandleKeyEvent(ImGuiIOPtr io, int vkCode, bool down)
    {
        // Modifier flags — set both the dedicated mod event (used by ImGui shortcut
        // matching) and the legacy bool fields (used by InputText etc.).
        switch (vkCode)
        {
            case VK_SHIFT:
                io.KeyShift = down;
                io.AddKeyEvent(ImGuiKey.ModShift, down);
                break;
            case VK_CONTROL:
                io.KeyCtrl = down;
                io.AddKeyEvent(ImGuiKey.ModCtrl, down);
                break;
            case VK_ALT:
                io.KeyAlt = down;
                io.AddKeyEvent(ImGuiKey.ModAlt, down);
                if (down)
                    KeyHandler.SetKeyDown(Key.Alt);
                else
                    KeyHandler.SetKeyUp(Key.Alt);
                break;
        }

        // Forward the actual key as an ImGuiKey event.
        var imguiKey = ((Key)vkCode).ToImGuiKey();
        if (imguiKey != ImGuiKey.None)
            io.AddKeyEvent(imguiKey, down);
    }

    /// <summary>
    /// Releases modifiers that are no longer physically held, according to Windows.
    /// Key-up events are lost while the app is unfocused or halted at a debugger
    /// breakpoint, so modifiers are re-checked whenever focus returns.
    ///
    /// This only ever *releases* keys: GetKeyState reflects the thread's synchronous
    /// key state, which can report a stale "down" right after refocusing (the release
    /// was delivered to the previously focused app). Synthesizing a key-down from it
    /// would stick the modifier with no key-up ever arriving. A genuinely held
    /// modifier re-registers through its auto-repeat WM_KEYDOWN messages.
    /// </summary>
    private static void SyncModifiersFromSystemState(ImGuiIOPtr io)
    {
        if (!IsVkDown(VK_SHIFT))
        {
            io.KeyShift = false;
            io.AddKeyEvent(ImGuiKey.ModShift, false);
            KeyHandler.SetKeyUp(Key.ShiftKey);
        }

        if (!IsVkDown(VK_CONTROL))
        {
            io.KeyCtrl = false;
            io.AddKeyEvent(ImGuiKey.ModCtrl, false);
            KeyHandler.SetKeyUp(Key.CtrlKey);
        }

        if (!IsVkDown(VK_ALT))
        {
            io.KeyAlt = false;
            io.AddKeyEvent(ImGuiKey.ModAlt, false);
            KeyHandler.SetKeyUp(Key.Alt);
        }
    }

    private static bool IsVkDown(int vkCode) => (GetKeyState(vkCode) & 0x8000) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    /// <summary>
    /// Applies the cursor ImGui requested this frame right away. Windows only sends
    /// WM_SETCURSOR on the next mouse movement, so waiting for it makes the pointer
    /// shape trail one movement step behind the hover state — very noticeable on
    /// small drag handles and splitters.
    /// </summary>
    internal void ApplyRequestedCursor()
    {
        var requested = ImGui.GetMouseCursor();
        if (requested == _lastRequestedCursor)
            return;

        _lastRequestedCursor = requested;

        // Outside our client area the cursor belongs to whoever is under it
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
            return;

        UpdateMouseCursor();
    }

    private ImGuiMouseCursor _lastRequestedCursor = ImGuiMouseCursor.Arrow;

    private bool UpdateMouseCursor()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (((uint) io.ConfigFlags & (uint) ImGuiConfigFlags.NoMouseCursorChange) > 0)
            return false;

        ImGuiMouseCursor imgui_cursor = ImGui.GetMouseCursor();
        if (imgui_cursor == ImGuiMouseCursor.None || io.MouseDrawCursor)
        {
            // Hide OS mouse cursor if imgui is drawing it or if it wants no cursor
            Cursor.Current = null;
            return true;
        }

        Cursor newCursor;

        // Show OS mouse cursor
        switch (imgui_cursor)
        {
            case ImGuiMouseCursor.Arrow:
                newCursor = Cursors.Arrow;
                break;
            case ImGuiMouseCursor.TextInput:
                newCursor = Cursors.IBeam;
                break;
            case ImGuiMouseCursor.ResizeAll:
                newCursor = Cursors.SizeAll;
                break;
            case ImGuiMouseCursor.ResizeEW:
                newCursor = Cursors.SizeWE;
                break;
            case ImGuiMouseCursor.ResizeNS:
                newCursor = Cursors.SizeNS;
                break;
            case ImGuiMouseCursor.ResizeNESW:
                newCursor = Cursors.SizeNESW;
                break;
            case ImGuiMouseCursor.ResizeNWSE:
                newCursor = Cursors.SizeNWSE;
                break;
            case ImGuiMouseCursor.Hand:
                newCursor = Cursors.Hand;
                break;
            default:
                newCursor = Cursors.Arrow;
                break;
        }

        if (Cursor.Current != newCursor)
        {
            Cursor = newCursor;

            // Apply immediately - the next WM_SETCURSOR only comes with the next mouse move
            Cursor.Current = newCursor;
        }

        return true;
    }
}
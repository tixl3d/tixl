using System;
using System.Numerics;
using System.Windows.Forms;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.SystemUi;
using MouseButtons = System.Windows.Forms.MouseButtons;

namespace T3.Player;

internal static partial class Program
{
    private static void InitializeInput(Control windowControl)
    {
        MsForms.MsForms.TrackKeysOf(_renderForm);
        windowControl.KeyUp += OnRenderFormOnKeyUp;
        windowControl.MouseMove += MouseMoveHandler;
        windowControl.MouseClick += MouseMoveHandler;
    }
    
    private static void OnRenderFormOnKeyUp(object _, KeyEventArgs keyArgs)
    {
        var coreUi = CoreUi.Instance;
        if (_resolvedOptions.Windowed && keyArgs is { Alt: true, KeyCode: Keys.Enter })
        {
            _swapChain.IsFullScreen = !_swapChain.IsFullScreen;
            RebuildBackBuffer(_renderForm, _device, ref _renderView, ref _backBuffer, _swapChain);
            coreUi.Cursor.SetVisible(!_swapChain.IsFullScreen);
        }

        var currentPlayback = Playback.Current;
        if (CompositionSettings.Current.Export.EnablePlaybackControlWithKeyboard)
        {
            switch (keyArgs.KeyCode)
            {
                case Keys.Left:
                    currentPlayback.TimeInBars -= 4;
                    break;
                case Keys.Right:
                    currentPlayback.TimeInBars += 4;
                    break;
                case Keys.Space:
                    currentPlayback.PlaybackSpeed = Math.Abs(currentPlayback.PlaybackSpeed) > 0.01f ? 0 : 1;
                    break;
            }
        }

        if (keyArgs.KeyCode == Keys.Escape)
        {
            coreUi.ExitApplication();
        }
    }

    private static void MouseMoveHandler(object sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (sender is not Form form)
            return;

        var relativePosition = new Vector2((float)e.X / form.Size.Width,
                                           (float)e.Y / form.Size.Height);

        MouseInput.Set(relativePosition, (e.Button & MouseButtons.Left) != 0);
    }
}
using System;
using SDL;
using T3.Core.Animation;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.SdlPlatform;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.Player;

internal static partial class Program
{
    /// <summary>
    /// Handles all pending window events. Returns false once a window was closed: during loading that cancels,
    /// afterwards it ends the render loop.
    /// </summary>
    private static unsafe bool PumpEvents()
    {
        SDL_Event sdlEvent;
        while (SDL_PollEvent(&sdlEvent))
        {
            switch (sdlEvent.Type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                    _closeRequested = true;
                    break;

                case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                    // Key-up events never arrive while another application has focus.
                    KeyHandler.ReleaseAllKeys();
                    break;

                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                    KeyHandler.SetKeyDown(SdlKeyMap.ToVirtualKey(sdlEvent.key.key));
                    break;

                case SDL_EventType.SDL_EVENT_KEY_UP:
                    var virtualKey = SdlKeyMap.ToVirtualKey(sdlEvent.key.key);
                    KeyHandler.SetKeyUp(virtualKey);
                    OnKeyUp((Key)virtualKey, (sdlEvent.key.mod & SDL_Keymod.SDL_KMOD_ALT) != 0);
                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_MOTION when sdlEvent.motion.windowID == _mainWindow?.Id:
                    _isLeftMouseButtonDown = (sdlEvent.motion.state & SDL_MouseButtonFlags.SDL_BUTTON_LMASK) != 0;
                    MouseInput.Set(_mainWindow.ToRelativePosition(sdlEvent.motion.x, sdlEvent.motion.y), _isLeftMouseButtonDown);
                    break;

                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP
                    when sdlEvent.button.windowID == _mainWindow?.Id && sdlEvent.button.Button == SDLButton.SDL_BUTTON_LEFT:
                    _isLeftMouseButtonDown = sdlEvent.button.down;
                    MouseInput.Set(_mainWindow.ToRelativePosition(sdlEvent.button.x, sdlEvent.button.y), _isLeftMouseButtonDown);
                    break;
            }
        }

        return !_closeRequested;
    }

    private static void OnKeyUp(Key key, bool isAltDown)
    {
        if (isAltDown && key == Key.Return)
        {
            SetBorderlessFullScreen(!_isFullScreen);
        }

        var currentPlayback = Playback.Current;
        if (CompositionSettings.Current.Export.EnablePlaybackControlWithKeyboard)
        {
            switch (key)
            {
                case Key.CursorLeft:
                    currentPlayback.TimeInBars -= 4;
                    break;
                case Key.CursorRight:
                    currentPlayback.TimeInBars += 4;
                    break;
                case Key.Space:
                    currentPlayback.PlaybackSpeed = Math.Abs(currentPlayback.PlaybackSpeed) > 0.01f ? 0 : 1;
                    break;
            }
        }

        if (key == Key.Esc)
        {
            if (_isLoading)
            {
                // The loading sequence checks this between steps and shuts down cleanly
                _loadCancelled = true;
            }
            else
            {
                CoreUi.Instance.ExitApplication();
            }
        }
    }

    private static bool _closeRequested;
    private static bool _isLeftMouseButtonDown;
}

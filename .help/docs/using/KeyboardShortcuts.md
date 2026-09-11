
## Graph Mouse Gestures

| Action | Gesture |
| --- | --- |
| Route Connections through Anchors | Shift+Right Mouse Button drag across cables |
| Cut Connections | Ctrl+Right Mouse Button drag across cables |
| Merge Anchors | Drag one anchor onto another anchor of the same type and release |

Hold the modifier, press the right mouse button, and draw across the connections you want to change. Release to apply the preview. Each stroke is one undo step. Press Escape to cancel; release the mouse button before starting another gesture. Ctrl+Shift+Right Mouse Button makes no change.

Routing creates one small anchor for each source output crossed by the stroke. Branches from the same output share an anchor; different outputs keep separate anchors, even when their types match. Uncrossed branches stay connected as before.

Drag an anchor's center to move it, its left socket to connect an input, or its right socket to connect an output. Anchors retain their type while either side remains connected. Disconnecting or cutting the last remaining cable removes the anchor automatically; shake and Disconnect do the same. Undo restores the anchor and its connections together. Reconnecting a wire keeps the anchor, and a newly added blank anchor stays available for wiring.

Drop one anchor's center inside the 32×32 canvas area centered on another anchor of the same type to merge them. The stationary anchor stays in place and receives the dragged anchor's outgoing cables. Its input wins when both have sources; otherwise the dragged anchor's input is retained. Directly connected anchors collapse without connecting an anchor to itself. A merge that would create a cycle is refused. Moving a group does not merge anchors. One undo restores both anchors, their cables, and the original position.

Anchors can be selected, copied, duplicated, and deleted like other operators. Deleting an anchor removes its cables. Automatic layout preserves manually placed anchors, and dragging them near other nodes does not create magnetic connections. Bypass leaves anchors unchanged because they already forward their input.

Routing supports the built-in ordinary slot types. A stroke containing an unsupported type, a composition multi-input bundle source, or an output carrying clip metadata is rejected without changing its cables. Individual connections into multi-input targets can be routed and cut. These gestures apply to editable graphs and take precedence over canvas pan/zoom while held.

## Hotkeys

The following is a list of the **default keyboard** short-cuts. With v4.0.4 you can customize this list in the *Settings*.

 | action                         | Key                                                |
 | ------------------------------ | -------------------------------------------------- |
 | Add New Operator               | Tab (Context search type name)                     |
 | PlaybackForward                | L                                                  |
 | PlaybackForwardHalfSpeed       | Shift + L                                          |
 | PlaybackBackwards              | J                                                  |
 | PlaybackStop                   | K                                                  |
 | PlaybackPreviousFrame          | Shift + CursorLeft                                 |
 | PlaybackNextFrame              | Shift + CursorRight                                |
 | PlaybackJumpToNextKeyframe     | Period                                             |
 | PlaybackJumpToPreviousKeyframe | Comma                                              |
 | PlaybackNextFrame              | Shift + CursorRight                                |
 | PlaybackJumpBack               | B                                                  |
 | Undo                           | ctrl + Z                                           |
 | Redo                           | ctrl + shift + Z                                   |
 | Save                           | ctrl + S                                           |
 | FocusSelection                 | F (NeedsWindowHover)                               |
 | Duplicate                      | ctrl + D (NeedsWindowFocus)                        |
 | DuplicateWithConnections       | ctrl + shift + D (NeedsWindowFocus)                |
 | DeleteSelection                | Delete (NeedsWindowFocus)                          |
 | DeleteSelection                | Backspace (NeedsWindowFocus)                       |
 | CopyToClipboard                | ctrl + C (NeedsWindowFocus)                        |
 | PasteFromClipboard             | ctrl + V (NeedsWindowFocus)                        |
 | InsertKeyframe                 | C (NeedsWindowFocus)                               |
 | InsertKeyframeWithIncrement    | C, shift (NeedsWindowFocus)                        |
 | ToggleDisabled                 | Shift + D (NeedsWindowFocus)                       |
 | ToggleBypassed                 | Shift + B (NeedsWindowFocus)                       |
 | Disconnect                     | Alt + D (NeedsWindowFocus)                         |
 | PinToOutputWindow              | P (NeedsWindowFocus)                               |
 | DisplayImageAsBackground       | ctrl + P                                           |
 | ClearBackgroundImage           | ctrl + P (NeedsWindowFocus) or use Clear BG button |
 | LoadBookmark1                  | ctrl + D1                                          |
 | LoadBookmark2                  | ctrl + D2                                          |
 | LoadBookmark3                  | ctrl + D3                                          |
 | LoadBookmark4                  | ctrl + D4                                          |
 | LoadBookmark5                  | ctrl + D5                                          |
 | LoadBookmark6                  | ctrl + D6                                          |
 | LoadBookmark7                  | ctrl + D7                                          |
 | LoadBookmark8                  | ctrl + D8                                          |
 | LoadBookmark9                  | ctrl + D9                                          |
 | LoadBookmark0                  | ctrl + D0                                          |
 | SaveBookmark1                  | ctrl + D1, shift                                   |
 | SaveBookmark2                  | ctrl + D2, shift                                   |
 | SaveBookmark3                  | ctrl + D3, shift                                   |
 | SaveBookmark4                  | ctrl + D4, shift                                   |
 | SaveBookmark5                  | ctrl + D5, shift                                   |
 | SaveBookmark6                  | ctrl + D6, shift                                   |
 | SaveBookmark7                  | ctrl + D7, shift                                   |
 | SaveBookmark8                  | ctrl + D8, shift                                   |
 | SaveBookmark9                  | ctrl + D9, shift                                   |
 | SaveBookmark0                  | ctrl + D0, shift                                   |
 | LoadLayout0                    | F1                                                 |
 | LoadLayout1                    | F2                                                 |
 | LoadLayout2                    | F3                                                 |
 | LoadLayout3                    | F4                                                 |
 | LoadLayout4                    | F5                                                 |
 | LoadLayout5                    | F6                                                 |
 | LoadLayout6                    | F7                                                 |
 | LoadLayout7                    | F8                                                 |
 | LoadLayout8                    | F9                                                 |
 | LoadLayout9                    | F10                                                |
 | SaveLayout0                    | ctrl + F1                                          |
 | SaveLayout1                    | ctrl + F2                                          |
 | SaveLayout2                    | ctrl + F3                                          |
 | SaveLayout3                    | ctrl + F4                                          |
 | SaveLayout4                    | ctrl + F5                                          |
 | SaveLayout5                    | ctrl + F6                                          |
 | SaveLayout6                    | ctrl + F7                                          |
 | SaveLayout7                    | ctrl + F8                                          |
 | SaveLayout8                    | ctrl + F9                                          |
 | SaveLayout9                    | ctrl + F10                                         |
 | LayoutSelection                | G                                                  |
 | ToggleFullScreenGraph          | ctrl + F11                                         |
 | ToggleFocusMode                | Shift + Esc                                        |
 | AddSection                    | Shift+S, alias Shift+A (NeedsWindowFocus)          |
 | ToggleVariationsWindow         | Alt+V (NeedsWindowFocus)                           |

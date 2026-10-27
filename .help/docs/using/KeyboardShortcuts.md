
## Hotkeys

The following is a list of the **default keyboard** short-cuts. With v4.0.4 you can customize this list in the *Settings*. Entries marked *(focus)* only work while the window is focused; *(hover)* only while the mouse is over the window.

### General

| Action                   | Key                                       |
| ------------------------ | ----------------------------------------- |
| Add New Operator         | Tab (then type the operator name)         |
| Undo                     | Ctrl + Z                                  |
| Redo                     | Ctrl + Shift + Z                          |
| Save                     | Ctrl + S                                  |
| Toggle Fullscreen        | F11                                       |
| Toggle Focus Mode        | F12                                       |
| Toggle All UI Elements   | Shift + Esc                               |
| Toggle Variations Window | Alt + V                                   |
| Toggle Console Window    | Ctrl + Shift + Alt + L                    |
| Toggle Manual Test Window| Ctrl + Shift + Alt + T                    |
| Render Animation         | Ctrl + Shift + R                          |
| Render Screenshot        | Ctrl + Alt + R                            |

### Graph

| Action                    | Key                                                  |
| ------------------------- | ---------------------------------------------------- |
| Focus Selection           | F (hover)                                            |
| Search Graph              | Ctrl + F                                             |
| Open Operator             | I                                                    |
| Close Operator            | U                                                    |
| Rename Child              | Enter                                                |
| Edit Parameters           | S (focus), see below                                 |
| Navigate Backwards        | Alt + Left                                           |
| Navigate Forward          | Alt + Right                                          |
| Select to Above / Below   | Up / Down (focus)                                    |
| Select to Left / Right    | Left / Right (focus)                                 |
| Duplicate                 | Ctrl + D (focus)                                     |
| Duplicate with Connections| Ctrl + Shift + D (focus)                             |
| Delete Selection          | Delete or Backspace (focus)                          |
| Delete and Disconnect     | Ctrl + Delete or Ctrl + Backspace (focus)            |
| Copy to Clipboard         | Ctrl + C (focus)                                     |
| Paste from Clipboard      | Ctrl + V (focus)                                     |
| Paste Values              | Ctrl + Shift + V                                     |
| Toggle Disabled           | Shift + D (focus)                                    |
| Toggle Bypassed           | Shift + B (focus)                                    |
| Disconnect                | Alt + D (focus)                                      |
| Layout Selection          | G                                                    |
| Align Selection Left      | Alt + A                                              |
| Add Section               | Shift + S, alias Shift + A (focus)                   |
| Add Comment               | Ctrl + Shift + C                                     |
| Pin to Output Window      | P (focus)                                            |
| Show in Graph             | Shift + P                                            |
| Display Image as Background | Ctrl + P                                           |
| Clear Background Image    | Ctrl + Shift + P (focus) or use the Clear BG button  |

### Playback and Timeline

| Action                          | Key                  |
| ------------------------------- | -------------------- |
| Toggle Playback                 | Space                |
| Playback Forward                | L                    |
| Playback Forward at Half Speed  | Shift + L            |
| Playback Backwards              | J                    |
| Playback Stop                   | K                    |
| Previous Frame                  | Shift + Left         |
| Next Frame                      | Shift + Right        |
| Jump to Start Time              | Home                 |
| Jump to Next Keyframe           | Period               |
| Jump to Previous Keyframe       | Comma                |
| Scrub Time                      | T                    |
| Set Start Time                  | B                    |
| Set End Time                    | N                    |
| Insert Keyframe                 | C (focus)            |
| Insert Keyframe with Increment  | Shift + C (focus)    |
| Toggle Animation Pinning        | Shift + K            |
| Split Selected or Hovered Clips | Ctrl + X             |
| Select Following Clips          | Ctrl + Shift + A     |
| Tap Beat Sync                   | Z                    |
| Tap Beat Sync Measure           | X                    |

### Output Camera

| Action                | Key               |
| --------------------- | ----------------- |
| Move Left / Right     | A / D             |
| Move Forward / Back   | W / S             |
| Move Up / Down        | E / Q             |
| Reset Camera          | F                 |
| Focus Selection       | C                 |

### Bookmarks and Layouts

| Action          | Key                  |
| --------------- | -------------------- |
| Load Bookmark   | Ctrl + 1 … 9, 0      |
| Save Bookmark   | Ctrl + Shift + 1 … 9, 0 |
| Load Layout 0–9 | F1 … F10             |
| Save Layout 0–9 | Ctrl + F1 … F10      |

## Editing Parameters with the Keyboard

Press `S` with an operator selected and the graph focused to open the **parameter popover** at the bottom of the graph. It lists the operator's parameters with their values. Animated values are orange, connected ones blue, and changed values are brighter than defaults. The popover works in Focus Mode too, where the Parameter Window is hidden.

### Finding a parameter

Type letters to filter the list. They match the start of a parameter name, the start of a word in it (`count` finds LineCount), or its initials (`lc`). The order of the parameters doesn't change; the best match gets the focus. Up and Down move through the list.

### Typing a value

Press Tab to edit the focused value, then type it. The value is previewed while you type.

| You type          | Result                                                            |
| ----------------- | ----------------------------------------------------------------- |
| `10`, `-5`        | sets the number                                                   |
| `*2`, `+1`, `/4`  | continues from the current value: `100*2`, shown as `= 200`       |
| `(1+2)^2`, `pi*2` | calculates the value                                              |
| `0 1 0`           | sets all components of a vector                                   |
| `x 1`             | sets one component                                                |
| `orange`, `#f80`  | sets a color by name or hex code; `a 0.5` sets its alpha          |
| `sim`             | picks the matching dropdown option, Up and Down show the others   |

Down applies the value and moves to the next parameter, Up to the previous one. Enter applies and closes the popover. Esc steps back: first the typed value, then the filter, then the popover.

### Adjusting a value

Left and Right adjust the focused value with the infinity slider. A short press moves one step, holding accelerates. Shift makes the steps finer, Ctrl coarser.

- On colors, Left and Right change the brightness. Type `h` (hue), `s` (saturation) or one of `r g b a` first to adjust that instead.
- On vectors, type the component first, e.g. `x`.
- Dropdowns step to the previous or next option, checkboxes toggle.

### Commands

| Action                          | Key       |
| ------------------------------- | --------- |
| Reset the parameter to default  | Shift + R |
| Insert an operator connected to the parameter | Shift + I |

### Presets when inserting operators

In the symbol browser (Tab), type a space after the operator's name, or after picking it with the arrow keys, to list its presets. Type to filter them and press Enter to insert the operator with that preset. Another space inserts it and opens the parameter popover, so you can go straight on to adjusting its values.

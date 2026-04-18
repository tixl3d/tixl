# Working with the Timeline

Below each *Graph Window* is a timeline. You can toggle its visibility in *Application Menu* → *View* → *Timeline*.

Some of its properties can be controlled by the graph's toolbar:

* The **playback settings** control details like the current soundtrack.
* The **Timeline format** toggles the raster between bars, seconds, or frames (see below).
* The **Loop playback** toggle shows the loop (or work area).
* The **Dope/Curve** toggle switches between the two primary modes.

## Controlling playback time

You can scrub the time in the lower shaded section of the timeline. (In the upper area, dragging will select objects like keyframes and time clips.)

You can use these keyboard shortcuts:

* Hold **Shift** to snap to the bar raster or other timeline elements.
* `Space` to toggle playback.
* `Home` to jump to the beginning.
* `L` to play.
* `L` again to play faster.
* `J` to play backward.
* `J` again to play backward faster.
* `K` to stop.
* `.` and `,` to jump to the next and previous keyframes.
* `Shift` + ← and → to step in 30 ms increments.

### Snapping

In the background, you'll notice the **time raster**. You can switch between:

* **Bars** (using the current project's BPM rate)
* **Seconds**
* **F30 and F60 Frames**

By default, dragging timeline elements snaps only to the **bar** raster. You can zoom in to snap to finer resolutions (beats and ticks) or zoom out (bars, measures, phrases).

Hold `Shift` to disable snapping.

You can adjust the timeline raster density in *Settings*.

## Working with the Dope Sheet

The dope sheet view displays the keyframes of the currently selected operators.

### Adding and editing keyframes

You can add keyframe animations in the *Parameter Window*: click the parameter input while holding `Alt` to insert the first keyframe:

![alt text](images/anim-8.gif)

After a parameter is animated you can:

1. **Insert new keyframes** by modifying values.
2. **Toggle keyframes** by pressing the keyframe indicator in the parameter window.
3. Adjust keyframe interpolation with the context menu.
4. Adjust keyframe values by selecting a single keyframe.
5. Insert keyframes by holding `Alt` and clicking on the animation curve.

To **remove an animation**, either:

* Remove all keyframes.
* Reset the parameter by clicking the parameter name in the *Parameter Window*.

### Displaying keyframes

By default, only the keyframes of selected operators are shown in the timeline. You can enable *Keep animated parameters visible* in the toolbar:

![alt text](images/image-9.png)

This will keep parameters visible. If too many parameters are displayed, press `Ctrl`+`Shift`+`K` twice to clear the list.

### Scaling keyframe animation speed

To slow down or speed up an animation:

1. Select keyframes.
2. Hold `Alt` to show the stretch indicators at the bottom.
3. Drag the stretch indicators:

![alt text](images/anim-9.gif)

### Keyframe parameters

In the context menu, you can switch between interpolation modes. To control tangents, switch between *Curve* and *Dope Sheet* mode.

### Inserting animation increments

You can insert **incremental keyframes** by pressing `Shift+C`. This will insert a new keyframe incremented by 1 at the current time.

This can be useful for adding *step markers* while listening to a soundtrack. For example:

1. Add a soundtrack to your project.
2. Press `Home` or go to the timeline start.
3. Create a \[Value] operator and animate its parameter in the *Parameter Window* (see above). Change its interpolation mode to *Constant*.
4. Press `Space` or `L` to start playback.
5. Press `Shift+C` on beats or key moments in the soundtrack. This will insert keyframes with values 1,2,3,4 etc.
6. Connect \[HasValueChanged] and \[TriggerAnim] or other operators to use this timing.

To recompute the keyframe values (e.g., after inserting or deleting keys):

1. `Shift`+Click on the keyframe row to select all keyframes.
2. Use *Recount values* from the context menu.

## Working with Time Clips

Some operators like \[VideoClip] or \[TimeClip] output "time clipping." These operators are evaluated only if the current playback time is within their *Clip Time*. Otherwise, they are ignored as if disabled.

If a composition has any time clips, the timeline displays a *Time Clip Area* above the *Dope Sheet*.

The primary operator for this is \[TimeClip]. It splits the evaluation flow so sub-scenes render only while playback is within their time.

You can combine these clips with a \[Group] as in this example:

![alt text](images/anim-4.gif)

The active clip will be highlighted.

### Time clips need to be connected

> [!NOTE]
> Clips not connected will never render unless selected directly in the Output Window. TiXL will fade out these operators and display a "Not Connected?" warning in their tooltip:

![inactive clip](images/image-7.png)

### Editing Time Clips

You can drag one or more time clips with the left mouse button. They will snap to:

* The timeline raster (if in *Bars* mode)
* Other time clips
* The current playback time
* Other keyframes
* The loop area (if active)

Hold `Shift` to disable snapping.

Rename clips by pressing `Return`.

### Time Clip Layers

You can drag clips **vertically** to add layers if needed. These layers do not affect rendering order, so the name is slightly misleading.

> [!NOTE]
> The rendering order (i.e., the evaluation order) is defined by the connections in the group on the right. The vertical layer order in the timeline has no effect.

> [!NOTE]
> TiXL automatically distributes overlapping clips into separate layers.

Hold `Ctrl`+`Shift` while dragging vertically to adjust only the layer index.

### Time Remapping

An advanced feature is time remapping. Similar to \[SetCommandTime], this allows the left side of the time clip to have its time speed and source remapped.

Consider this example:

![alt text](images/anim-6.gif)

Here, the second time clip remaps the source time to 500% speed. This is indicated by the red bottom bar:

Selecting a single time clip shows the mapping with two curves indicating the relation between clip time and source time:

![alt text](images/image-8.png)

Ways to edit time remapping:

1. Drag clip start and end times while holding `Alt`.
2. *Slip*-drag the source region by dragging inside the clip with `Alt+Ctrl`.
3. Select clips and choose *Edit Clip Times* from the context menu.
4. Select clips and choose *Clear Time Stretch* to reset the mapping.

> [!NOTE]
> Working with remapped time can be very confusing, especially when editing keyframes.
> Consider carefully whether this complexity is needed in your project.

### Splitting Time Clips

You can select one or more clips and split them at the current playback time via *context menu* → *Cut at Time*. This will automatically rename the new clip and try to reconnect it to target groups.

### Nesting Timing

If an operator symbol has an output flagged as **time clip**, instances of this operator will be shown in the time clip area. Unlike \[TimeClip] operators, their source time is **not** linked to the clip time: When dragging such an operator, e.g., from starting at 0 to starting at 1 bar, its source time still points to the original 0.

### Editing Videos

This can be useful for editing \[VideoClip]s similar to a video editing application. However, seeking in video files with high compression will lead to performance issues. If you want to use this feature, we recommend re-encoding video files, e.g., with the following [ffmpeg](https://support.audacityteam.org/basics/installing-ffmpeg) command:

```
ffmpeg -i "input.mp4" -g 1 -c:v libx264 -preset veryfast -crf 18 output.mp4
```

### Editing Clip Timing

You can use the **Clip Timing Editor** to edit the timing of one or more time clips:

![alt text](images/anim-10.gif)

If the selection has multiple values, the editor will indicate that with the shortcut NaN. You can replace this with a number to set the field for all selected clips.

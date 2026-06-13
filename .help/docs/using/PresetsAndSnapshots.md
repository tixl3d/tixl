# Presets and Snapshots

Working with Presets and Snapshots is one of the best features in TiXL. You can find them in the "Variations" window which should be open in most of the default layouts.

Variations come in two flavours: Presets and Snapshots. Internally they are using the same method jointly called "Variation"

## Working with presets

**Presets** are settings for Operator _symbols_ like [Blob] or [Layer2d].

Presets showcase whats possible with an operator, and highlight options and parameter combinations you might not have discovered.

## Working with snapshots

**Snapshots** are parameter sets stored for a group of operator _instances_ in a composition. E.g. in an project for a live VJ-set you can use the context menu to activate Snapshots for some of the operators in that project. You can then use the + icon to save (I.e. create a snapshot of) the current state of all parameters of these operators into a single Snapshot. 

![snapshots1](https://user-images.githubusercontent.com/1732545/232545725-7d528af0-325e-4b31-9bcc-812ecc595878.gif)

### The snapshot control view

While the Variations window is the visual interface for snapshots, the Parameter window offers a precise, per-parameter one: when nothing is selected in the graph (the composition itself is active), it shows the **snapshot control view**.

At the top, a selector bar shows the index of the active snapshot, a dropdown to pick one, and prev/next arrows that cycle through snapshots in index order. On the right are three actions:

- **Write** (camera icon) — update the snapshot with the current parameter values. Only enabled when values differ from the snapshot.
- **Revert** (circular arrow) — re-apply the snapshot's stored values. Also only enabled when something changed.
- **Remove** (trash icon) — delete the snapshot.

Click the **+** button to create a new snapshot from the current values; it becomes active and its name field is focused so it can be named right away. Clicking the index number on the left of the bar renames the active snapshot.

Below the bar, all operators enabled for snapshots are listed in rounded panels with the parameters captured in the active snapshot. The values are editable exactly like in the regular parameter view, including undo. Clicking an operator's name selects it and centers it in the graph. Parameters published as composition inputs appear in their own "Inputs" group.

Parameters that no longer match the snapshot are highlighted, with a revert handle next to them. Clicking the handle restores the snapshot value; dragging it opens a slider that scales the change — towards 0 reverts, above 1 amplifies it beyond the current value.

All actions are undoable, and the view stays in sync with the Variations window — both operate on the same snapshots.

### Controlling individual parameters

Snapshot control can be limited to individual parameters: right-click a parameter in the Parameter window and choose **"Control with Snapshots"**. Controlled parameters show a green knob icon in the connection area next to their name.

- Enabling a parameter on an operator that wasn't snapshot-enabled yet enables the operator with just that parameter.
- Disabling the last controlled parameter disables the operator for snapshots entirely.
- The operator's context menu entry "Enable for snapshots" in the graph remains the bulk toggle: it enables or disables all parameters at once.

Existing snapshots are kept consistent automatically — enabling a parameter captures its current value into all snapshots, disabling removes its stored values. Parameters that are not controlled are never touched when snapshots are applied or blended.

## Using Snapshots with Midi-Controllers
Snapshots are incredibly powerful in combination with a launch-pad controller like the APC Mini. Each of the highlighted buttons will indicate a snapshot for the current operator. Clicking an empty button will create a new snapshot.

Additional interaction for APC-Mini are:
- Holding Clip Stop will activate the deletion mode (indicated by a red flashing line). Clicking green buttons will remove those snapshots.
- Holding the Shift Button with a Scene button will activate smooth blending with the slider below the Shift button. Moving the slider all the way up will fully transition into that snapshot. Moving the slider down will restore the state at the start of the blending. Tapping the shift button will disable blending.


## Live previews and hover view
The Variation window provides some very powerful options to explore presets:

### Live Thumbnails
By default TiXL will generate live thumbnails for your currently pinned output. This means, that even the presets for the currently selected operator are not the final output, you can see how that preset would affect the final output further down the stream:

![preset-preview](https://user-images.githubusercontent.com/1732545/232548921-d204da36-67af-4f3f-b2ee-ab8af5dc7625.gif)

You can enable or disable live previews and hover preview with the toggle icons in the window header (highlighted when active) or with the context menu.

Live thumbnails are temporary: they never overwrite the default thumbnails stored with the project. When you disable live previews, the default thumbnails reappear. Use "Update thumbnails" in the context menu to explicitly save the current renderings as new defaults.

Note: Rendering of thumbnails will interfere with effects that rely on feedback rendering, eg. [AdvancedFeedback] or [SimpleLiquid]. 

In these cases you should disable live thumbnails and hover preview.

![image](https://user-images.githubusercontent.com/1504019/232598204-70d2be3f-5697-4ed9-9912-e2b2eacee749.png)


### Blending between Variations

There are multiple options to preview, mix and apply both presets and snapshots:

1. Click to apply
2. With hover preview enabled, hover of the thumbnails and then apply.
3. Hold ALT-Key and hover to blend between variations. 

You can select variation thumbnails (without applying them) by dragging a selection frame on the background. 
With these variations selected you can...

4. Select only one presets and use ALT to Mix in that variation
5. Select two to blend between them.

Notice how you can mix with ratio below 0 or above 100%!






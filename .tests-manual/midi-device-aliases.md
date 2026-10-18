---
id: midi-device-aliases
title: MIDI device aliases — stable device names across machines
added: 2026-10-18
added-in-version: 4.4
scope: settings
tags: [user, midi]
prerequisites:
  - A MIDI controller with at least one knob or fader is connected.
  - A project is open that contains at least one [MidiInput] taught to that controller (its Device input shows the physical device name).
related-help:
  - ../.help/docs/using/MidiDeviceAliases.md
---

Projects store MIDI device names. Aliases (`TiXL:Name`) replace them with a stable name that is
mapped to the physical device per machine in **Settings » Midi**. This set covers mapping,
teaching, the replacement of device-specific names, undo, and the warnings an unmapped alias shows.

## Step: Finding the device-specific name

**Action:**
Open **Windows » Settings**, choose the **Midi** category and look at the **Device-Specific Names** list.

**Expected:**
- The connected controller's name is listed with a usage count (`used 1x` or more).
- Hovering the row shows a tooltip listing `Project / Symbol / Operator` for each referencing input.
- The row has a **Use Alias...** button.

## Step: Replacing the name with an alias

**Action:**
Click **Use Alias...**. In the dialog keep the suggested alias name (or type `Pad`), make sure the device combo shows the connected controller, and click **Replace and Save**.

**Expected:**
- The dialog's summary states how many inputs are replaced.
- The device-specific row disappears; the alias appears in **Device Aliases** mapped to the controller with the same usage count.
- The [MidiInput] in the graph now shows `TiXL:Pad` (or the suggested name) in its Device input and still reacts to the controller.

## Step: Undoing the replacement

**Action:**
Focus the graph window and press `Ctrl+Z` once, then `Ctrl+Shift+Z` (redo).

**Expected:**
- One undo restores the physical name in every replaced input at once.
- Redo puts the alias back.

## Step: Teaching stores the alias

**Action:**
Add a new [MidiInput], enable its **TeachTrigger** and move a knob on the controller.

**Expected:**
- The Device input shows the alias (`TiXL:Pad`), not the physical device name.
- Channel and Control are taught as before.

## Step: Reordering decides the taught alias

**Action:**
In Settings » Midi click **Add Alias...**, name it `Second`, map it to the same controller. Drag its grip above the first alias. Teach another [MidiInput].

**Expected:**
- The new op stores `TiXL:Second`, the alias that is now first in the list.
- Dragging the grip reorders rows live; dropping keeps the new order after closing and reopening the settings.

## Step: Merging two aliases

**Action:**
Click **Add Alias...**, name it `pad` (lower case) and map it to the controller. Teach a [MidiInput] so it stores `TiXL:pad`. Then click the edit button on the `TiXL:pad` row, start typing `P` and pick `Pad` from the suggestions. Confirm with **Merge and Save**.

**Expected:**
- The suggestion list offers the existing alias names.
- The dialog says it merges into the existing alias and keeps its mapping.
- Afterwards only `TiXL:Pad` is listed; its usage count grew by the merged references and the taught op shows `TiXL:Pad`.
- One undo restores `TiXL:pad` in the merged inputs.

## Step: Unplugged device keeps its mapping

**Action:**
Unplug the controller, click **Rescan Devices**.

**Expected:**
- The alias rows stay. Their device combo shows the name with `(NOT FOUND)` in the warning colour.
- The [MidiInput] shows a warning that the device for the alias is not connected.
- Plugging the controller back in and rescanning clears both.

## Step: Unmapped alias warns and matches nothing

**Action:**
Set a [MidiInput]'s Device input to `TiXL:Nowhere` by typing it into the dropdown field.

**Expected:**
- The op shows a warning that the alias is not mapped on this machine, pointing to Settings » Midi.
- Moving controller knobs does not change the op's output.
- Settings » Midi lists `TiXL:Nowhere` in **Device Aliases** as **Not Set**; picking a device there makes the op react.

## Step: The alias file ships with a Player export

**Action:**
Export the project as a Player (**File » Export Executable...**) and look into the export folder.

**Expected:**
- The folder contains `midiAliases.json` next to the Player executable, with one entry per alias and its device name.
- The same file exists in the settings folder (folder icon at the bottom of the settings window).

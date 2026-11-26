---
id: mapped-enum-editing
title: Publishing and editing dropdown parameters
scope: parameter-window
tags: [user]
added: 2026-11-26
added-in-version: 4.4
prerequisites:
  - A writable (non-library) project is open.
---

Covers int parameters shown as a dropdown (an enum mapped onto the int): carrying the dropdown over when
publishing, and editing the enum's entries from the parameter settings.

## Step: Publishing a dropdown parameter

**Action:**
Combine an [AnimatedText] into a new operator. Inside it, publish the **Unit** and **HorizontalAlign** parameters of the [AnimatedText] as inputs.

**Expected:**
- Both new inputs show a dropdown with the same entries as on [AnimatedText].
- The new operator's source declares a private enum `Units` (Unit's enum is private to [AnimatedText]) and references `BmFontDescription.HorizontalAligns` directly.
- Undo removes the input and the copied `Units` enum again.

## Step: Opening the entry editor

**Action:**
Open the parameter settings of the new operator and select the published **Unit** input.

**Expected:**
- **Display as** shows a segmented switch `Value | Dropdown | Switch` with Dropdown selected, and an **Edit Entries...** button right below it.
- Clicking it opens the **Edit Enum Entries** dialog listing `Characters` and `Groups` with their values.
- A hint says that keeping existing instances consistent is up to you. No custom-code hint is shown, because the operator holds only slots.

## Step: Editing entries

**Action:**
Drag `Groups` above `Characters`, rename it to `Words`, add an entry, then click **Apply**.

**Expected:**
- **Apply** stays disabled until something changed, and while a name is empty, invalid or duplicated (the name turns magenta).
- After applying, the dropdown lists `Words`, `Characters` and the new entry in that order; `Words` still selects the value it had as `Groups`.
- Undo restores the original entries.

## Step: Compile errors and outside enums

**Action:**
Open **Edit Entries...** for an enum parameter of an editable operator with custom code that switches over its entries, rename one of them and apply. Then try **Edit Entries...** on the published **HorizontalAlign** input.

**Expected:**
- The dialog warns about custom code before applying. After applying, it shows the compile error and stays open; nothing is changed.
- For **HorizontalAlign** no dialog opens; the console logs that only enums declared inside the operator can be edited.

## Step: Turning a plain int into a dropdown and back

**Action:**
In the same operator, publish the **Seed** parameter of the [AnimatedText]. In its parameter settings, click **Switch** under **Display as**, then **Apply**. Afterwards click **Value** and confirm with **Show as Value**.

**Expected:**
- Clicking Switch opens **Edit Enum Entries** with a few `Option` entries; Cancel leaves the parameter a plain number.
- After Apply the parameter shows its entries side by side, and the source declares a `Seeds` enum mapped to the input. Undo turns it back into a plain number.
- Clicking Value asks for confirmation and says "This is not undoable." After confirming, the parameter is a number again, the `Seeds` enum is gone from the source, and the undo history is cleared.

**Sections** (formerly called annotations) are labeled, colored frames you draw behind operators in the [ui:Graph|graph] to group and comment on parts of your project.

They have no effect on rendering — they're purely there to organize a busy graph into readable parts and remind you (or a collaborator) what each cluster does. Add one with `Shift+S` (it wraps the current selection, or drops an empty frame at the cursor), then give it a title and color.

An operator belongs to the innermost section that fully contains it, and sections nest the same way — moving a frame takes its content along, and dragging operators in or out updates the grouping automatically. Collapse a section with the chevron in its header to fold its operators away and reclaim screen space; expanding brings them back unchanged.

Deleting an expanded section removes only the frame and leaves its operators in place. Deleting a *collapsed* section deletes the hidden operators and nested frames along with it — undo restores everything.

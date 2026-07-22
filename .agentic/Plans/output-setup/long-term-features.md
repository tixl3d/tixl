# Long Term Features related to output handling

Review once in a while to prioritize

When connecting a new display or projector, if have to stop presenting and represent.

For later:

- Setup menu should provide options to rename setup
- Setup menu should have a hover indication (e.g. change font color ForegroundFull)
- Unused surfaces (without any Content being routed into should be shown as unused)
- it would be nice to have a vertical splitter to adjust the width of the output settings panel
- Deleting an operator on the graph should also delete the Content item in the side panel
- We need an option to remove a reference like Content -> Surface. Not sure, about the interaction though. Some ideas:
  1. Context menu on Content Item listing all targets
  1. Listing the inputs in the target (e.g. Surface)
  1. Selecting a surface icon could show input target indicators (e.g. Icon.ArrorLeft) on the left side of potential targets. Active targets are highlighted (e.g. with UiColors.StatusActive). Inactive would be show as BackgroundFull.Fade(0.5). Clicking the input indicator would toggle the reference.
  1. Dragging a source onto a target could clear the old reference (Maybe this could be avoid with a keyboard shortcut)



## More use-cases for surfaces

- A surface could have axis aligned guide lines (relative to anchor points). E.g. image to create a rectified surface mapping to the front facade of a house. Then change to straightened mode. 





## Visual Cleanup:

- Narrow Form Inputs
  - Default input size -> 23
  - New ShortName combine op
  - Align paddings and high between label and input
  - Add number formatting suffix (e.g. m or px)
  - 


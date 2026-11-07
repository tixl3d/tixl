# New Symbol and Import Operators

Both entries were removed from the app menu (2026-10-24) because they did not work: "New Operator..."
opened the template dialog, which hasn't been tested for a long time, and "Import Operators" only showed a
"not yet available" message. This plan collects what is needed to bring them back properly.

## Current state

- The template dialog and its definitions (`Editor/Gui/Templates/`) were deleted on 2026-11-08, with the Empty Scene
  and 3d Scene template symbols: creating a project replaces them. The shader templates
  (`Examples/Symbols/templates/_PointShaderTemplate`, `_ImgFxShaderTemplate`, and their HLSL in
  `Lib/Assets/templates/`) are left as they are, unreferenced, until a New Symbol or ShaderToy import needs them.
- `UserActions.New` is in the keymap but has no handler. It only labelled the removed menu item.
- There is no import code. The menu item had a commented-out `_importDialog`.

## Goal: a "New Symbol" submenu in the Projects group

Each entry creates a symbol in the focused project and opens or places it:

1. **Empty Operator** — name + namespace, nothing else.
2. **From an example** — Duplicate as New Type on an example op; the shader templates above are candidates once
   they compile under Slang/Vulkan.
3. **Import from ShaderToy** — paste a ShaderToy URL or source, translate GLSL → HLSL for the
   image-effect template (mainImage, iTime/iResolution/iMouse/iChannel0..3 mapped to inputs).

`UserActions.New` should open the submenu's default (Empty Operator) once it exists.

## Goal: Import Operators

Copy symbols (with their dependencies and assets) from another project folder or a shared `.zip` into the
focused project, resolving namespace and id collisions. Needs a design pass first: what the unit of
exchange is (single symbol vs. package), how dependencies on other user projects are handled, and whether
this replaces copy/paste across projects.

## Open questions

- Should New Symbol live under Projects or Edit › Add?
- ~~Is the template dialog worth repairing?~~ No: projects made the scene templates obsolete, and the dialog was
  deleted (2026-11-08).

# Operator reference

Every operator in the `Lib.*` namespace has a reference page here, auto-generated from its in-editor description by [ExportWikiDocumentation.cs](https://github.com/tixl3d/tixl/blob/main/Editor/Gui/UiHelpers/Wiki/ExportWikiDocumentation.cs).

Start browsing at [**Lib/**](lib/README.md) — the tree mirrors the operator library's namespace tree (`Lib.field.adjust.PushPullSDF` → [/operators/lib/field/adjust/PushPullSDF/](lib/field/adjust/PushPullSDF.md)).

## Authoring

- Edit operator descriptions in the TiXL editor, not the generated `.md` files. Run **Documentation → Export as WIKI** from the editor's main menu to regenerate the reference after changes.
- In prose, refer to operators with brackets: `[AdjustColors]`. The MkDocs hook in [scripts/docs/op_autolinks.py](https://github.com/tixl3d/tixl/blob/main/.help/scripts/docs/op_autolinks.py) resolves them against [index.json](index.json) at build time.
- Qualify a short name with its namespace (e.g. `[Lib.image.color.AdjustColors]`) when multiple operators share the name. Ambiguous bare references are left as plain text with a build warning.

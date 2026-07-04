# UI Topic Registry

Hand-authored source for the `ui:` half of the reference index — the editor's UI components and
concepts, the mirror of the generated operator index. `analysis_to_index.py` parses this into
`references/indices/topics.json` and uses it to resolve `[ui:Id]` (and bare-bracketed UI names the
video extractors already caught) into `ui:<id>` deep-links.

**Format** — one `## ` block per topic:
- The heading is the human **term**; `id:` is the stable PascalCase key (`ui:<id>`).
- `synonyms:` — comma-separated; *the* load-bearing field, it's what lets an SRT mention
  ("dope sheet area", "MagGraph") resolve to the right topic. Not shown to users.
- `parent:` — nesting (e.g. Dope sheet ⊂ Timeline), for "show more" grouping and hover "where to
  find it".
- `classes:` — the C# class(es) that implement the component. UI *windows* are where a future
  `[HelpUiID]` attribute would hang; for *concepts* (Symbol, Field, …) it's just the doc anchor.

This file is **metadata only**. Each topic's help text (the short doc shown on hover and by the
documentation icon) lives in its own file `references/topics/ui/<Id>.md` — edit it there. The build
records a `docFile` pointer in `topics.json` and the editor loads that file lazily. Keep those bodies
within the editor markdown subset (headings, bold, `code`, lists, `[Op]` / `[ui:Topic]` links — no
images or tables), since they render in the in-editor markdown view.

## Graph
id: Graph
synonyms: GraphWindow, GraphCanvas, Operator Graph, Magnetic Graph, MagGraph
classes: GraphWindow, MagGraphView

## SkillQuest
id: SkillQuest
synonyms: Skill Quest tutorials, Tutorials
classes: SkillTraining

## Skill Quest Level
id: SkillQuestLevel
parent: SkillQuest
classes: QuestLevel

## Skill Map
id: SkillMap
parent: SkillQuest
synonyms: Skill Quest Map
classes: SkillMapData

## Parameter window
id: ParameterWindow
synonyms: Parameter View
classes: ParameterWindow

## Output Window
id: OutputWindow
classes: OutputWindow

## Settings
id: Settings
synonyms: Settings Window, User Settings
classes: SettingsWindow

## Project Settings
id: ProjectSettings
synonyms: Composition settings
classes: ProjectSettingsWindow

## Symbol
id: Symbol
classes: Symbol

## Composition
id: Composition
classes: Instance

## User Project
id: UserProject
classes: EditableSymbolProject

## Asset
id: Asset
synonyms: Assets
classes: Asset

## Asset Library
id: AssetLibrary
classes: AssetLibrary

## Symbol Library
id: SymbolLibrary
classes: SymbolLibrary

## Symbol browser
id: SymbolBrowser
classes: PlaceholderCreation

## Variation window
id: VariationWindow
classes: VariationsWindow

## Search window
id: SearchWindow
synonyms: Control F, Find
classes: SearchDialog

## Sections
id: Annotations
synonyms: annotations, frames
classes: MagGraphSection

## Operator Settings
id: OperatorSettings
synonyms: Operator names
classes: ParameterSettings

## Gradient editor
id: GradientEditor
classes: GradientEditor

## Timeline
id: Timeline
synonyms: timeline window
classes: TimeLineCanvas

## Dope sheet
id: DopeSheet
parent: Timeline
synonyms: dope sheet area
classes: DopeSheetArea

## Animation area
id: AnimationArea
parent: Timeline
classes: TimelineDetailsArea

## Curve editor
id: CurveEditor
parent: Timeline
synonyms: Curve area
classes: TimelineCurveEditor

## Control bar
id: ControlBar
synonyms: tool bar
classes: TimeControls

## Performance monitor
id: PerformanceMonitor
synonyms: performance window, performance graph
classes: PerformanceWindow

## Control view
id: ControlView
classes: SnapshotControlView

## Shader graph
id: ShaderGraph
classes: ShaderGraphNode

## Shader node
id: ShaderNode
parent: ShaderGraph
classes: ShaderGraphNode

## Field
id: Field
synonyms: SDF, distance field, value field
classes: ShaderGraphNode

## Gizmo
id: Gizmo
classes: TransformGizmoHandling

## Welcome window
id: WelcomeWindow

## Welcome alpha window
id: WelcomeAlphaWindow
classes: WelcomeAlphaWindow

## Project panel
id: ProjectPanel
synonyms: project list, home
classes: ProjectsPanel

## Render settings
id: RenderSettings
classes: RenderWindow

## Output settings
id: OutputSettings
classes: OutputWindowState

## IO view
id: IoView
classes: IoViewWindow

## Console log
id: ConsoleLog
synonyms: log window, console window
classes: ConsoleLogWindow

## Splash screen
id: SplashScreen
classes: SplashScreen

## Player
id: Player

## Player exporter
id: PlayerExporter
parent: Player
classes: PlayerExporter

## Color editor
id: ColorEditor
classes: ColorEditPopup

## Infinity slider
id: InfinitySlider
classes: InfinitySliderOverlay

## Parameter popup
id: ParameterPopup
classes: ParameterPopUp

## Focus mode
id: FocusMode
classes: LayoutHandling

## Idle motion
id: IdleMotion
classes: Playback

## Evaluation context
id: EvaluationContext
synonyms: context, variables
classes: EvaluationContext

## Time overrides
id: TimeOverrides
classes: EvaluationContext

## Local time
id: LocalTime
parent: EvaluationContext
classes: EvaluationContext

## Play back time
id: PlaybackTime
parent: EvaluationContext
classes: Playback

## Audio input
id: AudioInput
classes: WasapiAudioInput

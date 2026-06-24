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
- Everything after the metadata block (separated by a blank line) is the **embedded doc** — the short
  help shown on hover. `_TODO_` means not yet written.

## Graph
id: Graph
synonyms: GraphWindow, GraphCanvas, Operator Graph, Magnetic Graph, MagGraph
classes: GraphWindow, MagGraphView

_TODO_

## SkillQuest
id: SkillQuest
synonyms: Skill Quest tutorials, Tutorials
classes: SkillTraining

_TODO_

## Skill Quest Level
id: SkillQuestLevel
parent: SkillQuest
classes: QuestLevel

_TODO_

## Skill Map
id: SkillMap
parent: SkillQuest
synonyms: Skill Quest Map
classes: SkillMapData

_TODO_

## Parameter window
id: ParameterWindow
synonyms: Parameter View
classes: ParameterWindow

_TODO_

## Output Window
id: OutputWindow
classes: OutputWindow

_TODO_

## Settings
id: Settings
synonyms: Settings Window, User Settings
classes: SettingsWindow

_TODO_

## Project Settings
id: ProjectSettings
synonyms: Composition settings
classes: ProjectSettingsWindow

_TODO_

## Symbol
id: Symbol
classes: Symbol

_TODO_

## Composition
id: Composition
classes: Instance

_TODO_

## User Project
id: UserProject
classes: EditableSymbolProject

_TODO_

## Asset
id: Asset
synonyms: Assets
classes: Asset

_TODO_

## Asset Library
id: AssetLibrary
classes: AssetLibrary

_TODO_

## Symbol Library
id: SymbolLibrary
classes: SymbolLibrary

_TODO_

## Symbol browser
id: SymbolBrowser
classes: PlaceholderCreation

_TODO_

## Variation window
id: VariationWindow
classes: VariationsWindow

_TODO_

## Search window
id: SearchWindow
synonyms: Control F, Find
classes: SearchDialog

_TODO_

## Annotations
id: Annotations
synonyms: sections
classes: MagGraphAnnotation

_TODO_

## Operator Settings
id: OperatorSettings
synonyms: Operator names
classes: ParameterSettings

_TODO_

## Gradient editor
id: GradientEditor
classes: GradientEditor

_TODO_

## Timeline
id: Timeline
synonyms: timeline window
classes: TimeLineCanvas

_TODO_

## Dope sheet
id: DopeSheet
parent: Timeline
synonyms: dope sheet area
classes: DopeSheetArea

_TODO_

## Animation area
id: AnimationArea
parent: Timeline
classes: TimelineDetailsArea

_TODO_

## Curve editor
id: CurveEditor
parent: Timeline
synonyms: Curve area
classes: TimelineCurveEditor

_TODO_

## Control bar
id: ControlBar
synonyms: tool bar
classes: TimeControls

_TODO_

## Performance monitor
id: PerformanceMonitor
synonyms: performance window, performance graph
classes: PerformanceWindow

_TODO_

## Control view
id: ControlView
classes: SnapshotControlView

_TODO_

## Shader graph
id: ShaderGraph
classes: ShaderGraphNode

_TODO_

## Shader node
id: ShaderNode
parent: ShaderGraph
classes: ShaderGraphNode

_TODO_

## Field
id: Field
synonyms: SDF, distance field, value field
classes: ShaderGraphNode

_TODO_

## Gizmo
id: Gizmo
classes: TransformGizmoHandling

_TODO_

## Welcome window
id: WelcomeWindow

_TODO_

## Welcome alpha window
id: WelcomeAlphaWindow
classes: WelcomeAlphaWindow

_TODO_

## Project panel
id: ProjectPanel
synonyms: project list, home
classes: ProjectsPanel

_TODO_

## Render settings
id: RenderSettings
classes: RenderWindow

_TODO_

## Output settings
id: OutputSettings
classes: OutputWindowState

_TODO_

## IO view
id: IoView
classes: IoViewWindow

_TODO_

## Console log
id: ConsoleLog
synonyms: log window, console window
classes: ConsoleLogWindow

_TODO_

## Splash screen
id: SplashScreen
classes: SplashScreen

_TODO_

## Player
id: Player

_TODO_

## Player exporter
id: PlayerExporter
parent: Player
classes: PlayerExporter

_TODO_

## Color editor
id: ColorEditor
classes: ColorEditPopup

_TODO_

## Infinity slider
id: InfinitySlider
classes: InfinitySliderOverlay

_TODO_

## Parameter popup
id: ParameterPopup
classes: ParameterPopUp

_TODO_

## Focus mode
id: FocusMode
classes: LayoutHandling

_TODO_

## Idle motion
id: IdleMotion
classes: Playback

_TODO_

## Evaluation context
id: EvaluationContext
synonyms: context, variables
classes: EvaluationContext

_TODO_

## Time overrides
id: TimeOverrides
classes: EvaluationContext

_TODO_

## Local time
id: LocalTime
parent: EvaluationContext
classes: EvaluationContext

_TODO_

## Play back time
id: PlaybackTime
parent: EvaluationContext
classes: Playback

_TODO_

## Audio input
id: AudioInput
classes: WasapiAudioInput

_TODO_

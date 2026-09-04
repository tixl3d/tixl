# Locator

*in [Lib.render.gizmo](README.md)*

Can be used to add a visible transform gizmo and its position in a scene (only visible if 'Toggle Gizmos and Floor Grid' is enabled in the Output window).

The 'Pos' output can be linked to the 'Translation' input of any operator to synchronize (link) their position.
(e.g. a [PointLight] with any mesh or geometry via [Transform] and the [GodRays] effect).

The 'Distance to Camera' output allows using the locator as a visible target for [DepthOfField].

Helpful in combination with [TransformPoints], [TransformMesh]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Position** (Vector3) | Defines the position of the Locator |
| **Size** (Single) | Scales the Locator (only visible if 'Toggle Gizmos and Floor Grid' are enabled in the Output window) |
| **Thickness** (Single) | Defines the thickness of the visible Locator |
| **Color** (Vector4) | Defines the color of the Locator |
| **Label** (String) | Text field to give the locator a name that can be seen in the viewport |
| **Visibility** (GizmoVisibility) | Defines the settings that control the visibility of the locator<br/><br/>Off = Locator always invisible<br/>On = Locator always visible<br/>IfSelected = Locator becomes visible when selected in the 'Graph' Window<br/>Inherit = Locator is visible when 'Toggle Gizmos and Floor Grid' is activated<br/> |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
| **Pos** | System.Numerics.Vector3 |
| **DistanceToCamera** | System.Single |


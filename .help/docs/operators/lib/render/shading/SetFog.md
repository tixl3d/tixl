# SetFog

*in [Lib.render.shading](README.md)*

Adds fog to the incoming scene.

ProTip: If large meshes (for example a "quadmesh" as a ground plane) are not properly obscured by the fog, one possible reason could be that the tessellation is not high enough.

Useful combinations [ColorGradeDepth]. An operator that allows objects far away from the camera to be colored differently than close objects.

Also see [SetEnvironment] and [DepthBufferAsGrayScale]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command) | — |
| **Distance** (Single) | Defines how far away from the view the fog starts to appear. |
| **Bias** (Single) | Defines the thickness and gradations of the fog.<br/><br/>A lower bias creates thin fog. <br/>Meaning that the distance between the beginning of the fog until it becomes dense is very long. <br/><br/>A high bias creates dense fog.<br/>The area in which it completely obscures the view is only a short distance behind the area in which it begins.<br/> |
| **Color** (Vector4) | Defines the color of the fog. <br/>Tip: you can use the alpha channel to fade out fog color impact. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |


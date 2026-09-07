# CylinderMesh

*in [Lib.mesh.generate](README.md)*

Generates a procedural three-dimensional mesh which can be rendered with [DrawMesh], [DrawMeshUnlit], and [DrawMeshHatched] among others.

This mesh can be generated with or without closing caps. You can use the Height, Fill, and RadiusOffset parameters to generate things like pie charts, torus diagrams, cones, and other shapes.
Check the presets.

Tip:
- set the cap segments to 0 to skip drawing caps.
Also see [NGonMesh]

For a simple and interactive tutorial on the TiXL rendering pipeline, see [HowToDrawThings].

It can also become the source for a particle system with [MeshVerticesToPoints] or [PointsOnMesh].
To import static 3D meshes from other programs refer to [LoadObj].



## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Radius** (Single) | Defines the thickness of the Mesh |
| **RadiusOffset** (Single) | Scales the radius of the surface on top in relation to the radius. |
| **Height** (Single) | Scales the height of the Mesh |
| **Rows** (Int32) | Defines the subdivisions in height |
| **Columns** (Int32) | Defines the subdivisions of the outer surface |
| **CapSegments** (Int32) | Creates subdivisions on the top and bottom sides |
| **Spin** (Single) | Rotates the Geometry around the center |
| **Twist** (Single) | Rotates the surface on top in relation to the "Spin" setting |
| **Fill** (Single) | Cuts and compresses/slides the geometry along the radius |
| **Center** (Vector3) | — |
| **BasePivot** (Single) | Offsets the position of the pivot point (center).<br/><br/>This is helpful to change the point around which the cube rotates/along which it is scaled. |
| **Rotation** (Vector3) | Rotates the Mesh |

## Outputs
| Name | Type |
|---|---|
| **Data** | T3.Core.DataTypes.MeshBuffers |


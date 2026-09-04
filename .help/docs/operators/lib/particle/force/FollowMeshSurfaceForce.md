# FollowMeshSurfaceForce

*in [Lib.particle.force](README.md)*

Contains particles close to a surface of a simple(!) mesh geometry.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Amount** (Single) | The acceleration towards and on the surface. |
| **RandomizeSpeed** (Single) | A random factor on the target speed. |
| **SurfaceDistance** (Single) | The distance above the surface the particles will try to maintain.<br/><br/>Note: The effect does not work well with negative distances. |
| **RandomSurfaceDistance** (Single) | A random offset on the target surface distance. |
| **Spin** (Single) | Add a random rotation. This could be useful for wiggling or vein-like motions. |
| **RandomSpin** (Single) | A continuous random offset onto the spin. This can lead to more natural looking effects. |
| **RandomPhase** (Single) | Can be used to animate the random offsets mentioned above. |
| **MeshBuffers** (MeshBuffers Required) | — |
| **ShowGizmo** (GizmoVisibility) | When enabled shows an outline of the mesh if the output window has the Show Gizmos enabled. |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |


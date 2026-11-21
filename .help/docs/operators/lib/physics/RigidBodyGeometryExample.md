# RigidBodyGeometryExample

*in [Lib.physics](README.md)*

[VoronoiFracture] cuts a cube into cells; a [RigidBody] makes every cell a body, so the cube breaks apart when it lands. [RigidBodyGeometry] returns the pieces as geometry, ready for [GeometryToMesh] or any other geometry operator.

The ring on the right is drawn with ShowColliders: instead of its own surface you see the convex hulls it collides as.

AKA: fracture, shatter, break, debris, voronoi, collider view, debug

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- More seed points give smaller pieces; small pieces weigh less because mass follows volume.
- Toggle ShowColliders on the Pieces geometry too: fracture cells are already convex, so collider and drawing match.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |



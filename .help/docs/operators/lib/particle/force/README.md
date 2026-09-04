# Lib.particle.force

## Operators

- [**AxisStepForce**](AxisStepForce.md) — A force for the [ParticleSystem] that applies random accelerations toward an axis direction. This can lead to interesting results in motion design. If ApplyTrigger is true, a random selection ratio of particles is chosen and accelerated.
- [**CollisionForce**](CollisionForce.md) — A simple simulation of sphere collision between points. The radius of the points is defined on emit by the [ParticleSystem]'s PointRadiusW factor.
- [**CustomForce**](CustomForce.md) — Allows to write small shaders to create custom particle force effects.
- [**DirectionalForce**](DirectionalForce.md) — Generates a force acting on particles in a defined direction in space.
- [**FieldDistanceForce**](FieldDistanceForce.md) — Tries to keep particles within an SDF distance range.
- [**FieldVolumeForce**](FieldVolumeForce.md) — Allows the use of a signed distance field as a force to manipulate a particle system.
- [**FollowMeshSurfaceForce**](FollowMeshSurfaceForce.md) — Contains particles close to a surface of a simple(!) mesh geometry.
- [**MeshVolumeForce**](MeshVolumeForce.md) — Experimental force that moves particles along a low poly mesh.
- [**RandomJumpForce**](RandomJumpForce.md)
- [**ReconstructiveForce**](ReconstructiveForce.md) — Blends the simulation particle points toward the original matching emit points.
- [**SnapToAnglesForce**](SnapToAnglesForce.md) — Slowly align particle velocity with repeated angle steps on the xy-plane.
- [**SwitchParticleForce**](SwitchParticleForce.md) — Switches between connected forces for the ParticleSystem. Can be used to switch between forces or deactivate all.
- [**TextureMapForce**](TextureMapForce.md) — Accelerate particles from a signed normal map. The map is stretched to the camera clip space.
- [**TurbulenceForce**](TurbulenceForce.md) — Adds a turbulence force to a Particle Simulation.
- [**VectorFieldForce**](VectorFieldForce.md) — Applies a vector field to the particle velocity.
- [**VelocityForce**](VelocityForce.md) — Controls particle speeds by increasing, damping or limiting the particle velocit.y
- [**VerletRibbonForce**](VerletRibbonForce.md) — An experimental ribbon force simulation.
- [**VolumeForce**](VolumeForce.md) — Repels particles from primitive volumes.

---

*Auto-generated from the operator library.*

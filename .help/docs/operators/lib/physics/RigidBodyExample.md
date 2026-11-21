# RigidBodyExample

*in [Lib.physics](README.md)*

[RigidBody] turns any geometry into a collider. Shape Auto splits concave geometry into several convex hulls, so the rings keep their holes and slide down the posts they land over, and the letters keep their counters.

The same word falls twice: on the left with Pieces EachPartIsABody, so every letter is a body of its own, and on the right with WholeObject, so the word stays rigid. Bodies made from geometry weigh their volume times Density.

AKA: convex decomposition, compound body, concave collider, mesh collider, multi-part, ring toss

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- Set the Rings' Shape to ConvexHull and reset: every ring then fills its hole and rests on top of the posts.
- Turn on ShowColliders on a [RigidBodyGeometry] to see the hulls.
- MaxHulls trades accuracy against cost.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |



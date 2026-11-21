# RigidJoint

*in [Lib.physics](README.md)*

Declares a constraint between two bodies.

The world creates, updates and removes the actual constraint. Both bodies must be Dynamic or Kinematic; use a Kinematic body as the fixed anchor when you need a hinge or pendulum. The Weld type reads LocalOffsetB only, as the position of body B in the local space of body A.

AKA: constraint, hinge, ball socket, weld, distance servo

Tips
- For a ball socket, LocalOffsetA and LocalOffsetB are the same world point seen from each body.
- Hinge axes are given in each body's local space.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **MotorSpeed** (Single) | Hinge only: drives body B around the hinge axis at this speed in radians per second, for wheels and spinners. 0 leaves the hinge free. |
| **AutoAnchor** (Boolean) | Pair mode: put the joint at body B's starting position (plus LocalOffsetB) and work out body A's anchor and axis from where the bodies start. One joint then fits every instance of B, such as four wheels on one chassis. HingeAxisB is the axis; LocalOffsetA and HingeAxisA are ignored. |
| **DampingRatio** (Single) | 1 is critically damped; lower adds springiness. |
| **Mode** (Int32) | What gets linked. Pair: body A to body B, instance by instance. Chain: consecutive instances of body A, for ropes. Nearby: instances of body A that start within LinkDistance, for nets and breakable clumps. World: every instance of body A is held where it starts. Chain, Nearby and World ignore body B and anchor themselves from the starting positions. |
| **Frequency** (Single) | Spring frequency in Hz. Higher is stiffer. |
| **LocalOffsetA** (Vector3) | Anchor point in body A's local space. |
| **BodyB** (PhysicsBody) | Second body. Must be Dynamic or Kinematic. |
| **HingeAxisA** (Vector3) | Hinge axis in body A's local space. |
| **BodyA** (PhysicsBody) | First body. Must be Dynamic or Kinematic. |
| **LinkDistance** (Single) | Nearby mode: the largest distance between two body centres that still links them. |
| **LocalOffsetB** (Vector3) | Anchor point in body B's local space. Weld uses this as its translation. |
| **TargetDistance** (Single) | Distance held by the DistanceServo type. |
| **HingeAxisB** (Vector3) | Hinge axis in body B's local space. |
| **JointType** (Int32) | Constraint type: BallSocket, Hinge, Weld or DistanceServo. |
| **InstanceIndex** (Int32) | Pair and World modes: link only this instance of an instanced body, for example 0 to hang a rope by its first link. -1 links every instance; a number past the end means the last instance. |
| **Enabled** (Boolean) | Turns the constraint off without deleting the joint node. |
| **SwingLimit** (Single) | BallSocket only: the largest angle in degrees between HingeAxisA and HingeAxisB, so a limb cannot fold back on itself. Point both axes along the limb. 0 means no limit. |

## Outputs

| Name | Type |
|---|---|
| **OutJoint** | T3.Core.DataTypes.PhysicsJoint |



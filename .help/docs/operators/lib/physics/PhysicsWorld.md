# PhysicsWorld

*in [Lib.physics](README.md)*

Simulates rigid bodies with BepuPhysics and outputs their poses as a point buffer.

Time advances at a fixed rate, render poses are interpolated between the last two steps, and a timeline jump or rewind rebuilds the simulation. Bodies and joints are declared by [RigidBody] and [RigidJoint]; this world is the only stateful part of the system.

AKA: physics, bepu, rigid body dynamics, collisions, simulation

Tips
- Draw the output with [DrawMeshAtPoints] or [DrawPoints].
- A timeline change of more than half a second restarts the simulation, so scrub with care.
- IncludeStatics also writes static bodies such as the ground into the output.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **FixedRate** (Single) | Physics steps per second. The render rate never feeds the solver. |
| **Gravity** (Vector3) | World gravity in units per second squared. |
| **Bodies** (PhysicsBody) | Body operators to simulate. Connect RigidBody outputs here. |
| **ContactSpeed** (Single) | Closing speed in units per second from which an impact is reported in Contacts. Raise it to keep only hard hits. |
| **ContactStiffness** (Single) | Spring frequency of contacts in Hz. Higher is harder and less forgiving. |
| **MaxContacts** (Int32) | Length of the Contacts buffer. When more impacts happen in one frame the strongest are kept. 0 turns the output off. |
| **TimeScale** (Single) | Multiplier on the timeline delta. 0 freezes, 2 runs at double speed. |
| **Forces** (PhysicsForce) | Force operators acting on the dynamic bodies. Connect RigidForce outputs here. |
| **Substeps** (Int32) | Solver substeps per step. More substeps make stacks and joints stiffer. |
| **Multithreaded** (Boolean) | Spread collision and solving over worker threads. Disable to make stepping reproducible on one thread. |
| **Reset** (Boolean) | Rebuild the simulation from the body descriptions on the rising edge. |
| **SolverIterations** (Int32) | Velocity iterations per substep. |
| **Paused** (Boolean) | Freeze simulation time while still updating the output. |
| **Deterministic** (Boolean) | Ask Bepu for deterministic scheduling, so the same graph and thread count replay identically. |
| **IncludeStatics** (Boolean) | Also write static bodies into the Point buffer. Usually the ground is not part of the simulation output. |
| **Joints** (PhysicsJoint) | Joint operators connecting pairs of bodies. |
| **Interpolate** (Boolean) | Blend poses between fixed steps so motion stays smooth above the physics rate. |

## Outputs

| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |
| **BodyCount** | System.Int32 |
| **SimTime** | System.Single |
| **Contacts** | T3.Core.DataTypes.BufferWithViews |
| **ContactCount** | System.Int32 |



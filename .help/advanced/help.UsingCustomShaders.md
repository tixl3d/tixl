# Using Custom Shader Operators

Custom Shader Operators combine TiXL preset system with its shader pipeline to provide a very powerful and fun method to create useful custom operators.

## Preface
TiXL has hundreds of ops and effects. But at certain level it is no longer feasible to create new parameters for every single edge case.
This is where custom shader ops come into play.

As of v4.1 TiXL has:
- [CustomPixelShader]
- [CustomPointShader]
- [CustomForce]
- [CustomVertexShader]
- [CustomFaceShader]

You don't need to understand or write shader code to use these. Just open the preset window, create or select the Custom Shader op and experiment with the presets. Each of the ops come with a set of parameters Offset, A,B,C,D that can be used by the shader fragments. Ideally they should give some comments of what does what.

TODO: Add more details


## Writing Shaders
TODO: Add more details


## Built-in helper functions

| Name | Description | Parameters |
| --- | --- | --- |
|`Biased`| Applies the GainAndBias remapping | `float value` | 
|`SampleGradient`| Samples the color gradient at the giving position | `float value` | 


### Using Quaternions

| Name | Description | Parameters |
| --- | --- | --- |
| `qMul` | Performs standard Hamiltonian product of two quaternions. | `float4 q1`, `float4 q2` |
| `qRotateVec3` | Rotates a 3D vector by a quaternion using an optimized formula. | `float3 v`, `float4 q` |
| `qConjugate` | Returns the conjugate of a quaternion (negates the imaginary part). | `float4 q` |
| `qInverse` | Calculates the inverse of a quaternion. | `float4 q` |
| `qFromAngleAxis` | Creates a quaternion representing a rotation of `angle` around `axis`. | `float angle`, `float3 axis` |
| `qFromVectors` | Creates a quaternion representing the rotation from `v1` to `v2`. | `float3 v1`, `float3 v2` |
| `qLookAt` | Creates a rotation quaternion that points toward `forward` with specialized `up`. | `float3 forward`, `float3 up` |
| `qSlerp` | Performs Spherical Linear Interpolation between two quaternions. | `float4 a`, `float4 b`, `float t` |
| `qFromEuler` | Converts Euler angles (yaw, pitch, roll) to a quaternion. | `float yaw`, `float pitch`, `float roll` |
| `qToMatrix` | Converts a quaternion into a $4\times4$ rotation matrix. | `float4 quat` |
| `qFromMatrix3` | Simplified conversion from a $3\times3$ matrix to a quaternion. | `float3x3 m` |
| `qFromMatrix3Precise` | Robust conversion from a $3\times3$ matrix to a quaternion (handles all cases). | `float3x3 m` |



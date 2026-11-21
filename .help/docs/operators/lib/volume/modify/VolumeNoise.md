# VolumeNoise

*in [Lib.volume.modify](README.md)*

Adds procedural fractal noise to a volume.

Perlin, Simplex or Worley noise with fractal combination. Add and Multiply modulate the values, Displace pushes level-set surfaces along the noise for organic shapes, and Replace writes the noise itself.

AKA: turbulence, displacement, perlin, simplex, worley, fbm

Tips
- Animate Offset to make the noise flow.
- Displace scales the amplitude by the voxel size, so an Amplitude of 2 moves the surface up to about two voxels.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Fractal** (Int32) | How octaves are combined: None, Fbm, Ridged or Billow. |
| **Volume** (VdbVolume) | The volume to modulate. In Replace mode only its topology is used. |
| **Frequency** (Single) | How quickly the noise varies across space. Higher values give finer features. |
| **Octaves** (Int32) | Number of noise layers (1 to 8). More octaves add fine detail and cost time. |
| **Mode** (Int32) | Add, Multiply, Displace (level sets) or Replace. |
| **Seed** (Int32) | Seed of the noise generator. |
| **Offset** (Vector3) | Shifts the noise lookup position in noise space. Animate it to flow the pattern. |
| **NoiseType** (Int32) | Perlin, Simplex or Worley. |
| **Amplitude** (Single) | Strength of the noise. For Displace it is measured in voxels. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Volume.VdbVolume |



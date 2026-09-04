# SamplePointsByCameraDistance

*in [Lib.point.modify](README.md)*

Changes the W value / F value of existing points based on their distance to the active camera.
For example, to make distant points appear larger or smaller than nearby points, etc.
Needs a point source such as: [GridPoints], [RadialPoints], [SpherePoints] etc.
And [DrawPoints], [DrawLines] or similar in order to view the result.

Useful combinations: [GridPoints] [TransformPoints]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews) | Point input |
| **NearRange** (Single) | Defines the minimum distance from which points are taken into account. |
| **FarRange** (Single) | Defines the maximum distance from which points are taken into account. |
| **WForDistance** (Curve) | Curve that defines which value the points are assigned based on their distance |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |


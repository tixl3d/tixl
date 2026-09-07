# MeshFacesPoints

*in [Lib.mesh.modify](README.md)*



Create points at the center, corners or edges of the mesh's faces.

The size of each of the faces defines the size of the points and FX1 as the W attribute gets the radius. 

In center mode uses the "Inscribed Circle Center".

Useful combinations [LoadObj] and [DrawPoints] and more.

Similar Operators:
To create a point at each vertex of the connected mesh [MeshVerticesToPoints] can be used.
To get evenly distributed points on a mesh [PointsOnMesh] can be used.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputMesh** (MeshBuffers) | Mesh input via [LoadObj] for example |
| **Mode** (Int32) | Defines the output mode. This directly effects the resulting point count,  e.g. Loop mode results in FaceCount × 5 points because of an added separator. <br/><br/>Corners is because identical to [MeshVerticesToPoints]. |
| **ScaleUniform** (Single) | — |
| **Stretch** (Vector3) | — |
| **ScaleWithFaceArea** (Boolean) | — |
| **OffsetScale** (Single) | This can be useful to "inset" edge loops.<br/> |
| **Fx1** (Single) | Scales the W / size of the points. |
| **Fx2** (Single) | — |
| **Color** (Vector4) | Defines the color of the points |
| **Texture** (Texture2D) | — |
| **OffsetByTBN** (Vector3) | Moves the points along one of the axes of the normal |
| **Orientation** (Int32) | UseUpVector will look more consistent in most scenarios.<br/>TBN (Tangent, BiTagent and Normal) will be more precise a expecially for instanciating more content at the points. |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |


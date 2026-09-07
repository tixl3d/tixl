# Sketch

*in [Lib.image.generate.misc](README.md)*

Allows adding sketches as annotations or storyboards. You can create multiple pages and switch between them by selecting or animating the PageIndex. Use the ShowAs parameter to toggle between showing the sketches as a single frame (e.g., as a comment), until the next frame (e.g., as a storyboard or animation), or with onion skinning. You can also use the following keyboard shortcuts:

[P] - Draw mode
[E] - Erase mode
[X] - Cut Page
[V] - Insert cut page
[0-9] - Select brush size

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Scene** (Command) | — |
| **InputImage** (Texture2D) | — |
| **Filename** (String) | — |
| **StrokeSize** (Single) | — |
| **StrokeColor** (Vector4) | — |
| **ColorMode** (Int32) | — |
| **Background** (Vector4) | — |
| **ShowOnionSkin** (Boolean) | — |
| **OverridePageIndex** (Int32) | — |
| **Resolution** (Int2) | — |
| **OnionSkinRange** (Single) | — |
| **SyncToKeyframes** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **ColorBuffer** | T3.Core.DataTypes.Texture2D |
| **Points** | T3.Core.DataTypes.BufferWithViews |


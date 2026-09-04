# RemoveStaticBackground

*in [Lib.image.analyze](README.md)*

Uses statistical filtering to remove static background from an image.

This is basically a keyer that tries to detect movement in front of a static background.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **Update** (Boolean) | — |
| **OutputMode** (Int32) | — |
| **MeanRate** (Single) | How quickly the background “memory” adapts to slow changes like lighting drift. Higher values settle faster, but risk absorbing slow-moving people into the background. |
| **SpreadUpRate** (Single) | How fast the system becomes more tolerant when a pixel suddenly gets noisy or unstable. Higher values reduce flicker, but can make the key less sensitive. |
| **SpreadDownRate** (Single) | How slowly the tolerance tightens again when things become stable. Lower values keep the key steady over time and avoid nervous, popping edges |
| **MinSpread** (Single) | A minimum "noise cushion" so perfectly flat areas don’t become hypersensitive. Increase it if you see sparkly false positives in quiet regions. |
| **BackgroundGateLo** (Single) | The point where the system starts to distrust changes and begins to slow down background learning. Raise it if you want the background to keep adapting even with mild motion. |
| **BackgroundGateHi** (Single) | The point where the system stops learning from a pixel because it’s likely foreground. Lower it to prevent “burn-in” of people; raise it if the background needs to adapt under stronger changes. |
| **ZScale** (Single) | Overall sensitivity knob for what counts as "different from the background". <br/>Higher values key more aggressively; lower values are more forgiving. |
| **BrightSuppression** (Single) | How much to ignore pixels that get brighter than the background (useful for projectors adding light). Lower values ignore bright projection changes more strongly, focusing on dark occlusions like people. |
| **EnableChroma** (Boolean) | — |
| **ChromaWeight** (Single) | How strongly color changes influence the key compared to brightness changes. Increase it to react more to hue shifts; decrease it if you only care about silhouettes from light/dark changes. |
| **VoteThreshold** (Single) | How “foreground” a pixel must look before it really counts as a vote in the neighborhood check. Raise it to ignore weak, noisy changes; lower it to pick up faint motion. |
| **DensityRange** (Vector2) | x: The minimum amount of nearby agreement needed before the mask starts strengthening a pixel. Raise it to require bigger, more solid shapes; lower it to keep thinner details.<br/><br/>y: The density where the neighborhood consensus is treated as fully confident. Lower it to fill gaps more aggressively; raise it to stay stricter and preserve fine edges.<br/> |
| **TemporalStability** (Single) | — |
| **KeepOriginal** (Single) | How much of the raw mask is kept versus the neighborhood-refined result. Higher values preserve delicate details but keep more noise; lower values look cleaner and more "chunky". |
| **IsTraining** (Boolean) | — |
| **TrainingMeanRate** (Single) | — |
| **TrainingSpreadUpRate** (Single) | — |
| **TrainingSpreadDownRate** (Single) | — |
| **TrainingBrightSuppression** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


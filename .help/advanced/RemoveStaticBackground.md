## RemoveStaticBackground

**RemoveStaticBackground** is a real-time effect that separates people or other moving elements from a static camera image.  
It is designed for artistic and interactive contexts where stability, predictability, and debuggability are more important than aggressive or opaque “AI-style” segmentation.

Typical use-cases include interactive installations, projection-based performances, silhouette extraction for visuals, and webcam-driven interaction where lighting and image quality are not ideal.


## What it does

The effect continuously learns what the empty scene looks like and how much each pixel normally fluctuates due to noise, compression, or subtle lighting changes. Pixels are only considered foreground when they deviate more than expected and do so consistently in space and time.  

The result is a stable foreground mask that preserves the main outline of people while suppressing flicker, speckle noise, and single-frame artifacts.


## How it works

The system builds a simple statistical model per pixel, consisting of a background color and an expected deviation (“spread”). During normal operation, the model updates slowly and selectively, avoiding learning pixels that look like foreground.  

Foreground detection compares the current image against this learned background, normalized by the pixel’s own noise level. This produces a soft per-pixel confidence rather than a hard threshold.  

To improve robustness, the raw foreground signal is first validated temporally, requiring consistency across consecutive frames to remove webcam noise. After that, a spatial density check enforces regional coherence, ensuring that pixels belong to a larger foreground shape rather than appearing in isolation.

An explicit training mode can be enabled to force rapid learning when the scene is known to be empty, such as at startup or after camera or lighting changes.


## Limitations

The effect assumes a static camera and a mostly static background. Large or sudden lighting changes require retraining. Very fast motion may appear with a one-frame delay due to temporal validation. The system is not designed to handle moving backgrounds or to replace depth-based segmentation.

## How to tweak it

Start by adjusting **ZScale**, which controls overall sensitivity. Increase it until the main silhouette appears clearly, then stop before noise becomes visible.  

Next, adjust **TemporalHold** to suppress flicker from noisy cameras; values around 0.3–0.5 usually provide a good balance between stability and responsiveness.  

If small holes or speckles remain, refine **VoteThreshold** and **DensityLo / DensityHi** to require stronger neighborhood agreement. Finally, use **KeepOriginal** to balance between preserving fine details and producing a more solid, stable shape.

Debug views such as *Spread*, *Z*, and *Range* are strongly recommended during tuning, as they reveal how the system perceives noise and change.

---

## Parameters

| Name | Description | Typical Range |
|-----|------------|---------------|
| MeanRate | Speed at which the background color adapts | 0.002 – 0.02 |
| SpreadUpRate | How fast noise tolerance grows | 0.02 – 0.1 |
| SpreadDownRate | How slowly tolerance shrinks | 0.0005 – 0.005 |
| MinSpread | Minimum noise floor | 0.003 – 0.01 |
| ZScale | Detection sensitivity (does not affect learning) | 0.02 – 2.0 |
| BrightSupp | Suppression of brightness increases (projectors) | 0.2 – 1.0 |
| IsTraining | Forces all pixels to be learned as background | 0 or 1 |
| VoteThreshold | How confident a pixel must be to vote as foreground | 0.3 – 0.7 |
| DensityLo | Minimum neighborhood agreement | 0.15 – 0.35 |
| DensityHi | Full neighborhood agreement | 0.3 – 0.6 |
| KeepOriginal | Blend between raw detail and refined stability | 0.0 – 1.0 |
| TemporalHold | Requires persistence across frames | 0.0 – 1.0 |

---

**RemoveStaticBackground** is best understood as a controllable, transparent foreground extractor rather than a black-box keyer. Its strength lies in making the trade-offs between sensitivity, stability, and responsiveness visible and adjustable.

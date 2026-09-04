# Lib.numbers.float.process

## Operators

- [**Accumulator**](Accumulator.md) — Accumulates a value with the incoming rate.
- [**BlendValues**](BlendValues.md) — Blends a number of incoming values with a float value.
- [**Damp**](Damp.md) — Damps (i.e. smoothens or filters) an incoming float value.
- [**DampAngle**](DampAngle.md) — Damps (i.e. smoothens or filters) an incoming float value. Avoids flips when jumping from 359 to 0 degrees.
- [**DetectPulse**](DetectPulse.md) — Detects if rapid changes within an input signal exceed a threshold. This can be useful for detecting beats in OSC or audio signals.
- [**Ease**](Ease.md) — Note: The input value should evolve by steps (e.g., sudden changes) to fully utilize the easing effect.
- [**EaseKeys**](EaseKeys.md) — This operator creates smooth, ease-in-out transitions by overriding the easing of keyframes. Customize the transition style with options like cubic, exponential, or back easing, or choose a linear fallback for a simple interpolation.
- [**FreezeValue**](FreezeValue.md) — Keeps the current input value as long as Keep is true.
- [**HasValueDecreased**](HasValueDecreased.md) — Tests whether the incoming value has decreased by a defined threshold and outputs this as a Boolean
- [**Lerp**](Lerp.md) — Blends between two values (lerp). Also see [BlendValues] to blend between more values and [Remap] to blend between two value ranges.
- [**PeakLevel**](PeakLevel.md) — Analyzes the incoming value changes and outputs information about the peaks
- [**RemapValues**](RemapValues.md) — Takes in a list of Vector2 and picks the y component of the pair where X is closest to LookupValue.
- [**SmoothStep**](SmoothStep.md) — Smoothes an incoming animated value
- [**Spring**](Spring.md) — Adds a bounce effect to the incoming value.

---

*Auto-generated from the operator library.*

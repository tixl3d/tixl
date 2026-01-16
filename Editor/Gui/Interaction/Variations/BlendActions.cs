using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Variations;

/// <summary>
/// Forwards abstract actions triggered by midi-inputs to ac
/// </summary>
public static class BlendActions
{
    public static void StartBlendingTowardsSnapshot(int index)
    {
        if (VariationHandling.ActiveInstanceForSnapshots == null || VariationHandling.ActivePoolForSnapshots == null)
        {
            Log.Warning("Can't blend without active composition or variation pool");
            return;
        }

        if (SymbolVariationPool.TryGetSnapshot(index, out var variation))
        {
            // Log.Debug($"Start blending towards: {index}");
            _blendTowardsIndex = index;
            VariationHandling.ActivePoolForSnapshots.BeginBlendTowardsSnapshot(VariationHandling.ActiveInstanceForSnapshots, variation, 0);
        }
    }

    public static void UpdateBlendingTowardsProgress(int index, float midiValue)
    {
        if (VariationHandling.ActiveInstanceForSnapshots == null || VariationHandling.ActivePoolForSnapshots == null)
        {
            Log.Warning("Can't blend without active composition or variation pool");
            return;
        }

        if (_blendTowardsIndex == -1)
        {
            return;
        }

        if (SymbolVariationPool.TryGetSnapshot(_blendTowardsIndex, out var variation))
        {
            var normalizedValue = midiValue / 127.0f;
            SmoothVariationBlending.StartBlendTo(variation, normalizedValue);
        }
        else
        {
            SmoothVariationBlending.Stop();
        }
    }
    
    public static void StopBlendingTowards()
    {
        _blendTowardsIndex = -1;
        VariationHandling.ActivePoolForSnapshots.ApplyCurrentBlend();
        BlendActions.SmoothVariationBlending.Stop();
    }
    
    public static void UpdateBlendValues(int obj, float value)
    {
        //Log.Warning($"BlendValuesUpdate {obj} not implemented");
    }

    public static void StartBlendingSnapshots(int[] indices)
    {
        Log.Warning($"StartBlendingSnapshots {indices.Length} not implemented");
    }
    
    /// <summary>
    /// Smooths blending between variations to avoid glitches by low 127 midi resolution steps 
    /// </summary>
    public static class SmoothVariationBlending
    {
        public static void StartBlendTo(Variation variation, float normalizedBlendWeight)
        {
            if (variation != _targetVariation)
            {
                _dampedWeight = normalizedBlendWeight;
                _targetVariation = variation;
            }

            _targetWeight = normalizedBlendWeight;
            UpdateBlend();
        }

        public static void UpdateBlend()
        {
            if (_targetVariation == null || VariationHandling.ActiveInstanceForSnapshots == null)
                return;

            if (float.IsNaN(_dampedWeight) || float.IsInfinity(_dampedWeight))
            {
                _dampedWeight = _targetWeight;
            }

            if (float.IsNaN(_dampingVelocity) || float.IsInfinity(_dampingVelocity))
            {
                _dampingVelocity = 0.5f;
            }

            var frameDuration = 1 / 60f;    // Fixme: (float)Playback.LastFrameDuration
            _dampedWeight = MathUtils.SpringDamp(_targetWeight,
                                                 _dampedWeight,
                                                 ref _dampingVelocity,
                                                 20f, frameDuration);

            if (MathF.Abs(_dampingVelocity) < 0.0005f)
                return;

            VariationHandling.ActivePoolForSnapshots?.
                              BeginBlendTowardsSnapshot(VariationHandling.ActiveInstanceForSnapshots, 
                                                        _targetVariation, 
                                                        _dampedWeight);
        }

        public static void Stop()
        {
            _targetVariation = null;
        }

        private static float _targetWeight;
        private static float _dampedWeight;
        private static float _dampingVelocity;
        private static Variation _targetVariation;
    }

    
    private static int _blendTowardsIndex = -1;


}
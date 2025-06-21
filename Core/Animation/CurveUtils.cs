namespace T3.Core.Animation;

public static class CurveUtils
{
    public enum OutsideCurveBehavior
    {
        Constant = 0,
        Cycle,
        CycleWithOffset,
        Oscillate
    };

    internal static IOutsideCurveMapper CreateOutsideCurveMapper(OutsideCurveBehavior outsideBehavior)
    {
        return outsideBehavior switch
                   {
                       OutsideCurveBehavior.Constant        => new ConstantCurveMapper(),
                       OutsideCurveBehavior.Cycle           => new CycleCurveMapper(),
                       OutsideCurveBehavior.CycleWithOffset => new CycleWithOffsetCurveMapper(),
                       OutsideCurveBehavior.Oscillate       => new OscillateCurveMapper(),
                       _                                    => throw new System.Exception("undefined outside behavior")
                   };
    }
}
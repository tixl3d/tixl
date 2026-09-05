using T3.Core.Utils;

namespace Lib.point._internal;

[Guid("bdd982c4-dfc4-48d6-888a-f067081dbe8e")]
internal sealed class _MixPoints : Instance<_MixPoints>
{
    [Output(Guid = "5bf5f55e-9099-4413-b17a-f49d042cb4ca")]
    public readonly Slot<Point[]> Result = new();

    public _MixPoints()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // var countX = CountX.GetValue(context).Clamp(1, 100);
        // var countY = CountY.GetValue(context).Clamp(1, 1);

        var listA = A.GetValue(context);
        var listB = B.GetValue(context);
        if (listA == null || listA.Length == 0 || listB == null || listB.Length == 0)
            return;

        var combination = (Combinations)Combination.GetValue(context);

        //var count = countX * countY;
        if (_points.Length != listA.Length)
            _points = new Point[listA.Length];

        var factor = Factor.GetValue(context);
        var mode = (Modes)Mode.GetValue(context);
        switch (combination)
        {
            case Combinations.Modulo:
            {
                for (var index = 0; index < listA.Length; index++)
                {
                    var pA = listA[index % listA.Length];
                    var pB = listB[index % listB.Length];

                    ComputeStep(index, pA, pB);
                }
                break;
            }
            case Combinations.Interpolate:
            {
                float bStep = (float)listB.Length / (listA.Length-0.999f);

                for (var index = 0; index < listA.Length; index++)
                {
                    var pA = listA[index % listA.Length];
                    var bPointer = bStep * index;
                    var bIndex = (int)bPointer;
                    var fraction = bPointer - bIndex;
                    try
                    {
                        var pB1 = listB[bIndex < listB.Length - 1 ? bIndex  : listB.Length-1];
                        var pB2 = listB[bIndex < listB.Length - 2 ? bIndex + 1 : listB.Length - 1];
                        var pB = new Point()
                                     {
                                         Position = Vector3.Lerp(pB1.Position, pB2.Position, fraction),
                                         F1 = MathUtils.Lerp(pB1.F1, pB2.F1, fraction),
                                         Orientation =  Quaternion.Identity,
                                     };
                        //pB.Position.W = 1;
                        ComputeStep(index, pA, pB);
                    }
                    catch (Exception)
                    {
                        Log.Error("incorrect index calculation: \nindex: {index}  bIndex {bIndex}  fraction: {fraction}  lengthA:{listA.Length}  lengthB:{listB.Length}", this);
                    }
                }
                break;
            }
        }
            
        Result.Value = _points;

        void ComputeStep(int index, Point pA, Point pB)
        {
            switch (mode)
            {
                case Modes.Add:
                    _points[index].Position = pA.Position + pB.Position;
                    break;

                case Modes.Multiply:
                    _points[index].Position = pA.Position * pB.Position;
                    break;

                case Modes.Blend:
                    _points[index].Position = Vector3.Lerp(pA.Position, pB.Position, factor);
                    break;
            }
        }
    }


    private const float Pi2 = (float)Math.PI * 2;
    private Point[] _points = new Point[0];

    enum Modes
    {
        Add,
        Multiply,
        Blend,
    }

    enum Combinations
    {
        Modulo,
        Interpolate,
    }
        
    [Input(Guid = "57F1D1D3-B437-4761-A5F5-0520CF820F58")]
    public readonly InputSlot<Point[]> A = new();
        
    [Input(Guid = "3119875E-A6EA-4D19-B536-513459A0DB98")]
    public readonly InputSlot<Point[]> B = new();
        
    [Input(Guid = "e8e8d26f-ccd1-4c15-b215-9c5bcfc133fb", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "41BF7407-D81F-433C-9C73-41D2C831FE02")]
    public readonly InputSlot<float> Factor = new();

    [Input(Guid = "CACFC7CE-19AA-41B8-81FE-79E2D211C8F5", MappedType = typeof(Combinations))]
    public readonly InputSlot<int> Combination = new();

        
    // [Input(Guid = "6bcc7eb9-fd84-4eed-9817-ab43710425cf")]
    // public readonly InputSlot<Vector3> Start = new InputSlot<Vector3>();
    //
    // [Input(Guid = "49622fdc-a9eb-419c-8163-5a333e9dc543")]
    // public readonly InputSlot<float> StartW = new InputSlot<float>();
    //
    // [Input(Guid = "96dfec6b-fbc4-4fb8-86fd-99a3796c8866")]
    // public readonly InputSlot<Vector3> Scale = new InputSlot<Vector3>();
    //
    // [Input(Guid = "e020a2ba-3233-450a-a90b-39a47b8f0f7f")]
    // public readonly InputSlot<float> ScaleW = new InputSlot<float>();
    //
    // [Input(Guid = "64e29a9d-9510-49aa-9bb9-936a18bb69e1")]
    // public readonly InputSlot<int> CountX = new InputSlot<int>();
    //
    // [Input(Guid = "b6751ca8-438a-465c-839f-daee548d0e46")]
    // public readonly InputSlot<int> CountY = new InputSlot<int>();
}
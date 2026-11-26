#nullable enable
using Lib.Utils;

namespace Lib.geometry.convert;

/// <summary>
/// Flattens the curve primitives of a Geometry into GPU points, ready for [DrawLines] and the point ops: the sampling of
/// [CurvesToCPoints] followed by the upload of [UploadToGpu].
/// </summary>
[Guid("d955fd1d-af90-480b-b4e6-9142a1daf0a9")]
internal sealed class CurvesToPoints : Instance<CurvesToPoints>, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "8c437c4a-72a2-4f86-a7e0-ef8003851bd2")]
    public readonly Slot<BufferWithViews?> OutBuffer = new();

    public CurvesToPoints()
    {
        OutBuffer.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastSource = Curves.GetValue(context);
        var points = _sampler.Sample(_lastSource,
                                     Tolerance.GetValue(context),
                                     CloseLoops.GetValue(context),
                                     (CurvePointSampler.Samplings)Math.Clamp(Sampling.GetValue(context), 0, 2),
                                     (CurvePointSampler.PolygonEdgeModes)Math.Clamp(PolygonEdges.GetValue(context), 0, 2),
                                     SharpAngle.GetValue(context),
                                     CurvePointSampler.FieldSource.Parse(SetF1To.GetValue(context)),
                                     CurvePointSampler.FieldSource.Parse(SetF2To.GetValue(context)));
        _lists.Clear();
        if (points != null)
            _lists.Add(points);

        OutBuffer.Value = _uploader.Upload(_lists, this);
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => _sampler.MissingAttributeName == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    public string GetStatusMessage() => _sampler.MissingAttributeName == null ? string.Empty : $"No float or int attribute '{_sampler.MissingAttributeName}'; its field keeps the default.";

    #region ICustomDropdownHolder - keywords and the float and int attributes of the last input
    string ICustomDropdownHolder.GetValueForInput(Guid inputId) => CurvePointSampler.FieldSource.GetDisplayValue(inputId == SetF1To.Input.InputDefinition.Id ? SetF1To.Value : SetF2To.Value);

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId) => CurvePointSampler.FieldSource.GetOptions(_lastSource);

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (selected == null)
            return;

        if (inputId == SetF1To.Input.InputDefinition.Id)
        {
            SetF1To.SetTypedInputValue(CurvePointSampler.FieldSource.ToStoredValue(selected));
        }
        else if (inputId == SetF2To.Input.InputDefinition.Id)
        {
            SetF2To.SetTypedInputValue(CurvePointSampler.FieldSource.ToStoredValue(selected));
        }
    }
    #endregion

    private Geometry? _lastSource;
    private readonly CurvePointSampler _sampler = new();
    private readonly StructuredListUploader _uploader = new();
    private readonly List<StructuredList> _lists = new(1);

    [Input(Guid = "126f23f9-158b-468f-ba25-dd55c665a63c")]
    public readonly InputSlot<Geometry> Curves = new();

    [Input(Guid = "682bab73-6b13-4f0d-9162-a464b1889b56")]
    public readonly InputSlot<float> Tolerance = new();

    [Input(Guid = "b766fe96-2b4b-4a9e-b8e8-f3ba646970fe")]
    public readonly InputSlot<bool> CloseLoops = new();

    [Input(Guid = "d6da84fa-512f-480f-b6db-8bf777f5a3b2", MappedType = typeof(CurvePointSampler.Samplings))]
    public readonly InputSlot<int> Sampling = new();

    [Input(Guid = "bbbca9ff-7678-4549-82e0-aa2a988b49de", MappedType = typeof(CurvePointSampler.PolygonEdgeModes))]
    public readonly InputSlot<int> PolygonEdges = new();

    [Input(Guid = "f48316ce-a09a-4599-8d60-3fe0fcf24bfe")]
    public readonly InputSlot<float> SharpAngle = new();

    [Input(Guid = "b4c7768e-07c8-412f-8b7a-6ecee84ce3f5")]
    public readonly InputSlot<string> SetF1To = new();

    [Input(Guid = "06186624-8191-4526-aad0-5daefb0818ab")]
    public readonly InputSlot<string> SetF2To = new();
}

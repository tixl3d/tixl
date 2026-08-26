

// ReSharper disable RedundantNameQualifier

namespace Lib.point._experimental;

[Guid("271e397e-051c-473f-968f-a2251fed65d1")]
internal sealed class _GetSketchPoints : Instance<_GetSketchPoints>
{
    [Output(Guid = "9b1adfd0-2a94-438e-a0b9-6f6ae69f5690")]
    public readonly Slot<StructuredList> PointList = new();

    [Output(Guid = "FB4F4AC1-696C-4606-B479-ED8DADD166D3")]
    public readonly Slot<float> DistanceToCurrentTime = new();

    public _GetSketchPoints()
    {
        DistanceToCurrentTime.UpdateAction += Update;
        PointList.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Pages.GetValue(context) is not List<_SketchImpl.Page> orderedPages)
        {
            Log.Warning("Can't get pages from sketch implementation", this);
            PointList.Value = _emptyList;
            DistanceToCurrentTime.Value = 10000;
            return;
        }

        var pageIndex = PageIndex.GetValue(context);
        var useGetOnionSkin = GetOnionSkin.GetValue(context);

        if (useGetOnionSkin)
        {
            var pageIndexOffset = pageIndex;
            if (pageIndexOffset == 0)
            {
                PointList.Value = _emptyList;
                return;
            }

            var found = false;
            for (var index = 0; index < orderedPages.Count; index++)
            {
                var p = orderedPages[index];
                if (p.Time > context.LocalTime - 0.002f)
                {
                    if (pageIndexOffset < 0)
                    {
                        pageIndex = index + pageIndexOffset;
                    }
                    else
                    {
                        var index1 = (p.Time > context.LocalTime + 0.002f) ? 1 : 0;
                        pageIndex = index + pageIndexOffset- index1;
                    }

                    found = true;
                    break;
                }
            }

            if (!found)
            {
                pageIndex = orderedPages.Count + pageIndexOffset;
            }
        }

        var isValidIndex = pageIndex >= 0 && pageIndex < orderedPages.Count;
        if (!isValidIndex || orderedPages[pageIndex] == null)
        {
            PointList.Value = _emptyList;
            DistanceToCurrentTime.Value = 10000;
            return;
        }

        var activePage = orderedPages[pageIndex];
        PointList.Value = activePage.PointsList;
        DistanceToCurrentTime.Value = (float)(context.LocalTime - activePage.Time);
    }

    private static readonly StructuredList<Point> _emptyList = new(1);

    [Input(Guid = "C7C71DE4-A9A8-4194-B490-AB48004565D1")]
    public readonly InputSlot<object> Pages = new();

    [Input(Guid = "1b8d9a00-f337-431e-bab3-24d45ed9d191")]
    public readonly InputSlot<int> PageIndex = new();

    [Input(Guid = "DDBAC6AB-E547-4955-9346-680B2C5BF8A4")]
    public readonly InputSlot<bool> GetOnionSkin = new();
}


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

            var overriddenPageIndex = OverriddenPageIndex.GetValue(context);
            if (overriddenPageIndex >= 0)
            {
                SetPage(orderedPages, overriddenPageIndex + pageIndexOffset, -pageIndexOffset * OnionSkinDistancePerPage);
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
        var distance = isValidIndex && orderedPages[pageIndex] != null
                           ? (float)(context.LocalTime - orderedPages[pageIndex].Time)
                           : 0;
        SetPage(orderedPages, pageIndex, distance);
    }

    private void SetPage(List<_SketchImpl.Page> orderedPages, int pageIndex, float distance)
    {
        var isValidIndex = pageIndex >= 0 && pageIndex < orderedPages.Count;
        if (!isValidIndex || orderedPages[pageIndex] == null)
        {
            PointList.Value = _emptyList;
            DistanceToCurrentTime.Value = 10000;
            return;
        }

        PointList.Value = orderedPages[pageIndex].PointsList;
        DistanceToCurrentTime.Value = distance;
    }

    /**
     * With an overridden page index, page times say nothing about order, so neighbours are faded by their page offset.
     * The sketch fades a page out at half the onion skin range, so at range 1 the direct neighbours show faded.
     */
    private const float OnionSkinDistancePerPage = 0.25f;

    private static readonly StructuredList<Point> _emptyList = new(1);

    [Input(Guid = "C7C71DE4-A9A8-4194-B490-AB48004565D1")]
    public readonly InputSlot<object> Pages = new();

    [Input(Guid = "1b8d9a00-f337-431e-bab3-24d45ed9d191")]
    public readonly InputSlot<int> PageIndex = new();

    [Input(Guid = "DDBAC6AB-E547-4955-9346-680B2C5BF8A4")]
    public readonly InputSlot<bool> GetOnionSkin = new();

    /** -1 while pages follow time. */
    [Input(Guid = "7202B1A9-45C5-412C-9796-3451BA523CE5")]
    public readonly InputSlot<int> OverriddenPageIndex = new();
}
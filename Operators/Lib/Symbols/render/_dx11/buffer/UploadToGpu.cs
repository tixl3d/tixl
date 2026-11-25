#nullable enable
using Lib.Utils;

namespace Lib.render._dx11.buffer;

/// <summary>Uploads structured lists, mostly CPU points, into one GPU structured buffer.</summary>
[Guid("7e28c796-85e7-47ee-99bb-9599284dbeeb")]
internal sealed class UploadToGpu : Instance<UploadToGpu>
{
    [Output(Guid = "c52dfa83-9820-4a54-b90b-62278dc8fe3f")]
    public readonly Slot<BufferWithViews?> OutBuffer = new();

    [Output(Guid = "e1775fdf-af5a-49b2-b6fc-20e2180b71f5")]
    public readonly Slot<int> Length = new();

    public UploadToGpu()
    {
        OutBuffer.UpdateAction += Update;
        Length.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lists.Clear();
        var inputs = Lists.CollectedInputs;
        for (var index = 0; index < inputs.Count; index++)
        {
            var list = inputs[index].GetValue(context);
            if (list != null)
                _lists.Add(list);
        }

        Lists.DirtyFlag.Clear();

        OutBuffer.Value = _uploader.Upload(_lists, this);
        Length.Value = _uploader.ElementCount;
    }

    private readonly StructuredListUploader _uploader = new();
    private readonly List<StructuredList> _lists = [];

    [Input(Guid = "08F181BB-9777-497C-871D-BCC1FF252F2F")]
    public readonly MultiInputSlot<StructuredList> Lists = new();
}

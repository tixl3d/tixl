#nullable enable
using Lib.Utils;

namespace Lib.io.video;

[Guid("9412d0f4-dab8-4145-9719-10395e154fa7")]
[ExportDependencies("NDILibDotNet6.dll", "Processing.NDI.Lib.x64.dll")]
public sealed class NdiOutput : Instance<NdiOutput>, IStatusProvider
{
    [Output(Guid = "3c0ae0e5-a2af-4437-b7fa-8ad300cb8b8b", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Texture2D?> TextureOutput = new();

    public NdiOutput()
    {
        TextureOutput.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var texture = Texture.GetValue(context);
        var senderName = SenderName.GetValue(context) ?? "unknown";

        TextureOutput.Value = texture;

        if (texture == null)
        {
            _lastErrorMessage = "No texture input";
            return;
        }

        _sender ??= new NdiSender(senderName);
        _sender.Rename(senderName);
        _sender.FrameRate = FrameRate.GetValue(context);
        _sender.EnableAlpha = EnableAlpha.GetValue(context);
        _sender.Send(texture);
        _lastErrorMessage = _sender.LastError;
        SenderName.Update(context);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _sender?.Dispose();
        _sender = null;
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;

    string? IStatusProvider.GetStatusMessage() => _lastErrorMessage;

    private NdiSender? _sender;
    private string? _lastErrorMessage;

    [Input(Guid = "15ddab7a-aad4-49eb-9e56-70714e10679f")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "740db4c4-4182-4743-a713-5a45855499d9")]
    public readonly InputSlot<string> SenderName = new();

    [Input(Guid = "CE0E267E-E96D-441F-86FB-BACEF1F0186A")]
    public readonly InputSlot<int> FrameRate = new();

    [Input(Guid = "E6359F2E-E4F7-419D-A403-CE5EB0528898")]
    public readonly InputSlot<bool> EnableAlpha = new();
}

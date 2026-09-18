using Lib.Utils;

namespace Lib.io.video;

[Guid("13be1e3f-861d-4350-a94e-e083637b3e55")]
public class SpoutOutput : Instance<SpoutOutput>
{
    [Output(Guid = "297ea260-4486-48f8-b58f-8180acf0c2c5", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> TextureOutput = new();

    public SpoutOutput()
    {
        TextureOutput.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var texture = Texture.GetValue(context);
        var senderName = SenderName.GetValue(context);
        TextureOutput.Value = texture;

        _sender ??= new SpoutSender(senderName);
        _sender.Rename(senderName);
        if (_sender.Send(texture) && _sender.Name != senderName)
        {
            // Spout may pick another name when the requested one is taken — show the one receivers see.
            SenderName.SetTypedInputValue(_sender.Name);
        }

        SenderName.Update(context);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _sender?.Dispose();
        _sender = null;
    }

    private SpoutSender _sender;

    [Input(Guid = "d4b5c642-9cb9-4f41-8739-edbb9c6c4857")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "7C27EBD7-3746-4B70-A252-DD0AC0445B74")]
    public readonly InputSlot<string> SenderName = new();
}

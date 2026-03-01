using T3.Core.Utils;

namespace Lib.image.use;

[Guid("57c1fe66-d8bc-4ea5-ad25-6986d4c2bba4")]
 internal sealed class KeepPreviousFrame : Instance<KeepPreviousFrame>
{
    [Output(Guid = "B8C943B7-A402-4AE1-A489-EEFF900889CD")]
    public readonly Slot<Texture2D> PreviousFrame = new();
    
    [Output(Guid = "7ba708e0-1cbb-411a-aa2c-bc78d248d761")]
    public readonly Slot<Texture2D> CurrentFrame = new();


    public KeepPreviousFrame()
    {
        CurrentFrame.UpdateAction += UpdateTexture;
        PreviousFrame.UpdateAction += UpdateTexture;
    }

    private void UpdateTexture(EvaluationContext context)
    {
        var keep = Keep.GetValue(context);
        if (!ImageA.HasInputConnections || !keep)
        {
            return;
        }

        var texture = ImageA.GetValue(context);
        if (texture == null)
            return;

        var description = texture.Description;

        var formatChanged = _prevTextureA == null || _prevTextureA.IsDisposed ||
                            _prevTextureB == null || _prevTextureB.IsDisposed ||
                            description.Height != _prevTextureView.Height ||
                            description.Width != _prevTextureView.Width ||
                            description.Format != _prevTextureView.Format ||
                            description.MipLevels != _prevTextureView.MipLevels ||
                            description.OptionFlags != _prevTextureView.OptionFlags ||
                            description.SampleDescription.Count != _prevTextureView.SampleDescription.Count;

        try
        {
            if (formatChanged)
            {
                Utilities.Dispose(ref _prevTextureA);
                Utilities.Dispose(ref _prevTextureB);

                _prevTextureA = Texture2D.CreateTexture2D(description);
                _prevTextureB = Texture2D.CreateTexture2D(description);
                _prevTextureView = description;
            }

            ResourceManager.Device.ImmediateContext.CopyResource(texture, _bufferToggle ? _prevTextureA : _prevTextureB);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create Texture2d: {e.Message}", this);
        }

        CurrentFrame.Value = _bufferToggle ? _prevTextureA : _prevTextureB;
        PreviousFrame.Value = _bufferToggle ? _prevTextureB : _prevTextureA;
        CurrentFrame.DirtyFlag.Clear();
        PreviousFrame.DirtyFlag.Clear();

        _bufferToggle = !_bufferToggle;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        Utilities.Dispose(ref _prevTextureA);
        Utilities.Dispose(ref _prevTextureB);
    }
        
    private bool _bufferToggle;
    private Texture2D _prevTextureA;
    private Texture2D _prevTextureB;
    private Texture2DDescription _prevTextureView;

    [Input(Guid = "B304EFD5-A8E4-4213-9C85-8F482C55C880")]
    public readonly InputSlot<Texture2D> ImageA = new();

    [Input(Guid = "9BAE6E82-3691-4F1A-82E0-77B3953B7019")]
    public readonly InputSlot<bool> Keep = new();
}
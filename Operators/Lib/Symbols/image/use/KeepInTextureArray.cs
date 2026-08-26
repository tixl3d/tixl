#nullable enable
using SharpDX.Direct3D11;
using T3.Core.Utils;

namespace Lib.image.use;

[Guid("a783908a-63c2-4961-94ca-b97d17ed0788")]
internal sealed class KeepInTextureArray : Instance<KeepInTextureArray>
{
    [Output(Guid = "bc99ae41-3f32-481f-b88a-277e71468f0e")]
    public readonly Slot<Texture2D?> SelectedSlice = new();

    [Output(Guid = "3DF525E4-12F1-407E-8C56-35C1578F0E21")]
    public readonly Slot<Texture2D?> TextureArray = new();
    
    public KeepInTextureArray()
    {
        SelectedSlice.UpdateAction += Update;
        TextureArray.UpdateAction += Update;
    }
    
    private void Update(EvaluationContext context)
    {
        var sourceTexture = SourceTexture.GetValue(context);
        if ( sourceTexture == null)
        {
            Log.Warning("Invalid textures");
            return;
        }

        var arraySize = ArraySize.GetValue(context).Clamp(1, 256);
        

        var writeIndex = WriteIndex.GetValue(context);
        if (TriggerWrite.GetValue(context))
        {
            CopyIntoArray(sourceTexture, arraySize, writeIndex);
        }
        
        if (_arrayTexture == null)
            return;

        var readIndex = ReadIndex.GetValue(context).Mod(_arrayDesc.ArraySize);
        UpdateSliceToOutput(readIndex);
        
        TextureArray.Value = _arrayTexture;
        SelectedSlice.Value = _sliceTexture;
        
        TextureArray.DirtyFlag.Clear();
        SelectedSlice.DirtyFlag.Clear();
    }

    
    private void CopyIntoArray(Texture2D src, int arraySize, int writeIndex)
    {
        try
        {
            var srcDesc = src.Description;

            bool needsRecreate =
                _arrayTexture == null ||
                _arrayTexture.IsDisposed ||
                _sliceTexture == null ||
                _sliceTexture.IsDisposed ||
                srcDesc.Width  != _arrayDesc.Width ||
                srcDesc.Height != _arrayDesc.Height ||
                srcDesc.Format != _arrayDesc.Format ||
                srcDesc.SampleDescription.Count   != _arrayDesc.SampleDescription.Count ||
                srcDesc.SampleDescription.Quality != _arrayDesc.SampleDescription.Quality ||
                 _arrayDesc.BindFlags != BindFlags.ShaderResource || 
                _arrayDesc.OptionFlags != ResourceOptionFlags.None||
                _arrayDesc.ArraySize != arraySize;


            if (needsRecreate)
            {
                Utilities.Dispose(ref _arrayTexture);
                
                _arrayDesc = new Texture2DDescription
                                 {
                                     Width = srcDesc.Width,
                                     Height = srcDesc.Height,
                                     MipLevels = 1,
                                     ArraySize = arraySize,
                                     Format = srcDesc.Format,
                                     SampleDescription = srcDesc.SampleDescription,
                                     Usage = ResourceUsage.Default,
                                     BindFlags = BindFlags.ShaderResource,
                                     CpuAccessFlags = CpuAccessFlags.None,
                                     OptionFlags = ResourceOptionFlags.None
                                 };
                
                _arrayTexture= new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, _arrayDesc));
                
                Utilities.Dispose(ref _sliceTexture);
                _sliceDesc = new Texture2DDescription
                                 {
                                     Width = srcDesc.Width,
                                     Height = srcDesc.Height,
                                     MipLevels = 1,
                                     ArraySize = 1,
                                     Format = srcDesc.Format,
                                     SampleDescription = srcDesc.SampleDescription,
                                     Usage = ResourceUsage.Default,
                                     BindFlags = BindFlags.ShaderResource,
                                     CpuAccessFlags = CpuAccessFlags.None,
                                     OptionFlags = srcDesc.OptionFlags
                                 };
                _sliceTexture = new Texture2D(new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, _sliceDesc));
            }
            
            int dstSlice = writeIndex.Mod(arraySize);

            int dstSubresource = Resource.CalculateSubResourceIndex(
                                                                    0,
                                                                    dstSlice,
                                                                    _arrayDesc.MipLevels
                                                                   );

            ResourceManager.Device.ImmediateContext.CopySubresourceRegion(
                                                                          src,
                                                                          0, // src subresource (single texture → mip 0)
                                                                          null,
                                                                          _arrayTexture,
                                                                          dstSubresource
                                                                         );
        }
        catch (Exception e)
        {
            Log.Warning("Can't copy:" + e.Message, this);
        }
    }    
    
    private void UpdateSliceToOutput(  int sliceIndex)
    {
        if (_sliceTexture == null || _arrayTexture == null)
            return;
        
        try
        {
            int srcSubresource = Resource.CalculateSubResourceIndex( 0, sliceIndex, _arrayDesc.MipLevels );
            
            ResourceManager.Device.ImmediateContext.CopySubresourceRegion(
                                          _arrayTexture,
                                          srcSubresource,
                                          null,
                                          _sliceTexture,
                                          0
                                         );
        }
        catch(Exception e) 
        {
            Log.Warning("Failed to create texture: " + e.Message, this);
        }
    }
    
    
    
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        Utilities.Dispose(ref _sliceTexture);
        Utilities.Dispose(ref _arrayTexture);
    }

    private Texture2D? _sliceTexture;
    private Texture2DDescription _sliceDesc;

    private Texture2D? _arrayTexture;
    private Texture2DDescription _arrayDesc;

    
    [Input(Guid = "6E12ECDB-9B4C-40A4-87D8-5279E2D855D6")]
    public readonly InputSlot<int> ArraySize = new(0);
    

    [Input(Guid = "4CAC523C-27BF-4F57-B13F-8D41038C5587")]
    public readonly InputSlot<Texture2D> SourceTexture = new();
    
    
    [Input(Guid = "FF4701FE-16B8-48AA-9842-91D811D05306")]
    public readonly InputSlot<bool> TriggerWrite = new();

    [Input(Guid = "C829E9D6-A00F-4A80-8F9A-7C6152AAAC01")]
    public readonly InputSlot<int> WriteIndex = new();
    
    [Input(Guid = "0227dbe2-797b-465d-bc48-fd47f42b6cd9")]
    public readonly InputSlot<int> ReadIndex = new(0);
}
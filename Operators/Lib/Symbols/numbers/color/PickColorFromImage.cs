using SharpDX.Direct3D11;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.numbers.color;

[Guid("42703423-1414-489e-aac2-21a3d7204262")]
internal sealed class PickColorFromImage : Instance<PickColorFromImage>
{
    [Output(Guid = "4f0c5c55-74b3-46d9-bbbc-4aad5dc14ea3")]
    public readonly Slot<Vector4> Output = new();

    public PickColorFromImage()
    {
        Output.UpdateAction += Update;
    }

    private unsafe void Update(EvaluationContext context)
    {
        var alwaysUpdate = AlwaysUpdate.GetValue(context);
        var inputImage = InputImage.GetValue(context);

        var position = Position.GetValue(context);

        if (inputImage == null)
        {
            return;
        }

        var d3DDevice = ResourceManager.Device;
        var immediateContext = d3DDevice.ImmediateContext;
            
        var inputDescription = inputImage.Description;

        var column = ((int)(position.X * inputDescription.Width)).Clamp(0, inputDescription.Width - 1);
        var row = ((int)(position.Y * inputDescription.Height)).Clamp(0, inputDescription.Height - 1);
        //column = column.Clamp(0, inputImage.Description.Width - 1);

        if (alwaysUpdate
            || _imageWithCpuAccess == null
            || _imageWithCpuAccess.Description.Format != inputDescription.Format
            || _imageWithCpuAccess.Description.Width != inputDescription.Width
            || _imageWithCpuAccess.Description.Height != inputDescription.Height
            || _imageWithCpuAccess.Description.MipLevels != inputDescription.MipLevels
           )
        {
            // keep a copy of the texture which can be accessed by CPU
            var desc = new Texture2DDescription()
                           {
                               BindFlags = BindFlags.None,
                               Format = inputDescription.Format,
                               Width = inputDescription.Width,
                               Height = inputDescription.Height,
                               MipLevels = inputDescription.MipLevels,
                               SampleDescription = new SampleDescription(1, 0),
                               Usage = ResourceUsage.Staging,
                               OptionFlags = ResourceOptionFlags.None,
                               CpuAccessFlags = CpuAccessFlags.Read, // <- that we want
                               ArraySize = 1
                           };
            Utilities.Dispose(ref _imageWithCpuAccess);
            _imageWithCpuAccess = Texture2D.CreateTexture2D(desc);
            immediateContext.CopyResource(inputImage, _imageWithCpuAccess);
        }

        var width = inputDescription.Width;
        var height = inputDescription.Height;

        column %= width;
        row %= height;

        // Gets a pointer to the image data, and denies the GPU access to that subresource.            
        var sourceDataBox =
            immediateContext.MapSubresource(_imageWithCpuAccess, 0, 0, MapMode.Read, MapFlags.None, out var sourceStream);

        using (sourceStream)
        {

            Vector4 color;

            switch (inputDescription.Format)
            {
                case Format.R8G8B8A8_UNorm:
                {
                    // Position to the wanted pixel. 4 of bytes per pixel
                    sourceStream.Position = GetStartIndex(row, sourceDataBox.RowPitch, column, 4);
                    var colorBytes = new Byte4(sourceStream.Read<Int32>());
                    color = new Color(colorBytes);
                }
                    break;

                case Format.R16G16B16A16_Float:
                {
                    const int count = 8; // 2 bytes per float
                    var buffPtr = stackalloc byte[count];

                    sourceStream.Position = GetStartIndex(row, sourceDataBox.RowPitch, column, count);
                    sourceStream.Read((IntPtr)buffPtr, 0, count);

                    var fullSpan = new ReadOnlySpan<byte>(buffPtr, count);
                    var r = (float)BitConverter.ToHalf(fullSpan[..2]);
                    var g = (float)BitConverter.ToHalf(fullSpan[2..4]);
                    var b = (float)BitConverter.ToHalf(fullSpan[4..6]);
                    var a = (float)BitConverter.ToHalf(fullSpan[6..8]);
                        
                    color = new Vector4(r, g, b, a);
                }
                    break;

                case Format.R16G16B16A16_UNorm:
                {
                    sourceStream.Position = GetStartIndex(row, sourceDataBox.RowPitch, column, 8) + 1;
                    var r = sourceStream.ReadByte();
                    ++sourceStream.Position;
                    var g = sourceStream.ReadByte();
                    ++sourceStream.Position;
                    var b = sourceStream.ReadByte();
                    ++sourceStream.Position;
                    var a = sourceStream.ReadByte();
                    color = new Vector4(r, g, b, a);
                }
                    break;
                    
                case Format.R32G32B32A32_Float:
                    try
                    {
                        sourceStream.Seek(row * sourceDataBox.RowPitch + 16 * column, SeekOrigin.Begin);
                        var r = sourceStream.Read<float>();
                        var g = sourceStream.Read<float>();
                        var b = sourceStream.Read<float>();
                        var a = sourceStream.Read<float>();
                        color = new Vector4(r, g, b, a);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(" Exception in PickColorFromImage: " + e.Message, this);
                        return;
                    }
                    break;
                    
                default:
                    Log.Warning($"Can't access unknown texture format {inputDescription.Format}", this);
                    color = Color.White;
                    break;
            }

            Output.Value = color;
        }

        immediateContext.UnmapSubresource(_imageWithCpuAccess, 0);
        return;

        static int GetStartIndex(int row, int rowPitch, int column, int dataSize) => row * rowPitch + column * dataSize;
    }


    Texture2D _imageWithCpuAccess;

    [Input(Guid = "3b8c51c9-c544-47eb-9d70-4bd6b161be2d")]
    public readonly InputSlot<Texture2D> InputImage = new();

    [Input(Guid = "27C1B604-4883-4B20-83E1-C435BF9D5499")]
    public readonly InputSlot<Vector2> Position = new();

    [Input(Guid = "84FF0CE4-443D-438D-8FC7-6D6EDE75D67B")]
    public readonly InputSlot<bool> AlwaysUpdate = new();
}
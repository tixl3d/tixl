using System.Drawing;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.image.fx.@_;

[Guid("b536f791-ae9a-45a7-a153-e2f36a65cfb3")]
internal sealed class BuildAsciiFontSorting : Instance<BuildAsciiFontSorting>
{
    [Output(Guid = "684a6260-5bc5-40e7-8610-b0aa24d9a23d")]
    public readonly Slot<Texture2D> MappingTexture = new();

    public BuildAsciiFontSorting()
    {
        MappingTexture.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var filePath = Filepath.GetValue(context);

        if (!TryGetFilePath(filePath, out var absolutePath))
        {
            Log.Error($"Could not find file: {filePath}", this);
            return;
        }

        var filterCharacters = FilterCharacters.GetValue(context); 
        var brightnessTable = SortAsciiLetters(absolutePath, filterCharacters);
        if (brightnessTable == null)
        {
            return;
        } 

        //Log.Debug("Generating texture", this);

        var sampleCount = brightnessTable.Length;
        const int entrySizeInBytes = sizeof(float);
        var listSizeInBytes = sampleCount * entrySizeInBytes;
        var bufferSizeInBytes = 1 * listSizeInBytes;

        using (var dataStream = new SharpDX.DataStream(bufferSizeInBytes, true, true))
        {
            var texDesc = new Texture2DDescription()
                              {
                                  Width = sampleCount,
                                  Height = 1,
                                  ArraySize = 1,
                                  BindFlags = BindFlags.ShaderResource,
                                  Usage = ResourceUsage.Default,
                                  MipLevels = 1,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  Format = Format.R32_Float,
                                  SampleDescription = new SampleDescription(1, 0),
                              };

            var filterOutFactor = (256f - filterCharCount) / 256;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var index = (int)(sampleIndex * filterOutFactor).Clamp(0, sampleCount);
                dataStream.Write((float)brightnessTable[index].Index / 256f);
            }

            dataStream.Position = 0;
            var dataRectangles = new SharpDX.DataRectangle[] { new(dataStream.DataPointer, listSizeInBytes) };
            Utilities.Dispose(ref MappingTexture.Value);

            MappingTexture.Value = Texture2D.CreateTexture2D(texDesc, dataRectangles);
        }
    }

    private BrightnessEntry[] SortAsciiLetters( string imageFilePath,string filterCharacters)
    {
        Bitmap bitmapImage;

        try
        {
            bitmapImage = new Bitmap(imageFilePath);
        }
        catch (Exception e)
        {
            Log.Debug("Failed to load image " + imageFilePath + " " + e);
            return null;
        }

        const int Rows = 16;
        const int Columns = 16;

        BrightnessEntry[] letterBrightnessArray = new BrightnessEntry[Rows * Columns];

        // Initialize index table
        for (var i = 0; i < letterBrightnessArray.Length; i++)
        {
            letterBrightnessArray[i].Index = i;
        }

        // Add brightness
        for (var xIndex = 0; xIndex < bitmapImage.Width; xIndex++)
        {
            for (var yIndex = 0; yIndex < bitmapImage.Height; yIndex++)
            {
                var letterColumnIndex = (int)((float)xIndex / bitmapImage.Width * Columns);
                var letterRowIndex = (int)((float)yIndex / bitmapImage.Height * Rows);
                var letterIndex = letterRowIndex * Columns + letterColumnIndex;
                if (letterIndex >= letterBrightnessArray.Length)
                {
                    Log.Warning("Letter index calculation  failed", this);
                    continue;
                }

                var pixel = bitmapImage.GetPixel(xIndex, yIndex);
                letterBrightnessArray[letterIndex].Brightness += (pixel.R + pixel.G + pixel.B) * 1f;
            }
        }       

        // Filter valid characters
        filterCharCount = 0;
        var random = new Random(23);
        if (!string.IsNullOrEmpty(filterCharacters))
        {
            for (var index = 0; index < letterBrightnessArray.Length; index++)
            {
                letterBrightnessArray[index].Brightness += random.NextFloat(0, 0.6f);
                var c = letterBrightnessArray[index];
                var char2 = (char)c.Index;
                if (filterCharacters.IndexOf(char2) == -1)
                {
                    letterBrightnessArray[index].Brightness = 999999999;
                    letterBrightnessArray[index].Index = 0;
                    filterCharCount++;
                }
            }
        }
            
        // Sort by brightness
        letterBrightnessArray = letterBrightnessArray.OrderBy(b => b.Brightness).ToArray();

        return letterBrightnessArray;
    }

    private int filterCharCount = 0;
        
    private struct BrightnessEntry
    {
        public int Index;
        public float Brightness;
    }

    [Input(Guid = "DAB66E97-0B79-41CE-B6C7-9B01A123746E")]
    public readonly InputSlot<string> Filepath = new();

    [Input(Guid = "936F7D5E-8EAB-4A9D-ABEF-B941086B027D")]
    public readonly InputSlot<string> FilterCharacters = new();
}
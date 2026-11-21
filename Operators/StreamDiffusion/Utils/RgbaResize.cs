using System;
using System.Threading.Tasks;

namespace t3.streamdiffusion.Onnx;

/// <summary>
/// CPU resize helpers for RGBA8 frames (stretch, center-crop, pad),
/// extracted from the removed SD pipeline so the V2 pipeline keeps its resize behavior.
/// </summary>
internal static class RgbaResize
{
    internal static byte[] Apply(byte[] rgbaInput, int inputWidth, int inputHeight, int width, int height, int resizeMode)
    {
        if (resizeMode == 2)
        {
            return ResizeRgbaPad(rgbaInput, inputWidth, inputHeight, width, height);
        }

        if (resizeMode == 1)
        {
            return ResizeRgbaCenterCrop(rgbaInput, inputWidth, inputHeight, width, height);
        }

        return ResizeRgbaStretch(rgbaInput, inputWidth, inputHeight, width, height);
    }

    /// <summary>
    /// Stretch variant that writes into a caller-provided buffer: the MatchInputAspect path runs
    /// every frame, and allocating the multi-megabyte target each frame starves the GC.
    /// </summary>
    internal static void ApplyInto(byte[] target, byte[] rgbaInput, int inputWidth, int inputHeight, int width, int height)
    {
        float scaleX = (float)inputWidth / width;
        float scaleY = (float)inputHeight / height;

        Parallel.For(0, height, y =>
        {
            float srcYF = y * scaleY;
            int srcY0 = (int)Math.Floor(srcYF);
            int srcY1 = Math.Min(srcY0 + 1, inputHeight - 1);
            float fy = srcYF - srcY0;

            for (var x = 0; x < width; x++)
            {
                float srcXF = x * scaleX;
                int srcX0 = (int)Math.Floor(srcXF);
                int srcX1 = Math.Min(srcX0 + 1, inputWidth - 1);
                float fx = srcXF - srcX0;

                srcX0 = Math.Clamp(srcX0, 0, inputWidth - 1);
                srcY0 = Math.Clamp(srcY0, 0, inputHeight - 1);

                var dstIdx = (y * width + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    float v00 = rgbaInput[(srcY0 * inputWidth + srcX0) * 4 + c];
                    float v10 = rgbaInput[(srcY0 * inputWidth + srcX1) * 4 + c];
                    float v01 = rgbaInput[(srcY1 * inputWidth + srcX0) * 4 + c];
                    float v11 = rgbaInput[(srcY1 * inputWidth + srcX1) * 4 + c];

                    float top = v00 + fx * (v10 - v00);
                    float bottom = v01 + fx * (v11 - v01);
                    float val = top + fy * (bottom - top);

                    target[dstIdx + c] = (byte)Math.Clamp(val, 0, 255);
                }
            }
        });
    }

    private static byte[] ResizeRgbaStretch(byte[] rgbaInput, int inputWidth, int inputHeight, int width, int height)
    {
        var output = new byte[width * height * 4];
        float scaleX = (float)inputWidth / width;
        float scaleY = (float)inputHeight / height;

        System.Threading.Tasks.Parallel.For(0, height, y =>
        {
            float srcYF = y * scaleY;
            int srcY0 = (int)Math.Floor(srcYF);
            int srcY1 = Math.Min(srcY0 + 1, inputHeight - 1);
            float fy = srcYF - srcY0;

            for (var x = 0; x < width; x++)
            {
                float srcXF = x * scaleX;
                int srcX0 = (int)Math.Floor(srcXF);
                int srcX1 = Math.Min(srcX0 + 1, inputWidth - 1);
                float fx = srcXF - srcX0;

                srcX0 = Math.Clamp(srcX0, 0, inputWidth - 1);
                srcY0 = Math.Clamp(srcY0, 0, inputHeight - 1);

                var dstIdx = (y * width + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    float v00 = rgbaInput[(srcY0 * inputWidth + srcX0) * 4 + c];
                    float v10 = rgbaInput[(srcY0 * inputWidth + srcX1) * 4 + c];
                    float v01 = rgbaInput[(srcY1 * inputWidth + srcX0) * 4 + c];
                    float v11 = rgbaInput[(srcY1 * inputWidth + srcX1) * 4 + c];

                    float top = v00 + fx * (v10 - v00);
                    float bottom = v01 + fx * (v11 - v01);
                    float val = top + fy * (bottom - top);

                    output[dstIdx + c] = (byte)Math.Clamp(val, 0, 255);
                }
            }
        });

        return output;
    }

    private static byte[] ResizeRgbaCenterCrop(byte[] rgbaInput, int inputWidth, int inputHeight, int width, int height)
    {
        float targetAspect = (float)width / height;
        float sourceAspect = (float)inputWidth / inputHeight;

        int cropX, cropY, cropW, cropH;
        if (sourceAspect > targetAspect)
        {
            cropH = inputHeight;
            cropW = (int)(inputHeight * targetAspect);
            cropX = (inputWidth - cropW) / 2;
            cropY = 0;
        }
        else
        {
            cropW = inputWidth;
            cropH = (int)(inputWidth / targetAspect);
            cropX = 0;
            cropY = (inputHeight - cropH) / 2;
        }

        return ResizeRgbaBilinear(rgbaInput, inputWidth, inputHeight, cropX, cropY, cropW, cropH, width, height);
    }

    private static byte[] ResizeRgbaPad(byte[] rgbaInput, int inputWidth, int inputHeight, int width, int height)
    {
        var output = new byte[width * height * 4];
        for (int i = 0; i < output.Length; i += 4)
        {
            output[i] = 127;
            output[i + 1] = 127;
            output[i + 2] = 127;
            output[i + 3] = 255;
        }

        float scale = Math.Min((float)width / inputWidth, (float)height / inputHeight);
        int contentW = Math.Max(1, (int)(inputWidth * scale));
        int contentH = Math.Max(1, (int)(inputHeight * scale));
        int offsetX = (width - contentW) / 2;
        int offsetY = (height - contentH) / 2;

        var content = ResizeRgbaBilinear(rgbaInput, inputWidth, inputHeight, 0, 0, inputWidth, inputHeight, contentW, contentH);

        for (var y = 0; y < contentH; y++)
        {
            Array.Copy(content, (y * contentW) * 4, output, ((y + offsetY) * width + offsetX) * 4, contentW * 4);
        }

        return output;
    }

    private static byte[] ResizeRgbaBilinear(byte[] rgbaInput, int inputWidth, int inputHeight,
                                              int srcX, int srcY, int srcW, int srcH,
                                              int outWidth, int outHeight)
    {
        var output = new byte[outWidth * outHeight * 4];
        float scaleX = (float)srcW / outWidth;
        float scaleY = (float)srcH / outHeight;

        System.Threading.Tasks.Parallel.For(0, outHeight, y =>
        {
            float srcYF = srcY + y * scaleY;
            int srcY0 = (int)Math.Floor(srcYF);
            int srcY1 = Math.Min(srcY0 + 1, inputHeight - 1);
            float fy = srcYF - srcY0;
            srcY0 = Math.Clamp(srcY0, 0, inputHeight - 1);

            for (var x = 0; x < outWidth; x++)
            {
                float srcXF = srcX + x * scaleX;
                int srcX0 = (int)Math.Floor(srcXF);
                int srcX1 = Math.Min(srcX0 + 1, inputWidth - 1);
                float fx = srcXF - srcX0;
                srcX0 = Math.Clamp(srcX0, 0, inputWidth - 1);

                var dstIdx = (y * outWidth + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    float v00 = rgbaInput[(srcY0 * inputWidth + srcX0) * 4 + c];
                    float v10 = rgbaInput[(srcY0 * inputWidth + srcX1) * 4 + c];
                    float v01 = rgbaInput[(srcY1 * inputWidth + srcX0) * 4 + c];
                    float v11 = rgbaInput[(srcY1 * inputWidth + srcX1) * 4 + c];

                    float top = v00 + fx * (v10 - v00);
                    float bottom = v01 + fx * (v11 - v01);
                    float val = top + fy * (bottom - top);

                    output[dstIdx + c] = (byte)Math.Clamp(val, 0, 255);
                }
            }
        });

        return output;
    }
}

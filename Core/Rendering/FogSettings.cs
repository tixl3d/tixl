using System.Runtime.InteropServices;
using T3.Graphics.Compat;
using T3.Core.Resource;

namespace T3.Core.Rendering;

public static class FogSettings
{
    public static Buffer DefaultSettingsBuffer
    {
        get
        {
            if (_defaultSettingsBuffer == null)
            {
                ResourceManager.SetupConstBuffer(new FogParameters()
                                                     {
                                                         Bias = 2,
                                                         Distance = 10000,
                                                         Color = new Vector4(0, 0, 0, 1),
                                                     }, ref _defaultSettingsBuffer);
            }

            return _defaultSettingsBuffer;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = Stride)]
    public struct FogParameters
    {
        [FieldOffset(0)]
        public Vector4 Color;

        [FieldOffset(4 * 4)]
        public float Distance;

        [FieldOffset(5 * 4)]
        public float Bias;

        private const int Stride = 8 * 4;
    }

    private static Buffer _defaultSettingsBuffer = null;
}
using System.Runtime.InteropServices;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeCategory
{
    public readonly int index;
    public readonly float score;
    private readonly nint _categoryName;
    private readonly nint _displayName;

    public string? CategoryName => Marshal.PtrToStringAnsi(_categoryName);
    public string? DisplayName => Marshal.PtrToStringAnsi(_displayName);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeCategories
{
    private readonly nint _categories;
    public readonly uint categoriesCount;

    public ReadOnlySpan<NativeCategory> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<NativeCategory>((NativeCategory*)_categories, (int)categoriesCount);
        }
    }
}
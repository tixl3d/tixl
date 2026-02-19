using System.Runtime.InteropServices;
using Mediapipe.PInvoke;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeClassifications
{
    private readonly nint _categories;
    public readonly uint categoriesCount;
    public readonly int headIndex;
    private readonly nint _headName;

    public ReadOnlySpan<NativeCategory> Categories
    {
        get
        {
            unsafe
            {
                return new ReadOnlySpan<NativeCategory>((NativeCategory*)_categories, (int)categoriesCount);
            }
        }
    }

    public string? HeadName => Marshal.PtrToStringAnsi(_headName);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeClassificationResult
{
    private readonly nint _classifications;
    public readonly uint classificationsCount;
    public readonly long timestampMs;
    public readonly bool hasTimestampMs;

    public ReadOnlySpan<NativeClassifications> Classifications
    {
        get
        {
            unsafe
            {
                return new ReadOnlySpan<NativeClassifications>((NativeClassifications*)_classifications,
                    (int)classificationsCount);
            }
        }
    }

    public void Dispose()
    {
        UnsafeNativeMethods.mp_tasks_c_components_containers_CppCloseClassificationResult(this);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeClassificationResultArray
{
    private readonly nint _data;
    public readonly int size;

    public void Dispose()
    {
        UnsafeNativeMethods.mp_api_ClassificationResultArray__delete(this);
    }

    public ReadOnlySpan<NativeClassificationResult> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<NativeClassificationResult>((NativeClassificationResult*)_data, size);
        }
    }
}
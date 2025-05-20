using System.Runtime.CompilerServices;

namespace WpfD3d12.Utilities;

[InlineArray(3)]
public struct FixedArray3<T>
{
    private T _;
}

public static class FixedArrayExtensions
{
    public static void Dispose<T>(this FixedArray3<T> array) where T : IDisposable
    {
        foreach (var item in array)
        {
            item.Dispose();
        }
    }
}

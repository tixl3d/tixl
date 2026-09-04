using System;

namespace T3.Core.Utils;

internal class ArrayUtils
{
    /// <summary>
    /// Inserts a value in an array by replacing the array with a new array (length + 1) and copying the values
    /// Will create a lot of garbage if used frequently
    /// Created for Slots so they can have array-based inputs for performance reasons
    /// If index exceeds the length of the array, the value will be appended at the end
    /// </summary>
    internal static void InsertAtIndexOrEnd<T>(ref T[] array, T value, int index)
    {
        var originalLength = array.Length;
        var newLength = originalLength + 1;
        var newArray = new T[newLength];
        index = Math.Min(originalLength, index);
        for (var i = 0; i < index; i++)
        {
            newArray[i] = array[i];
        }

        newArray[index] = value;
        for (var i = index + 1; i < newLength; i++)
        {
            newArray[i] = array[i - 1];
        }

        array = newArray;
    }
    
    /// <summary>
    /// Removes an element in an array by replacing the array with a new array (length - 1) and copying the values
    /// Will create a lot of garbage if used frequently
    /// Created for Slots so they can have array-based inputs for performance reasons
    /// </summary>
    internal static void RemoveAt<T>(ref T[] array, int index)
    {
        var newArray = new T[array.Length - 1];
        for (var i = 0; i < index; i++)
        {
            newArray[i] = array[i];
        }

        for (var i = index; i < array.Length - 1; i++)
        {
            newArray[i] = array[i + 1];
        }

        array = newArray;
    }
}
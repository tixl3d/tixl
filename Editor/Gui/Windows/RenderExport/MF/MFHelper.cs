/*
Based on the LGPL license video helper at
https://github.com/RolandKoenig/SeeingSharp/blob/master/SeeingSharp.Multimedia_SHARED/Core/_Util/MFHelper.cs
*/

#region License information (SeeingSharp and all based games/applications)
/*
    Seeing# and all games/applications distributed together with it. 
	Exception are projects where it is noted otherwise.
    More info at 
     - https://github.com/RolandKoenig/SeeingSharp (sourcecode)
     - http://www.rolandk.de/wp (the authors homepage, german)
    Copyright (C) 2016 Roland König (RolandK)
    
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    
    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.
    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see http://www.gnu.org/licenses/.
*/
#endregion

using System.Text;

namespace T3.Editor.Gui.Windows.RenderExport.MF;

/// <summary>
/// A helper class containing utility methods used when working with Media Foundation.
/// Source: https://github.com/RolandKoenig/SeeingSharp/blob/master/SeeingSharp.Multimedia/Core/_Util/MFHelper.cs
/// </summary>
internal abstract class MfHelper
{
    /// <summary>
    /// Gets the Guid from the given type.
    /// </summary>
    /// <typeparam name="T">The type to get the guid from.</typeparam>
    internal static Guid GetGuidOf<T>()
    {
        return typeof(T).GUID;
    }

    /// <summary>
    /// Builds a Guid for a video subtype for the given format id (see MFRawFormats).
    /// </summary>
    /// <param name="rawFormatId">The raw format id.</param>
    internal static Guid BuildVideoSubtypeGuid(int rawFormatId)
    {
        return new Guid(
                        rawFormatId,
                        0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    }

    /// <summary>
    /// Helper function that builds the Guid for a video subtype using the given FOURCC value
    /// </summary>
    /// <param name="fourCcString">The FOURCC string to convert to a guid.</param>
    internal static Guid BuildVideoSubtypeGuid(string fourCcString)
    {
        return new Guid(
                        GetFourCcValue(fourCcString),
                        0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    }

    /// <summary>
    /// Gets the FourCC value for the given string.
    /// More infos about FourCC:
    ///  see: http://msdn.microsoft.com/en-us/library/windows/desktop/bb970509(v=vs.85).aspx,
    ///  see: http://msdn.microsoft.com/en-us/library/windows/desktop/aa370819(v=vs.85).aspx#creating_subtype_guids_from_fourccs_and_d3dformat_values,
    ///  see: http://de.wikipedia.org/wiki/FourCC
    /// </summary>
    /// <param name="fourCcString">The FourCC string to be converted into an unsigned integer value.</param>
    #if DESKTOP
        internal static uint GetFourCCValue(string fourCCString)
    #else
    private static int GetFourCcValue(string fourCcString)
        #endif
    {
        if (string.IsNullOrEmpty(fourCcString)) { throw new ArgumentNullException("subtype"); }
        if (fourCcString.Length > 4) { throw new ArgumentException("Given value too long!"); }

        // Build fcc value
        var asciiBytes = Encoding.UTF8.GetBytes(fourCcString);
        var fccValueBytes = new byte[4];
        for (var loop = 0; loop < 4; loop++)
        {
            if (asciiBytes.Length > loop) { fccValueBytes[loop] = asciiBytes[loop]; }
            else { fccValueBytes[loop] = 0x20; }
        }

        // Return guid
        #if DESKTOP
            return BitConverter.ToUInt32(fccValueBytes, 0);
        #else 
        return BitConverter.ToInt32(fccValueBytes, 0);
        #endif
    }

    /// <summary>
    /// Encodes the given values to a single long.
    /// Example usage: Size attribute.
    /// </summary>
    /// <param name="valueA">The first value.</param>
    /// <param name="valueB">The second value.</param>
    internal static long GetMfEncodedIntsByValues(int valueA, int valueB)
    {
        var valueXBytes = BitConverter.GetBytes(valueA);
        var valueYBytes = BitConverter.GetBytes(valueB);

        var resultBytes = new byte[8];
        if (BitConverter.IsLittleEndian)
        {
            resultBytes[0] = valueYBytes[0];
            resultBytes[1] = valueYBytes[1];
            resultBytes[2] = valueYBytes[2];
            resultBytes[3] = valueYBytes[3];
            resultBytes[4] = valueXBytes[0];
            resultBytes[5] = valueXBytes[1];
            resultBytes[6] = valueXBytes[2];
            resultBytes[7] = valueXBytes[3];
        }
        else
        {
            resultBytes[0] = valueXBytes[0];
            resultBytes[1] = valueXBytes[1];
            resultBytes[2] = valueXBytes[2];
            resultBytes[3] = valueXBytes[3];
            resultBytes[4] = valueYBytes[0];
            resultBytes[5] = valueYBytes[1];
            resultBytes[6] = valueYBytes[2];
            resultBytes[7] = valueYBytes[3];
        }

        return BitConverter.ToInt64(resultBytes, 0);
    }

    /// <summary>
    /// Decodes two integer values from the given long.
    /// Example usage: Size attribute.
    /// </summary>
    /// <param name="encodedInts">The long containing both encoded ints.</param>
    internal static Tuple<int, int> GetValuesByMfEncodedInts(long encodedInts)
    {
        var rawBytes = BitConverter.GetBytes(encodedInts);

        if (BitConverter.IsLittleEndian)
        {
            return Tuple.Create(
                                BitConverter.ToInt32(rawBytes, 4),
                                BitConverter.ToInt32(rawBytes, 0));
        }

        return Tuple.Create(
                            BitConverter.ToInt32(rawBytes, 0),
                            BitConverter.ToInt32(rawBytes, 4));
    }

    /// <summary>
    /// Converts the given duration value from media foundation to a TimeSpan structure.
    /// </summary>
    /// <param name="durationLong">The duration value.</param>
    internal static TimeSpan DurationLongToTimeSpan(long durationLong)
    {
        return TimeSpan.FromMilliseconds(durationLong / 10000);
    }

    /// <summary>
    /// Converts the given TimeSpan value to a duration value for media foundation
    /// </summary>
    /// <param name="timespan">The timespan.</param>
    internal static long TimeSpanToDurationLong(TimeSpan timespan)
    {
        return (long)(timespan.TotalMilliseconds * 10000);
    }
}

// namespace T3.Gui.Windows

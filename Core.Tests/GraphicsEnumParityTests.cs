using System.Reflection;
using Xunit;

namespace Core.Tests;

/// <summary>
/// The generated enums are copies of SharpDX's, kept identical on purpose: projects store the members by name,
/// shaders and C# structs assume the values, and the D3D11 backend casts between them. These tests fail if a
/// generated copy ever drifts from SharpDX — delete them together with SharpDX in v5.
/// </summary>
public class GraphicsEnumParityTests
{
    [Fact]
    public void EveryFacadeEnumMatchesSharpDx()
    {
        var mismatches = new List<string>();
        var checkedEnums = 0;

        // Every enum of the compatibility layer is a copy of a SharpDX one; of the shared layer only Format
        // is, the rest being the backend's own vocabulary.
        var generatedEnums = typeof(T3.Graphics.Compat.BindFlags).Assembly.GetTypes()
                                                                 .Where(type => type.IsEnum && type.IsPublic && !_compatOwnEnums.Contains(type.Name))
                                                                 .Append(typeof(T3.Graphics.Format));

        foreach (var facadeEnum in generatedEnums)
        {
            var original = FindSharpDxEnum(facadeEnum.Name);
            if (original == null)
            {
                mismatches.Add($"{facadeEnum.Name}: no SharpDX enum with this name");
                continue;
            }

            checkedEnums++;
            var facadeMembers = MembersOf(facadeEnum);
            var originalMembers = MembersOf(original);

            foreach (var (name, value) in originalMembers)
            {
                if (!facadeMembers.TryGetValue(name, out var facadeValue))
                    mismatches.Add($"{facadeEnum.Name}.{name} is missing from the facade");
                else if (facadeValue != value)
                    mismatches.Add($"{facadeEnum.Name}.{name} is {facadeValue} in the facade and {value} in SharpDX");
            }

            foreach (var name in facadeMembers.Keys.Where(name => !originalMembers.ContainsKey(name)))
            {
                mismatches.Add($"{facadeEnum.Name}.{name} does not exist in SharpDX");
            }

            if (facadeEnum.GetCustomAttribute<FlagsAttribute>() is null != (original.GetCustomAttribute<FlagsAttribute>() is null))
                mismatches.Add($"{facadeEnum.Name}: [Flags] differs");
        }

        Assert.Empty(mismatches);
        Assert.True(checkedEnums > 30, $"expected the generated enums, found {checkedEnums}");
    }

    private static Dictionary<string, long> MembersOf(Type enumType)
    {
        return Enum.GetNames(enumType).ToDictionary(name => name, name => Convert.ToInt64(Enum.Parse(enumType, name)));
    }

    private static Type? FindSharpDxEnum(string name)
    {
        return _sharpDxAssemblies.Select(assembly => assembly.GetTypes()
                                                             .FirstOrDefault(type => type.IsEnum && type.IsPublic && type.Name == name))
                                 .FirstOrDefault(type => type != null);
    }

    /// <summary>Enums the compatibility layer adds of its own; everything else must be a SharpDX copy.</summary>
    private static readonly HashSet<string> _compatOwnEnums = ["StateGroups"];

    private static readonly Assembly[] _sharpDxAssemblies =
        [
            typeof(SharpDX.Direct3D11.Device).Assembly,
            typeof(SharpDX.Direct3D.PrimitiveTopology).Assembly,
            typeof(SharpDX.DXGI.Format).Assembly,
        ];
}

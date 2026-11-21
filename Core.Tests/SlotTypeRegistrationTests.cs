using System;
using System.Runtime.CompilerServices;
using T3.Core.DataTypes;
using T3.Core.Model;
using T3.Core.Operator.Slots;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Slot types have to be registered with the package so the editor can build input definitions for
/// operators that use them; symbol type updates resolve the default value through
/// <see cref="InputValueCreators"/>. A missing entry is not a compile error — the operator simply
/// refuses to load and shows a modal error box — so the registrations the operator packages depend
/// on are pinned here.
/// </summary>
public class SlotTypeRegistrationTests
{
    // The registration runs in SymbolPackage's static constructor; typeof() alone would not trigger it.
    private static void EnsureRegistered() => RuntimeHelpers.RunClassConstructor(typeof(SymbolPackage).TypeHandle);

    [Theory]
    [InlineData(typeof(PhysicsBody))]
    [InlineData(typeof(PhysicsJoint))]
    [InlineData(typeof(PhysicsForce))]
    public void PhysicsDescriptorTypeIsRegistered(Type type)
    {
        EnsureRegistered();

        Assert.True(InputValueCreators.Entries.ContainsKey(type), $"{type.Name} has no default input value creator");
        Assert.True(TypeNameRegistry.Entries.ContainsKey(type), $"{type.Name} has no registered type name");
        Assert.NotNull(InputValueCreators.Entries[type]());
    }
}

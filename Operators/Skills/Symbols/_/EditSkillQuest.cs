using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using System.Runtime.InteropServices;
using T3.Core.DataTypes;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource;

namespace Skills._;

[Guid("6d84debe-0ebb-4d8e-a774-f00d5daa056c")]
internal sealed class EditSkillQuest : Instance<EditSkillQuest>
{
}

// ReSharper disable once UnusedType.Global
public sealed class ShareDefinition : IShareResources
{
    // ReSharper disable once EmptyConstructor
    public ShareDefinition(){}
    #pragma warning disable CA1822
    public bool ShouldShareResources => false;
    #pragma warning restore CA1822
}

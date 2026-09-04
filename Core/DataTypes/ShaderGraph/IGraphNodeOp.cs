#nullable enable
using System.Collections.Generic;

namespace T3.Core.DataTypes.ShaderGraph;

/**
 * Needs to implemented by ShaderGraph-Symbols to provide access to their <see cref="ShaderGraphNode"/> and code generation methods.
 */
public interface IGraphNodeOp
{
    ShaderGraphNode ShaderNode { get; }
    
    /**
     * Called only once for registering globals and definitions constant over the iteration of connected fields.
     */
    void AddDefinitions(CodeAssembleContext c)
    {
    }

    /**
     * Some nodes like SdfMaterial require custom handling and evaluation of connected fields.
     * If this method is overridden GetPre/Post ShaderCode calls are omitted.
     */
    bool TryBuildCustomCode(CodeAssembleContext c)
    {
        return false;
    }
    
    /**
     * Called before iterating connected fields. Should be used for manipulating the context
     * before evaluation (e.g. TransformSDF)
     */
    void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
    }

    /**
     * Called after iterating a connected field. Should be used for manipulating the result field (e.g. offset f) 
     */
    void GetPostShaderCode(CodeAssembleContext cac, int inputIndex)
    {
    }
    
    /**
     * Get resources
     */
    void AppendShaderResources(ref List<ShaderGraphNode.SrvBufferReference> list)
    {
    }    
}
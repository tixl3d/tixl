#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SharpDX.Direct3D11;
using T3.Core.Animation;
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using Log = T3.Core.Logging.Log;
// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable InvalidXmlDocComment

namespace T3.Core.DataTypes;

/**
 * Represents the attributes and parameters required to build a shader code from
 * nested instances. This basically replicates the instance graph structure so the graph
 * can be processed without actually updating instances. This is required for maintaining
 * the structure while allowing to completely cache the output.
 *
 * Storing the references to its operator Instance here is unfortunate.
 *
 * The process is somewhat complex:
 *
 * - [_GetFieldShaderAttributes] calls Update() on its connected graphNode(s).
 * - This then invokes .Update() on all connected graphNode-Ops which calls its...
 * - ShaderGraphNode.Update() -> Which...
 *   - updates its connected graph nodes
 *     - recursively calls Update() on their connected ops
 *   - checks if parameters are changed
 * - after completing this first pass of traversing the tree, we know if parameters, the code or the whole graph structure changed.
 * - [_GetFieldShaderAttributes]
 *      then calls...
 *          _graphNode.CollectShaderCode()
 *          _graphNode.CollectAllNodeParams()
 *      to recursively collect all required code and parameter fragments.
 */


public sealed class ShaderGraphNode
{
     
    
    #region node handling
    public ShaderGraphNode(Instance instance,
                           MultiInputSlot<ShaderGraphNode>? nodeMultiInputInput = null,
                           params InputSlot<ShaderGraphNode>?[] inputsSlots)
    {
        _instance = instance;
        InstanceForPrefixId = _instance;
        _nodeOp = instance as IGraphNodeOp;
        if (_nodeOp == null)
        {
            Log.Warning("ShaderOperator does not implement IGraphNodeOp interface", this);
        }
        
        _connectedNodeMultiInput = nodeMultiInputInput;
        _connectedNodeInputs = inputsSlots;
    }

    private int _lastUpdateFrame;
    

    public void Update(EvaluationContext context)
    {
        if (_lastUpdateFrame == Playback.FrameCount)
        {
            //Log.Debug($"Re-eval {OutputCounts++}", _instance);
            return;
        }


        _lastUpdateFrame = Playback.FrameCount;

        CollectedChanges = ChangedFlags.None;

        if (_hasCodeChanges)
        {
            CollectedChanges |= ChangedFlags.Code;
            _hasCodeChanges = false;
        }

        // Initialize prefix and collect parameters inputs.
        // (Deferred because symbolChildId is not set at construction time)
        if (string.IsNullOrEmpty(_prefix))
        {
            _prefix = BuildNodeId(InstanceForPrefixId);
            _shaderParameterInputs = ShaderParamHandling.CollectInputSlots(_instance, _prefix);
        }

        UpdateInputsNodes(context);

        var paramsNeedUpdate = IsAnyParameterDirty() || true;
        if (paramsNeedUpdate)
        {
            CollectedChanges |= ChangedFlags.Parameters;
            foreach (var paramInput in _shaderParameterInputs)
            {
                paramInput.Update(context);
            }
        }
        //Log.Debug($"Processed {_instance}  > {CollectedChanges.ToDetailedString()}", _instance);
    }

    public ChangedFlags CollectedChanges;

    /**
     * While collecting connected input nodes we computed a checksum to detect of the
     * graph structure has been changed and the shader code needs to be regenerated.
     */
    public int StructureHash;

    /// <summary>
    /// Recursive update all connected inputs nodes and collect their changes...
    /// </summary>
    /// <remarks>
    /// This invokes Update() on the connected notes which will then call this methode there. 
    /// </remarks>
    private void UpdateInputsNodes(EvaluationContext context)
    {
        _connectedNodeOps.Clear();

        if (_connectedNodeMultiInput != null)
        {
            _connectedNodeOps.AddRange(_connectedNodeMultiInput.GetCollectedTypedInputs(true));
        }

        _connectedNodeOps.AddRange(_connectedNodeInputs);
        
        // Align size of input nodes to connected fields
        if (_connectedNodeOps.Count != InputNodes.Count)
        {
            CollectedChanges |= ChangedFlags.Structural;
            InputNodes.Clear();
            InputNodes.AddRange(Enumerable.Repeat<ShaderGraphNode>(null!, _connectedNodeOps.Count).ToList());
        }

        // Update all connected nodes
        var lastStructureHash = StructureHash;
        StructureHash = _instance.SymbolChildId.GetHashCode();
        
        InputNodes.Clear();

        foreach(var connectedNodeOp in _connectedNodeOps)
        {
            var updatedNode = connectedNodeOp?.GetValue(context);

            if (updatedNode == null) 
                continue;
            
            StructureHash = StructureHash * 31 + updatedNode.StructureHash;
            CollectedChanges |= updatedNode.CollectedChanges;
            InputNodes.Add(updatedNode);
        }

        if (StructureHash != lastStructureHash)
        {
            CollectedChanges |= ChangedFlags.Structural;
        }

        // Remove all nulls from InputNodes
        InputNodes.RemoveAll(node => node == null);

        // Clear dirty flags for multi input to prevent ops stuck in dirty state 
        _connectedNodeMultiInput?.DirtyFlag.Clear();
    }        

    /** Flags the node to trigger a recompilation. */
    public void FlagCodeChanged()
    {
        _hasCodeChanges = true;
    }

    /** this will be cleared once the shader code is being collected */
    private bool _hasCodeChanges = true;
    #endregion

    private int _lastParamUpdateFrame = -1;

    public void CollectEmbeddedShaderCode(CodeAssembleContext cac)
    {
        if (_instance is not IGraphNodeOp nodeOp)
            return;

        var isRoot = cac.ContextIdStack.Count == 0;
        if (isRoot)
        {
            cac.ContextIdStack.Add("");
        }
        
        if (cac.TryMarkDefinitionsAdded(this))
        {
            nodeOp.AddDefinitions(cac);
        }

        if (nodeOp.TryBuildCustomCode(cac))
        {
            return;
        }
        
        if (InputNodes.Count == 0)
        {
            nodeOp.GetPreShaderCode(cac, 0);
            nodeOp.GetPostShaderCode(cac, 0);

            // We assume that a node without an input field is a distance function
            // We copy the local coordinates to the field result, so we can use
            // it later for things like UV mapping.
            //cac.AppendCall($"f{cac}.xyz = p.w < 0.5 ?  p{cac}.xyz : 1; // #{string.Join(' ', cac.ContextIdStack)} {this}");
            return;
        }

        // We need to start a new fieldContext
        var contextId = cac.ContextIdStack[^1];
        var requiresSubContext = InputNodes.Count > 1;

        if (requiresSubContext)
        {
            cac.SubContextCount++;
            cac.IndentCount++;
            cac.Calls.AppendLine();
            cac.AppendCall($"// {_instance.Symbol.Name}");

            var subContextIndex = cac.SubContextCount;
            for (var inputFieldIndex = 0; inputFieldIndex < InputNodes.Count; inputFieldIndex++)
            {
                cac.PushContext(subContextIndex, 
                                InputNodes.Count == 0 ? "" : ((char)('a' + inputFieldIndex)).ToString());

                nodeOp.GetPreShaderCode(cac, inputFieldIndex);

                InputNodes[inputFieldIndex]?.CollectEmbeddedShaderCode(cac);

                nodeOp.GetPostShaderCode(cac, inputFieldIndex);

                cac.PopContext();
                cac.Calls.AppendLine();
            }

            cac.IndentCount--;
        }
        else
        {
            nodeOp.GetPreShaderCode(cac, 0);

            if (InputNodes.Count == 1)
            {
                InputNodes[0]?.CollectEmbeddedShaderCode(cac);
            }

            nodeOp.GetPostShaderCode(cac, 0);
        }
    }
    

    #region parameters ----------------------
    private List<ShaderParamHandling.ShaderParamInput> _shaderParameterInputs = [];

    /**
     * Called by <see cref="GenerateShaderGraphCode"/> to collect the parameters and their current values.
     */
    public void CollectAllNodeParams(List<float> floatValues, List<ShaderParamHandling.ShaderCodeParameter> codeParams, int frameNumber, int graphId)
    {
        // Prevent double evaluation (note that _lastUpdateFrame will be updated after getting the code)
        // We need to check frame and the graphId, because different subsets of the graph might be used
        // by different CodeGenerator (e.g. one of RayMarching and one of particle forces).
        if (_lastParamUpdateFrame == frameNumber && _lastParamGraphId == graphId)
            return;

        _lastParamUpdateFrame = frameNumber;
        _lastParamGraphId = graphId;

        foreach (var inputNode in InputNodes)
        {
            inputNode?.CollectAllNodeParams(floatValues, codeParams, frameNumber, graphId);
        }

        foreach (var input in _shaderParameterInputs)
        {
            input.GetAsFloatValues(floatValues, codeParams);
        }

        // Update non input parameters
        foreach (var param in AdditionalParameters)
        {
            switch (param.Value)
            {
                case Matrix4x4 matrix4X4:
                    ShaderParamHandling.AddMatrixParameter(floatValues, codeParams, $"{_prefix}{param.Name}", matrix4X4);
                    break;
                case Vector2 v2:
                    ShaderParamHandling.AddVec2Parameter(floatValues, codeParams, $"{_prefix}{param.Name}", v2);
                    break;                
                case Vector3 v3:
                    ShaderParamHandling.AddVec3Parameter(floatValues, codeParams, $"{_prefix}{param.Name}", v3);
                    break;                
                case float f:
                    ShaderParamHandling.AddScalarParameter(floatValues, codeParams, $"{_prefix}{param.Name}", f);
                    break;
            }
        }
    }

    private bool IsAnyParameterDirty()
    {
        foreach (var input in _shaderParameterInputs)
        {
            if (input.Slot.IsDirty)
                return true;
        }

        return false;
    }

    // ReSharper disable once CollectionNeverUpdated.Global
    public List<Parameter> AdditionalParameters = [];
    
    #endregion
    
    
    /**
     * Called by <see cref="GenerateShaderGraphCode"/> to collect the parameters and their current values.
     */
    public void CollectResources(List<SrvBufferReference> buffers, int frameNumber, int graphId)
    {
        if (_nodeOp == null)
            return;
        
        // Prevent double evaluation (note that _lastUpdateFrame will be updated after getting the code)
        // We need to check frame and the graphId, because different subsets of the graph might be used
        // by different CodeGenerator (e.g. one of RayMarching and one of particle forces).
        // TODO: extract this combination into some kind of type.
        if (_lastBufferUpdateFrame == frameNumber && _lastBufferUpdateGraphId == graphId)
            return;
        
        // StructuredBuffer<Point> Points : register(t0);

        _lastBufferUpdateFrame = frameNumber;
        _lastBufferUpdateGraphId = graphId;

        foreach (var inputNode in InputNodes)
        {
            inputNode?.CollectResources(buffers, frameNumber, graphId);
        }

        _nodeOp.AppendShaderResources(ref buffers);
    }    
    
    /// <summary>
    /// A reference that can later be used to generate shader code with references like...
    /// <code>
    ///    StructuredBuffer&lt;Point&gt; Points : register(t123)
    ///    \-------------|--------------/ \-----|------/
    ///                  |                      | 
    ///             Definition               Will be added by GenerateShaderGraphCode
    /// </code>
    /// </summary>
    /// <param name="Definition">Should include type and unique name used in the shader.
    /// E.g. "StructuredBuffer&lt;Point&gt; Points_ABC123"</param> where ABC123 is a unique id of the node.
    /// <param name="Srv">The reference to the SRV connected to the shader node. It will be passed
    /// from GenerateShaderGraphCode to the [SetXYZStage] operators.</param>
    public record SrvBufferReference(string Definition, ShaderResourceView Srv);

    // Keep the input slot so we can detect and handle structural changes to the graph
    private readonly MultiInputSlot<ShaderGraphNode>? _connectedNodeMultiInput;
    private readonly InputSlot<ShaderGraphNode>?[] _connectedNodeInputs;
    private readonly List<Slot<ShaderGraphNode>?> _connectedNodeOps = [];
    public readonly List<ShaderGraphNode?> InputNodes = [];
    
    private int _lastParamGraphId;
    private int _lastBufferUpdateGraphId;
    private int _lastBufferUpdateFrame;
    
    private readonly Instance _instance;
    
    /// <summary>
    /// For nested operators like SdfToColor the SymbolChildId would no longer be unique.
    /// In these cases we can override this parameter.
    /// </summary>
    public Instance InstanceForPrefixId;
    private readonly IGraphNodeOp? _nodeOp;

    public sealed class Parameter(string shaderTypeName, string name, object value)
    {
        public string ShaderTypeName = shaderTypeName;
        internal readonly string Name = name;
        public object Value = value;
    }

    [Flags]
    public enum ChangedFlags
    {
        None = 0,
        Structural = 1 << 1,
        Code = 1 << 2,
        Parameters = 1 << 3,
        HasErrors = 1 << 4,
    }

    #region prefix
    private string? _prefix;

    private static string BuildNodeId(Instance instance)
    {
        return instance.GetType().Name + "_" + instance.SymbolChildId.ShortenGuid() + "_";
    }

    public override string? ToString() => _prefix;
    #endregion
}

/**
 * Marks a field as a parameter for a shader graph node so it can be automatically checked for changes
 */
public sealed class GraphParamAttribute : Attribute;
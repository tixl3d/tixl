using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpGLTF.Animations;
using T3.Core.Logging;
using T3.Core.Rendering.Material;
using T3.Core.Utils.Geometry;
using T3.Serialization;

namespace T3.Core.DataTypes;

/// <summary>
/// Combines buffers required for mesh rendering. Eventually this will be an abstraction from
/// format specific details, so it can be created from gltf, obj, fbx, etc.
/// </summary>
public class SceneSetup : IEditableInputType, IDisposable
{
    // TODO: Implement UI and serialize 
    // private Dictionary<string, string> MaterialAssignments;
    
    /// <summary>
    /// Recursive description of the loaded nodes...
    /// This can be translated into a list of draw dispatches.
    /// </summary>
    /// <remarks>
    /// This closely follows the gltf format structure but should be
    /// agnostic to other multi-node formats like obj.</remarks>
    public readonly List<SceneNode> RootNodes = new();
    
    public class SceneNode
    {
        public string Name;
        public readonly List<SceneNode> ChildNodes = new();
        public Matrix4x4 CombinedTransform;
        public Transform Transform;
        public string MeshName;
        public int MeshChunkIndex;
        public MeshBuffers MeshBuffers;
        public SceneMaterial Material;
        public BufferWithViews SkinWeights;
        public int SkeletonIndex = -1;
    }

    /// <summary>
    /// Joint hierarchy and bind data extracted from a skin.
    /// Joint transforms are in joint-local space; object space is derived by walking <see cref="ParentIndices"/>.
    /// </summary>
    public sealed class SceneSkeleton
    {
        public string[] JointNames;
        public int[] ParentIndices;   // -1 marks a root joint
        public Transform[] RestLocalTransforms;
        public Matrix4x4[] InverseBindMatrices;
    }

    public readonly List<SceneSkeleton> Skeletons = new();

    /// <summary>
    /// An animation clip extracted from the source file, targeting joints of the <see cref="Skeletons"/>.
    /// Curve samplers are created once at load with isolated memory; sampling them per frame is cheap.
    /// Times are in seconds.
    /// </summary>
    public sealed class SceneAnimClip
    {
        public string Name;
        public float Duration;
        public readonly List<JointAnimChannel> Channels = new();
    }

    public sealed class JointAnimChannel
    {
        public int SkeletonIndex;
        public int JointIndex;
        public ICurveSampler<Vector3> TranslationSampler;
        public ICurveSampler<Quaternion> RotationSampler;
        public ICurveSampler<Vector3> ScaleSampler;
    }

    public readonly List<SceneAnimClip> AnimationClips = new();
    
    /// <summary>
    /// Holds information required for building a T3 PbrMaterial.
    /// </summary>
    public class SceneMaterial
    {
        public string Name;
        public PbrMaterial.PbrParameters PbrParameters;
        public PbrMaterial PbrMaterial;
    }
    
    // FIXME: This should probably be moved to somewhere in core -> Rendering
    public struct Transform
    {
        public Vector3 Translation;
        public Quaternion Rotation;
        public Vector3 Scale;
        
        public Matrix4x4 ToTransform()
        {
            return GraphicsMath.CreateTransformationMatrix(
                                                           scalingCenter: Vector3.Zero,
                                                           scalingRotation: Quaternion.Identity,
                                                           scaling: Scale,
                                                           rotationCenter: Vector3.Zero,
                                                           rotation: Rotation,
                                                           translation: Translation);
        }
    }
    
    public List<NodeSetting> NodeSettings;
    
    /// <summary>
    /// Holds settings for a node inside the scene
    /// </summary>
    public class NodeSetting
    {
        public int NodeHashId;
        public string PbrMaterialId;
        public NodeVisibilities Visibility;
        
        public enum NodeVisibilities
        {
            Default = 0,
            Visible,
            HiddenSelf,
            HiddenBranch,
        }
    }
    
    #region dispatch preprocessing
    /// <summary>
    /// Flattens the node structure
    /// </summary>
    public void GenerateSceneDrawDispatches()
    {
        Dispatches.Clear();
        if (RootNodes == null || RootNodes.Count != 1)
        {
            Log.Warning("Gltf scene requires a single root node");
            return;
        }
        
        FlattenNodeTreeForDispatching(RootNodes[0]);
    }
    
    private void FlattenNodeTreeForDispatching(SceneNode node)
    {
        if (node.MeshBuffers != null)
        {
            var vertexCount = node.MeshBuffers.IndicesBuffer.Srv.Description.Buffer.ElementCount * 3;
            var newDispatch = new SceneDrawDispatch
                                  {
                                      MeshBuffers = node.MeshBuffers,
                                      VertexCount = vertexCount,
                                      VertexStartIndex = 0,
                                      Material = node.Material?.PbrMaterial,
                                      CombinedTransform = node.CombinedTransform,
                                      ChunkIndex = node.MeshChunkIndex,
                                      Scale = node.Transform.Scale,
                                      SkinWeights = node.SkinWeights,
                                      SkeletonIndex = node.SkeletonIndex,
                                  };
            
            Dispatches.Add(newDispatch);
        }
        
        foreach (var childNode in node.ChildNodes)
        {
            FlattenNodeTreeForDispatching(childNode);
        }
    }
    
    /// <summary>
    /// Flattened structure used by _DispatchSceneDraws to dispatch draw commands.
    /// </summary>
    public class SceneDrawDispatch
    {
        public MeshBuffers MeshBuffers;
        public int VertexCount;
        public int VertexStartIndex;
        public PbrMaterial Material;
        public Matrix4x4 CombinedTransform;
        public int ChunkIndex;
        public Vector3 Scale;
        public BufferWithViews SkinWeights;
        public int SkeletonIndex = -1;
    }
    
    public readonly List<SceneDrawDispatch> Dispatches = new();
    #endregion
    
    #region serialization
    public void Write(JsonTextWriter writer)
    {
        writer.WritePropertyName(nameof(SceneSetup));
        writer.WriteStartObject();
        
        writer.WritePropertyName("NodeSettings");
        writer.WriteStartArray();
        
        if (NodeSettings != null)
        {
            lock (NodeSettings)
            {
                foreach (var setting in NodeSettings)
                {
                    writer.WriteStartObject();
                    writer.WriteValue(nameof(setting.NodeHashId), setting.NodeHashId);
                    
                    if (setting.Visibility != NodeSetting.NodeVisibilities.Default)
                        writer.WriteObject(nameof(setting.Visibility), setting.Visibility.ToString());
                    writer.WriteEndObject();
                }
            }
        }
        
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
    
    public void Read(JToken inputToken)
    {
        if (NodeSettings == null)
        {
            NodeSettings = new List<NodeSetting>();
        }
        else
        {
            NodeSettings.Clear();
        }
        
        var nodeSettingsToken = inputToken[nameof(SceneSetup)];
        if (nodeSettingsToken == null)
            return;
        
        try
        {
            var jArray = (JArray)nodeSettingsToken[nameof(NodeSettings)];
            if (jArray == null)
                return;
            
            foreach (var keyEntry in jArray)
            {
                NodeSettings.Add(new NodeSetting()
                                     {
                                         NodeHashId = (keyEntry[nameof(NodeSetting.NodeHashId)] ?? -1).Value<int>(),
                                         Visibility = (inputToken[nameof(NodeSetting.Visibility)] == null)
                                                          ? NodeSetting.NodeVisibilities.Default
                                                          : (NodeSetting.NodeVisibilities)Enum.Parse(typeof(NodeSetting.NodeVisibilities),
                                                                                                     inputToken[nameof(NodeSetting.Visibility)].ToString()),
                                     });
            }
        }
        catch (Exception e)
        {
            Log.Warning("Can't read scene setting property " + e);
        }
    }
    
    public object Clone() => TypedClone();
    
    public SceneSetup TypedClone()
    {
        return new SceneSetup()
                   {
                       NodeSettings = NodeSettings == null ? new List<NodeSetting>() : new List<NodeSetting>(NodeSettings),
                   };
    }
    #endregion
    
    public void Dispose() => Dispose(true);
    
    public void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        // Dispatches can share MeshBuffers and PbrMaterial instances; their Dispose implementations are idempotent.
        foreach (var dispatch in Dispatches)
        {
            dispatch.MeshBuffers?.Dispose();

            if (dispatch.Material != null)
            {
                dispatch.Material.Dispose();
                dispatch.Material = null;
            }
        }

        Dispatches.Clear();
    }
}
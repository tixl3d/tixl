using T3.Core.Rendering.Material;
using T3.Core.Utils;

namespace Lib.mesh.draw;

[Guid("c78d114e-b404-491f-93a8-024a382586bd")]
internal sealed class CustomDrawMesh :Instance<CustomDrawMesh>,ICustomDropdownHolder,ICompoundWithUpdate
{
    [Output(Guid = "eed49fbd-9a7c-4aaa-8d42-abb682f7ebf8")]
    public readonly Slot<Command> Output = new();

        [Output(Guid = "8a92e09d-8e50-4b9d-9981-cc8d7a4ebb95")]
        public readonly Slot<string> OutShaderCode = new Slot<string>();
        
        
    public CustomDrawMesh()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (context.Materials != null)
        {
            _pbrMaterials.Clear();
            _pbrMaterials.AddRange(context.Materials);
        }

        var previousMaterial = context.PbrMaterial;
            
        var materialId = UseMaterialId.GetValue(context);
        if (!string.IsNullOrEmpty(materialId))
        {
            foreach(var m in context.Materials)
            {
                if (m.Name != materialId)
                    continue;
                    
                context.PbrMaterial = m;
                break;

            }
        }
            
        // Inner update
        Output.ConnectedUpdate(context);
        context.PbrMaterial = previousMaterial;
    }

    #region custom material dropdown
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return inputId != UseMaterialId.Input.Id 
                   ? "Undefined input" 
                   : UseMaterialId.TypedInputValue.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        yield return "Default";
            
        if(_pbrMaterials == null)
            yield break;

        foreach (var m in _pbrMaterials)
        {
            yield return string.IsNullOrEmpty(m.Name) ? "undefined" : m.Name;
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        if (inputId != UseMaterialId.Input.Id)
            return;
            
        UseMaterialId.SetTypedInputValue(selected);
    }

    private readonly List<PbrMaterial> _pbrMaterials = new(8);
    #endregion
        
    private enum ShadingModes
    {
        Default = 0,
        Flat = 1,
    }

        [Input(Guid = "d3c406b7-b17e-4c95-82b4-7249e6335675")]
        public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Mesh = new InputSlot<T3.Core.DataTypes.MeshBuffers>();

        [Input(Guid = "3fb1af41-eed4-4c38-9a6a-7efe494612d0")]
        public readonly InputSlot<System.Numerics.Vector3> Offset = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "e7dc01ac-cb1d-409a-b8f3-ad22b1255771")]
        public readonly InputSlot<float> A = new InputSlot<float>();

        [Input(Guid = "05feb9d0-3bfc-4c96-b79e-b884132813e9")]
        public readonly InputSlot<float> B = new InputSlot<float>();

        [Input(Guid = "dc032839-eca3-48d0-be68-99b06da28167")]
        public readonly InputSlot<float> C = new InputSlot<float>();

        [Input(Guid = "ada34207-e581-4f35-9ef5-8f6122e0bde5")]
        public readonly InputSlot<float> D = new InputSlot<float>();

        [Input(Guid = "4494c16e-1ff2-42db-9204-ac389451088f")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "eff4073a-619b-4b4c-8612-758c54dc12bf")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "7a6ca5a8-17a6-4a8b-9610-ed4c9e292cfe")]
        public readonly InputSlot<string> ShaderCode = new InputSlot<string>();

        [Input(Guid = "38404e1d-666e-42c3-9ece-8be0d69ff505")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> FragmentField = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "f5754aaf-f276-474f-b36f-4b608bdbfe88")]
        public readonly InputSlot<string> AdditionalDefines = new InputSlot<string>();

        [Input(Guid = "ede123e4-14cd-49f7-b7ce-c538ec381415")]
        public readonly InputSlot<string> ShaderTemplatePath = new InputSlot<string>();

        [Input(Guid = "93200afe-7b59-45a9-8662-4cfa477e011c")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "f9f36aec-ebbf-4221-a585-cdc6ea7f7c50")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "07f34663-a753-4186-ac02-ef937e482f6f")]
        public readonly InputSlot<float> AlphaCutOff = new InputSlot<float>();

        [Input(Guid = "0ad26992-fc45-4381-a45d-40eb43470646", MappedType = typeof(SharedEnums.BlendModes))]
        public readonly InputSlot<int> BlendMode = new InputSlot<int>();

        [Input(Guid = "e877da85-3f9b-4425-b389-1958e3daaf31", MappedType = typeof(FillMode))]
        public readonly InputSlot<int> FillMode = new InputSlot<int>();

        [Input(Guid = "a21fd13b-da4b-43f5-8697-85a2b5b9a1ff")]
        public readonly InputSlot<SharpDX.Direct3D11.CullMode> Culling = new InputSlot<SharpDX.Direct3D11.CullMode>();

        [Input(Guid = "43344f8e-5d14-438a-8f87-3922ce954cdb", MappedType = typeof(ShadingModes))]
        public readonly InputSlot<int> Shading = new InputSlot<int>();

        [Input(Guid = "1aa04960-2b81-49dd-8389-31176df558c8")]
        public readonly InputSlot<float> SpecularAA = new InputSlot<float>();

        [Input(Guid = "7f731ea3-c246-4e9c-95b3-b30260887161")]
        public readonly InputSlot<bool> EnableZTest = new InputSlot<bool>();

        [Input(Guid = "92736363-ef71-4828-bf2b-4e9f407b1155")]
        public readonly InputSlot<bool> EnableZWrite = new InputSlot<bool>();

        [Input(Guid = "a0fc26e4-54af-4f65-a3ff-0a87a2389e8d")]
        public readonly InputSlot<SharpDX.Direct3D11.Filter> Filter = new InputSlot<SharpDX.Direct3D11.Filter>();

        [Input(Guid = "65f2a12d-f6b4-4562-a11c-2b14e2ab5e34")]
        public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> WrapMode = new InputSlot<SharpDX.Direct3D11.TextureAddressMode>();

        [Input(Guid = "d5b24c95-4252-4b0b-ba37-0238f1957c4d")]
        public readonly InputSlot<string> UseMaterialId = new InputSlot<string>();

        [Input(Guid = "3e7b6c69-f4d1-4f96-a693-a2162fe29a77")]
        public readonly InputSlot<string> ShaderDefines = new InputSlot<string>();

        [Input(Guid = "70f947b7-6e25-417a-94ce-cb4522f35797")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();
}
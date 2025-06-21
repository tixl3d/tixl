
using T3.Core.DataTypes;
using T3.Core.DataTypes.DataSet;
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Rendering.Material;
using T3.Editor.Gui.InputUi.CombinedInputs;
using T3.Editor.Gui.InputUi.ListInputs;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using T3.Editor.Gui.InputUi.SingleControl;
using T3.Editor.Gui.InputUi.VectorInputs;
using T3.Editor.Gui.OutputUi;
using T3.Editor.UiModel.InputsAndTypes;
using Int3 = T3.Core.DataTypes.Vector.Int3;
using Point = T3.Core.DataTypes.Point;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Texture3D = T3.Core.DataTypes.Texture3D;
using PixelShader = T3.Core.DataTypes.PixelShader;
using VertexShader = T3.Core.DataTypes.VertexShader;
using ComputeShader = T3.Core.DataTypes.ComputeShader;
using GeometryShader = T3.Core.DataTypes.GeometryShader;

namespace T3.Editor.UiModel;

internal static class UiRegistration
{
    public static void RegisterUiTypes()
    {
        
        // set colors of types
        TypeUiRegistry.SetProperties(typeof(string), UiProperties.String);
        TypeUiRegistry.SetProperties(typeof(List<string>), UiProperties.String);
        TypeUiRegistry.SetProperties(typeof(Command), UiProperties.Command);

        RegisterTypesToProperty(UiProperties.Shader,
                                typeof(ComputeShader),
                                typeof(GeometryShader),
                                typeof(PixelShader),
                                typeof(VertexShader),
                                typeof(Texture3D),

                                // sharpDX types
                                typeof(SharpDX.Direct3D11.BindFlags),
                                typeof(SharpDX.Direct3D11.BlendOperation),
                                typeof(SharpDX.Direct3D11.BlendOption),
                                typeof(SharpDX.Direct3D11.BlendState),
                                typeof(SharpDX.Direct3D11.Buffer),
                                typeof(SharpDX.Direct3D11.ColorWriteMaskFlags),
                                typeof(SharpDX.Direct3D11.Comparison),
                                typeof(SharpDX.Direct3D11.CpuAccessFlags),
                                typeof(SharpDX.Direct3D11.CullMode),
                                typeof(SharpDX.Direct3D11.DepthStencilState),
                                typeof(SharpDX.Direct3D11.DepthStencilView),
                                typeof(SharpDX.Direct3D11.FillMode),
                                typeof(SharpDX.Direct3D11.Filter),
                                typeof(SharpDX.Direct3D11.InputLayout),
                                typeof(SharpDX.Direct3D11.RasterizerState),
                                typeof(SharpDX.Direct3D11.ResourceOptionFlags),
                                typeof(SharpDX.Direct3D11.ResourceUsage),
                                typeof(SharpDX.Direct3D11.SamplerState),
                                typeof(SharpDX.Direct3D11.TextureAddressMode),
                                typeof(SharpDX.Direct3D11.UnorderedAccessViewBufferFlags),
                                typeof(SharpDX.DXGI.Format),
                                typeof(SharpDX.Mathematics.Interop.RawViewportF),
                                typeof(SharpDX.Mathematics.Interop.RawRectangle)
                               );

        RegisterTypesToProperty(UiProperties.Texture, 
                                typeof(Texture2D),
                                typeof(RenderTargetReference),

                                // sharpDX types
                                typeof(SharpDX.Direct3D11.DepthStencilView),
                                typeof(SharpDX.Direct3D11.RenderTargetBlendDescription),
                                typeof(SharpDX.Direct3D11.RenderTargetView),
                                typeof(SharpDX.Direct3D11.ShaderResourceView),
                                typeof(SharpDX.Direct3D11.UnorderedAccessView)
                               );
        
        RegisterTypesToProperty(UiProperties.GpuData,
                                typeof(DataSet), 
                                typeof(SceneSetup), 
                                typeof(PbrMaterial), 
                                typeof(Texture3dWithViews), 
                                typeof(MeshBuffers), 
                                typeof(ParticleSystem), 
                                typeof(BufferWithViews),
                                typeof(GizmoVisibility),
                                // sharpDX types
                                typeof(SharpDX.Direct3D.PrimitiveTopology));
        
        RegisterTypesToProperty(UiProperties.ShaderGraph,
                                typeof(ShaderGraphNode));
        
                                // set colors of input and output UIs
        
                                // system types
                                RegisterIOType(typeof(float), () => new FloatInputUi(), () => new FloatOutputUi());
        RegisterIOType(typeof(int), () => new IntInputUi());
        RegisterIOType(typeof(bool), () => new BoolInputUi(), () => new BoolOutputUi());
        RegisterIOType(typeof(double), () => new FloatInputUi(), () => new FloatOutputUi());

        RegisterIOType(typeof(Vector2), () => new Vector2InputUi(), () => new VectorOutputUi<Vector2>());
        RegisterIOType(typeof(Vector3), () => new Vector3InputUi(), () => new VectorOutputUi<Vector3>());
        RegisterIOType(typeof(Vector4), () => new Vector4InputUi(), () => new VectorOutputUi<Vector4>());
        RegisterIOType(typeof(Quaternion), () => new Vector4InputUi(), () => new VectorOutputUi<Quaternion>());

        RegisterIOType(typeof(List<float>), () => new FloatListInputUi(), () => new FloatListOutputUi());
        RegisterIOType(typeof(List<int>), () => new IntListInputUi(), () => new IntListOutputUi());
        RegisterIOType(typeof(string), () => new StringInputUi(), () => new StringOutputUi());
        RegisterIOType(typeof(List<string>), () => new StringListInputUi(), () => new StringListOutputUi());


        // t3 core types
        RegisterIOType(typeof(Curve), () => new CurveInputUi());
        RegisterIOType(typeof(Gradient), () => new GradientInputUi());
        RegisterIOType(typeof(Point[]), null, () => new PointArrayOutputUi());
        RegisterIOType(typeof(StructuredList), () => new StructuredListInputUi(), () => new StructuredListOutputUi());
        RegisterIOType(typeof(Int3), () => new Int3InputUi());
        RegisterIOType(typeof(Int2),() => new Int2InputUi());
        RegisterIOType(typeof(Dict<float>), () => new FloatDictInputUi(), () => new FloatDictOutputUi());
        

        // Rendering
        RegisterIOType(typeof(DataSet), null, () => new DataSetOutputUi());
        RegisterIOType(typeof(Texture2D), null, () => new Texture2dOutputUi());
        RegisterIOType(typeof(Texture3dWithViews), null, () => new Texture3dOutputUi());
        RegisterIOType(typeof(Command), null, () => new CommandOutputUi());
        RegisterIOType(typeof(BufferWithViews), null, () => new BufferWithViewsOutputUi());
        RegisterIOType(typeof(SceneSetup), () => new SceneSetupInputUi(), () => new SceneSetupOutputUi());

        return;

        static void RegisterTypesToProperty(UiProperties properties, params Type[] types)
        {
            foreach(var type in types)
                TypeUiRegistry.SetProperties(type, properties);
        }

        static void RegisterIOType(Type type, Func<IInputUi> inputUi, Func<IOutputUi> outputUi = null)
        {
            InputUiFactory.Instance.AddFactory(type, inputUi);
            OutputUiFactory.Instance.AddFactory(type, outputUi);
        }
    }
}
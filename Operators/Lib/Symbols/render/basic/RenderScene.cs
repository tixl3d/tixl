namespace Lib.render.basic;

[Guid("b60072db-a16e-45f5-9425-bb80f5f9122c")]
internal sealed class RenderScene : Instance<RenderScene>
{
    [Output(Guid = "45777ce5-6408-4302-8a5a-a73b403b07c1")]
    public readonly Slot<Texture2D> Output = new();

    [Output(Guid = "cb997fe3-ce39-4783-8625-51705c151217")]
    public readonly Slot<Texture2D> DepthBuffer = new();

    [Output(Guid = "82974b1e-aaca-40ee-b584-42e05c003ff9")]
    public readonly Slot<Texture2D> NormalBuffer = new();

    [Output(Guid = "9c72ee0a-5538-4f9e-80d2-1530666bddde")]
    public readonly Slot<Texture2D> VelocityBuffer = new();

    [Input(Guid = "1b69a1ec-5f03-4182-b002-21c6387a68cf")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "5b838d3a-b693-4524-b8cd-b60530173d46")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "3db94583-6694-47e5-b775-23eb351e9f11")]
    public readonly InputSlot<Vector4> ClearColor = new();

    [Input(Guid = "679fa6c5-7b78-4846-b462-4de672cd63b0")]
    public readonly InputSlot<int> Multisampling = new();

        [Input(Guid = "e67e7052-1f2f-4ad0-909f-f97190128c34")]
        public readonly InputSlot<int> Environment = new InputSlot<int>();

    [Input(Guid = "bcb6064d-e137-4cd7-b743-cc84058a0d23")]
    public readonly InputSlot<bool> WithMaterialBuffer = new();
}

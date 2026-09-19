namespace Examples.user.anosci;

[Guid("c3090c65-194a-4f95-9b70-d003f54103f7")]
internal sealed class SnapToMultiple : Instance<SnapToMultiple>
{
    [Output(Guid = "87a311ce-238c-472c-b43e-e4ed5268bbc5")]
    public readonly Slot<int> Result = new();

    public SnapToMultiple()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var mod = Mod.GetValue(context);

        for(int nextLowestMod=mod;nextLowestMod>0;nextLowestMod--)
        {
            if(v%nextLowestMod == 0)
            {
                Result.Value = nextLowestMod;
                return;
            }
				
        }
			
    }
        
    [Input(Guid = "3bf35135-8fb1-46d3-95ea-008eded67060")]
    public readonly InputSlot<int> Value = new();

    [Input(Guid = "e122bf1d-8455-43f6-867f-4f43f3d6533c")]
    public readonly InputSlot<int> Mod = new();
}
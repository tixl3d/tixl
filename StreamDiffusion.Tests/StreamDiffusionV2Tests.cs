using t3.streamdiffusion.V2;
using Xunit;

namespace StreamDiffusion.Tests;

public class StreamDiffusionV2Tests
{
    [Theory]
    [InlineData(700, 0.70023f)]
    [InlineData(500, 0.49972f)]
    [InlineData(400, 0.40026f)]
    [InlineData(200, 0.19835f)]
    [InlineData(0, 0.00794f)]
    public void FlowMatchSchedule_MatchesReferenceSigmas(int timestep, float expectedSigma)
    {
        var schedule = new FlowMatchSchedule();
        Assert.Equal(expectedSigma, schedule.SigmaAt(timestep), 4);
    }

    [Fact]
    public void FlowMatchSchedule_AddNoise_BlendsBySigma()
    {
        var schedule = new FlowMatchSchedule();
        var sigma = schedule.SigmaAt(500);
        var result = new float[1];

        schedule.AddNoise(new[] { 2f }, new[] { -1f }, 500, result);

        Assert.Equal((1f - sigma) * 2f - sigma, result[0], 5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(6, 3)]
    [InlineData(7, 4)]
    [InlineData(8, 5)]
    [InlineData(9, 3)]
    public void KvSlotPolicy_KeepsSinkFramesAndRotatesTheRest(int frame, int expectedSlot)
    {
        Assert.Equal(expectedSlot, KvSlotPolicy.SlotForFrame(frame, cacheFrames: 6, sinkFrames: 3));
    }

    [Fact]
    public void KvCacheState_MaskHidesEmptySlotsAndTheOverwrittenOne()
    {
        var state = new KvCacheState(4);
        state.MarkWritten(0, 0);
        state.MarkWritten(1, 1);
        var mask = new float[8];

        state.FillMask(mask, tokensPerFrame: 2, overwrittenSlot: 1);

        Assert.Equal(new[] { 0f, 0f, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity }, mask);
        Assert.Equal(2, state.FindEmptySlot());
    }

    [Fact]
    public void MotionNoiseController_StillSceneKeepsInitialLevel()
    {
        var (noiseScale, timestep) = MotionNoiseController.Update(0f, 0.8f, 0.8f);

        Assert.Equal(0.8f, noiseScale, 5);
        Assert.Equal(700, timestep);
    }

    [Fact]
    public void MotionNoiseController_FastMotionLowersNoise()
    {
        var (noiseScale, _) = MotionNoiseController.Update(0.5f, 0.8f, 0.8f);

        Assert.True(noiseScale < 0.8f);
    }

    [Theory]
    [InlineData("A cat, wearing a hat.", new[] { 320, 6283, 275, 101722, 289, 1275, 274, 1 })]
    [InlineData("a   watercolor painting of a fox  in a forest", new[] { 289, 3231, 8045, 91067, 329, 289, 273, 56209, 301, 289, 58545, 1 })]
    [InlineData("Ünïcode 日本語! 😀 test", new[] { 104346, 3252, 10099, 91001, 332, 30618, 1808, 1 })]
    [InlineData("", new[] { 1 })]
    public void Umt5Tokenizer_MatchesReferenceIds(string text, int[] expected)
    {
        var path = Environment.GetEnvironmentVariable("TIXL_SDV2_TOKENIZER")
                   ?? @"D:\DEVDEVDEV\models\streamdiffusionv2\wan\google\umt5-xxl\tokenizer.json";
        if (!File.Exists(path))
            return; // tokenizer data is a multi-MB download, not part of the repo

        var tokenizer = Umt5Tokenizer.Load(path);

        Assert.Equal(expected, tokenizer.Encode(text, 512));
    }
}

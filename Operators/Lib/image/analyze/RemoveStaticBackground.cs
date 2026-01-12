namespace Lib.image.analyze;

[Guid("f1333224-e1e3-44b4-a84b-e9b13cacf320")]
internal sealed class RemoveStaticBackground : Instance<RemoveStaticBackground>
{
    [Output(Guid = "1f83c6cd-10b8-4293-8757-10cc75d2270a")]
    public readonly Slot<Texture2D> Output = new();

        [Input(Guid = "79e41a79-79a2-4ccb-97c5-f80b8d03c58f")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture2d = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "5cbdeb7e-1f9d-4905-8037-4cde42235898")]
        public readonly InputSlot<bool> Update = new InputSlot<bool>();

        [Input(Guid = "9b666162-80e0-43f5-8a5d-083729da0ff2", MappedType = typeof(OutputModes))]
        public readonly InputSlot<int> OutputMode = new InputSlot<int>();

        [Input(Guid = "76e07b3f-c411-4e23-be98-e15f994c81d8")]
        public readonly InputSlot<float> MeanRate = new InputSlot<float>();

        [Input(Guid = "6fbe1ea6-2195-442e-a82e-2cecd27dbc11")]
        public readonly InputSlot<float> SpreadUpRate = new InputSlot<float>();

        [Input(Guid = "8b792db9-797b-426b-9602-4e508e599ce0")]
        public readonly InputSlot<float> SpreadDownRate = new InputSlot<float>();

        [Input(Guid = "20c56912-81dc-44f2-b44a-91febe627f75")]
        public readonly InputSlot<float> MinSpread = new InputSlot<float>();

        [Input(Guid = "6c2093b8-eec1-4d5d-b8de-5c6e8cc1ac86")]
        public readonly InputSlot<float> BackgroundGateLo = new InputSlot<float>();

        [Input(Guid = "5edbbfe4-99d8-4722-a922-68df4a669d54")]
        public readonly InputSlot<float> BackgroundGateHi = new InputSlot<float>();

        [Input(Guid = "7d43f6f7-a2e5-4203-891e-0dcc7b7a0be5")]
        public readonly InputSlot<float> ZScale = new InputSlot<float>();

        [Input(Guid = "b27194af-c253-49e3-a215-1004d004ce3a")]
        public readonly InputSlot<float> BrightSuppression = new InputSlot<float>();

        [Input(Guid = "a54b96a9-8579-4b70-b82c-cbea74aa3e60")]
        public readonly InputSlot<bool> EnableChroma = new InputSlot<bool>();

        [Input(Guid = "007aec66-e8f2-493e-8035-cea3adfc1a2e")]
        public readonly InputSlot<float> ChromaWeight = new InputSlot<float>();

        [Input(Guid = "852434c4-56e3-4e73-9a39-3d5e61bb8570")]
        public readonly InputSlot<float> VoteThreshold = new InputSlot<float>();

        [Input(Guid = "2d542736-c84a-4aa5-bce9-326aa0591885")]
        public readonly InputSlot<System.Numerics.Vector2> DensityRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "74e69989-160e-4db8-8b31-940ebbb4c506")]
        public readonly InputSlot<float> TemporalStability = new InputSlot<float>();

        [Input(Guid = "e714a3a6-65bd-4cdf-b3b8-bdf8b24f872a")]
        public readonly InputSlot<float> KeepOriginal = new InputSlot<float>();

        [Input(Guid = "b26fb554-2d75-48cd-90cf-f1187ad18930")]
        public readonly MultiInputSlot<bool> IsTraining = new MultiInputSlot<bool>();

        [Input(Guid = "118496fa-914e-4e50-bb7d-fa496f77eeab")]
        public readonly InputSlot<float> TrainingMeanRate = new InputSlot<float>();

        [Input(Guid = "455ecd34-8228-4f70-b59a-fe6493234856")]
        public readonly InputSlot<float> TrainingSpreadUpRate = new InputSlot<float>();

        [Input(Guid = "57c8515d-f281-4644-a945-5590ea108542")]
        public readonly InputSlot<float> TrainingSpreadDownRate = new InputSlot<float>();

        [Input(Guid = "96b4debf-237c-465b-ae73-3c5e7036f43a")]
        public readonly InputSlot<float> TrainingBrightSuppression = new InputSlot<float>();
        
        private enum OutputModes {
            OutputForeground = 0,   // Camera RGB, alpha = refined foreground mask
            OutputMask       = 1,   // Grayscale mask in RGB, alpha = 1

            DebugMean        = 2,
            DebugSpread      = 3,
            DebugZ           = 4,
            DebugRawMask     = 5,
            DebugRefined     = 6,
            DebugBrightDark  = 7,
            DebugRange       = 8,
        }

}
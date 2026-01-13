using Mediapipe.Framework.Tool;

namespace Mediapipe.Tasks.Core;

internal class TaskInfo<T>(string taskGraph, List<string> inputStreams, List<string> outputStreams, T taskOptions)
    where T : ITaskOptions
{
    public string TaskGraph { get; } = taskGraph;
    public List<string> InputStreams { get; } = inputStreams;
    public List<string> OutputStreams { get; } = outputStreams;
    public T TaskOptions { get; } = taskOptions;

    public CalculatorGraphConfig GenerateGraphConfig(bool enableFlowLimiting = false)
    {
        if (string.IsNullOrEmpty(TaskGraph) || TaskOptions == null)
            throw new InvalidOperationException("Please provide both `task_graph` and `task_options`.");
        if (InputStreams?.Count <= 0 || OutputStreams?.Count <= 0)
            throw new InvalidOperationException("Both `input_streams` and `output_streams` must be non-empty.");

        if (!enableFlowLimiting)
            return new CalculatorGraphConfig
            {
                Node =
                {
                    new CalculatorGraphConfig.Types.Node
                    {
                        Calculator = TaskGraph,
                        Options = TaskOptions.ToCalculatorOptions(),
                        InputStream = { InputStreams },
                        OutputStream = { OutputStreams }
                    }
                },
                InputStream = { InputStreams },
                OutputStream = { OutputStreams }
            };

        IEnumerable<string> throttledInputStreams = InputStreams!.Select(AddStreamNamePrefix);
        string finishedStream = $"FINISHED:{Tool.ParseNameFromStream(OutputStreams!.First())}";
        CalculatorOptions flowLimiterOptions = new();
        flowLimiterOptions.SetExtension(FlowLimiterCalculatorOptions.Extensions.Ext, new FlowLimiterCalculatorOptions
        {
            MaxInFlight = 1,
            MaxInQueue = 1
        });

        return new CalculatorGraphConfig
        {
            Node =
            {
                new CalculatorGraphConfig.Types.Node
                {
                    Calculator = "FlowLimiterCalculator",
                    InputStreamInfo =
                    {
                        new InputStreamInfo
                        {
                            TagIndex = "FINISHED",
                            BackEdge = true
                        }
                    },
                    InputStream =
                    {
                        InputStreams!.Select(Tool.ParseNameFromStream).Append(finishedStream)
                    },
                    OutputStream =
                    {
                        throttledInputStreams.Select(Tool.ParseNameFromStream)
                    },
                    Options = flowLimiterOptions
                },
                new CalculatorGraphConfig.Types.Node
                {
                    Calculator = TaskGraph,
                    InputStream = { throttledInputStreams },
                    OutputStream = { OutputStreams },
                    Options = TaskOptions.ToCalculatorOptions()
                }
            },
            InputStream = { InputStreams },
            OutputStream = { OutputStreams }
        };
    }

    private static string AddStreamNamePrefix(string tagIndexName)
    {
        Tool.ParseTagAndName(tagIndexName, out string tag, out string name);
        return $"{tag}:throttled_{name}";
    }
}
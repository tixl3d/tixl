using Mediapipe;

namespace Mediapipe.Framework.Tool;

/// <summary>
///     translated version of mediapipe/framework/tool/name_util.cc
///     <summary />
public static partial class Tool
{
    public static string GetUnusedNodeName(CalculatorGraphConfig config, string nodeNameBase)
    {
        HashSet<string> nodeNames = new(config.Node.Select(node => node.Name).Where(name => name.Length > 0));

        string candidate = nodeNameBase;
        int iter = 1;

        while (nodeNames.Contains(candidate)) candidate = $"{nodeNameBase}_{++iter:D2}";

        return candidate;
    }

    public static string GetUnusedSidePacketName(CalculatorGraphConfig config, string inputSidePacketNameBase)
    {
        HashSet<string> inputSidePackets = new(
            config.Node.SelectMany(node => node.InputSidePacket)
                .Select(sidePacket =>
                {
                    ParseTagIndexName(sidePacket, out string tag, out int index, out string name);
                    return name;
                }));

        string candidate = inputSidePacketNameBase;
        int iter = 1;

        while (inputSidePackets.Contains(candidate)) candidate = $"{inputSidePacketNameBase}_{++iter:D2}";

        return candidate;
    }

    public static string GetUnusedStreamName(CalculatorGraphConfig config, string streamNameBase)
    {
        IEnumerable<string> outputStreamNames = config.Node.SelectMany(node => node.OutputStream)
            .Select(outputStream =>
            {
                ParseTagIndexName(outputStream, out string tag, out int index, out string name);
                return name;
            });

        string candidate = streamNameBase;
        int iter = 1;

        while (config.InputStream.Contains(candidate)) candidate = $"{streamNameBase}_{++iter:D2}";

        while (outputStreamNames.Contains(candidate)) candidate = $"{streamNameBase}_{++iter:D2}";

        return candidate;
    }

    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="nodeId" /> is invalid
    /// </exception>
    public static string CanonicalNodeName(CalculatorGraphConfig graphConfig, int nodeId)
    {
        CalculatorGraphConfig.Types.Node? nodeConfig = graphConfig.Node[nodeId];
        string? nodeName = nodeConfig.Name.Length == 0 ? nodeConfig.Calculator : nodeConfig.Name;

        IEnumerable<(string, int i)> nodesWithSameName = graphConfig.Node
            .Select((node, i) => (node.Name.Length == 0 ? node.Calculator : node.Name, i))
            .Where(pair => pair.Item1 == nodeName);

        if (nodesWithSameName.Count() <= 1) return nodeName;

        int seq = nodesWithSameName.Count(pair => pair.i <= nodeId);
        return $"{nodeName}_{seq}";
    }

    /// <exception cref="ArgumentException">
    ///     Thrown when the format of <paramref cref="stream" /> is invalid
    /// </exception>
    public static string ParseNameFromStream(string stream)
    {
        ParseTagIndexName(stream, out _, out _, out string name);
        return name;
    }

    /// <exception cref="ArgumentException">
    ///     Thrown when the format of <paramref cref="tagIndex" /> is invalid
    /// </exception>
    public static (string, int) ParseTagIndex(string tagIndex)
    {
        ParseTagIndex(tagIndex, out string tag, out int index);
        return (tag, index);
    }

    /// <exception cref="ArgumentException">
    ///     Thrown when the format of <paramref cref="stream" /> is invalid
    /// </exception>
    public static (string, int) ParseTagIndexFromStream(string stream)
    {
        ParseTagIndexName(stream, out string tag, out int index, out _);
        return (tag, index);
    }

    public static string CatTag(string tag, int index)
    {
        string colonIndex = index <= 0 || tag.Length == 0 ? "" : $":{index}";
        return $"{tag}{colonIndex}";
    }

    public static string CatStream((string, int) tagIndex, string name)
    {
        string tag = CatTag(tagIndex.Item1, tagIndex.Item2);

        return tag.Length == 0 ? name : $"{tag}:{name}";
    }
}
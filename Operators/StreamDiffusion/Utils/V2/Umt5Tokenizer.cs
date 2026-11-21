using System.Text.Json;

namespace t3.streamdiffusion.V2;

/// <summary>
/// Unigram tokenizer for the UMT5 vocabulary of Wan2.1, read from the Hugging Face
/// <c>tokenizer.json</c>. Splits on spaces like the Metaspace pre-tokenizer, then runs Viterbi per word.
/// </summary>
internal sealed class Umt5Tokenizer
{
    public const int PadId = 0;
    public const int EosId = 1;
    public const int UnkId = 3;

    private const char WordMarker = '▁';

    /** Same penalty the reference Unigram model subtracts from its lowest score for unknown characters. */
    private const float UnknownPenalty = 10f;

    private readonly Dictionary<string, (int Id, float Score)> _pieces;
    private readonly int _maxPieceLength;
    private readonly float _unknownScore;

    private Umt5Tokenizer(Dictionary<string, (int, float)> pieces, int maxPieceLength, float minScore)
    {
        _pieces = pieces;
        _maxPieceLength = maxPieceLength;
        _unknownScore = minScore - UnknownPenalty;
    }

    public static Umt5Tokenizer Load(string tokenizerJsonPath)
    {
        using var stream = File.OpenRead(tokenizerJsonPath);
        using var document = JsonDocument.Parse(stream);
        var vocab = document.RootElement.GetProperty("model").GetProperty("vocab");

        var pieces = new Dictionary<string, (int, float)>(vocab.GetArrayLength());
        var maxLength = 1;
        var minScore = float.MaxValue;
        var id = 0;
        foreach (var entry in vocab.EnumerateArray())
        {
            var piece = entry[0].GetString() ?? string.Empty;
            var score = (float)entry[1].GetDouble();
            pieces[piece] = (id, score);
            maxLength = Math.Max(maxLength, piece.Length);

            // Control tokens (<pad>, </s>, ...) carry score 0 and must not set the unknown penalty baseline
            if (id > UnkId && score < minScore)
                minScore = score;

            id++;
        }

        return new Umt5Tokenizer(pieces, maxLength, minScore);
    }

    /// <summary>Token ids for <paramref name="text"/> followed by the end-of-sequence id, truncated to <paramref name="maxLength"/>.</summary>
    public int[] Encode(string text, int maxLength)
    {
        var ids = new List<int>();
        foreach (var word in SplitWords(text))
        {
            AppendWord(WordMarker + word, ids);
        }

        if (ids.Count > maxLength - 1)
            ids.RemoveRange(maxLength - 1, ids.Count - (maxLength - 1));

        ids.Add(EosId);
        return ids.ToArray();
    }

    private static IEnumerable<string> SplitWords(string text)
    {
        // Matches the reference's whitespace cleaning: runs of whitespace collapse to one space, ends are trimmed
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private void AppendWord(string word, List<int> ids)
    {
        var chars = ToTextElements(word);
        var count = chars.Count;

        var bestScore = new float[count + 1];
        var bestStart = new int[count + 1];
        var bestId = new int[count + 1];
        Array.Fill(bestScore, float.NegativeInfinity);
        bestScore[0] = 0f;

        var offsets = new int[count + 1];
        for (var i = 0; i < count; i++)
            offsets[i + 1] = offsets[i] + chars[i].Length;

        for (var end = 1; end <= count; end++)
        {
            for (var start = Math.Max(0, end - _maxPieceLength); start < end; start++)
            {
                if (float.IsNegativeInfinity(bestScore[start]))
                    continue;

                var length = offsets[end] - offsets[start];
                if (length > _maxPieceLength)
                    continue;

                var piece = word.Substring(offsets[start], length);
                if (_pieces.TryGetValue(piece, out var entry))
                {
                    var candidate = bestScore[start] + entry.Score;
                    if (candidate > bestScore[end])
                    {
                        bestScore[end] = candidate;
                        bestStart[end] = start;
                        bestId[end] = entry.Id;
                    }
                }
                else if (end - start == 1)
                {
                    var candidate = bestScore[start] + _unknownScore;
                    if (candidate > bestScore[end])
                    {
                        bestScore[end] = candidate;
                        bestStart[end] = start;
                        bestId[end] = UnkId;
                    }
                }
            }
        }

        var wordIds = new List<int>();
        for (var end = count; end > 0; end = bestStart[end])
            wordIds.Add(bestId[end]);

        wordIds.Reverse();
        ids.AddRange(wordIds);
    }

    /** Splits into Unicode scalar values so surrogate pairs stay together, as the reference tokenizer works on characters. */
    private static List<string> ToTextElements(string text)
    {
        var result = new List<string>(text.Length);
        foreach (var rune in text.EnumerateRunes())
            result.Add(rune.ToString());

        return result;
    }
}

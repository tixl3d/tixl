#nullable enable
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes.Vector;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Editor.Gui.OutputUi;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Selection;
using T3.Serialization;

// ReSharper disable AssignNullToNotNullAttribute

namespace T3.Editor.UiModel;

internal static class SymbolUiJson
{
    internal static void WriteSymbolUi(SymbolUi symbolUi, JsonTextWriter writer)
    {
        try
        {
            writer.WriteStartObject();

            writer.WriteValue(JsonKeys.Id, symbolUi.Symbol.Id);
            writer.WriteComment(symbolUi.Symbol.Name);

            writer.WriteObject(JsonKeys.Description, symbolUi.Description);
            if (symbolUi.Tags != SymbolUi.SymbolTags.None)
                writer.WriteObject(JsonKeys.SymbolTags, (int)symbolUi.Tags); // Writing as bitmask might not be ideal...

            WriteInputUis(symbolUi, writer);
            WriteChildUis(symbolUi, writer);
            WriteOutputUis(symbolUi, writer);
            WriteAnnotations(symbolUi, writer);
            WriteLinks(symbolUi, writer);
            WriteTourPoints(symbolUi, writer);
            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            Log.Error($"Error writing symbol ui for {symbolUi.Symbol.Name} {symbolUi.Symbol.Id} to json: {e}");
            throw;
        }
    }

    private static void WriteInputUis(SymbolUi symbolUi, JsonTextWriter writer)
    {
        writer.WritePropertyName(JsonKeys.InputUis);
        writer.WriteStartArray();

        foreach (var inputEntry in symbolUi.InputUis.OrderBy(x => x.Key))
        {
            var symbolInput = symbolUi.Symbol.InputDefinitions.SingleOrDefault(inputDef => inputDef.Id == inputEntry.Key);
            if (symbolInput == null)
            {
                Log.Info($"In '{symbolUi.Symbol.Name}': Didn't found input definition for InputUi, skipping this one. This can happen if an input got removed.");
                continue;
            }

            writer.WriteStartObject(); // input entry
            writer.WriteObject(JsonKeys.InputId, inputEntry.Key);
            writer.WriteComment(symbolInput.Name);
            var inputUi = inputEntry.Value;
            inputUi.Write(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteChildUis(SymbolUi symbolUi, JsonTextWriter writer)
    {
        writer.WritePropertyName(JsonKeys.SymbolChildUis);
        writer.WriteStartArray();

        foreach (var childUi in symbolUi.ChildUis.Values.OrderBy(x => x.Id))
        {
            // Skip orphaned child UIs whose symbol child no longer exists
            if (childUi.SymbolChild == null ||
                !symbolUi.Symbol.Children.TryGetValue(childUi.Id, out var symbolChild))
            {
                Log.Warning($"Skipping UI child {childUi.Id} in '{symbolUi.Symbol.Name}' because corresponding symbol child is missing");
                continue;
            }

            writer.WriteStartObject(); // child entry
            writer.WriteObject(JsonKeys.ChildId, childUi.Id);

            {
                writer.WriteComment(symbolChild.ReadableName);

                if (childUi.CollapsedIntoAnnotationFrameId != Guid.Empty)
                {
                    writer.WriteObject(JsonKeys.AnnotationId, childUi.CollapsedIntoAnnotationFrameId);
                }

                if (childUi.Style != SymbolUi.Child.Styles.Default)
                {
                    writer.WriteObject(JsonKeys.Style, childUi.Style);

                    if (childUi.Size != SymbolUi.Child.DefaultOpSize)
                    {
                        writer.WritePropertyName(JsonKeys.Size);
                        _vector2ToJson(writer, childUi.Size);
                    }
                }

                if (!string.IsNullOrEmpty(childUi.Comment))
                {
                    writer.WriteObject(JsonKeys.Comment, childUi.Comment);
                }

                writer.WritePropertyName(JsonKeys.Position);
                _vector2ToJson(writer, childUi.PosOnCanvas);

                if (childUi.SnapshotGroupIndex != 0)
                    writer.WriteObject(nameof(SymbolUi.Child.SnapshotGroupIndex), childUi.SnapshotGroupIndex);

                if (childUi.ConnectionStyleOverrides.Count > 0)
                {
                    writer.WritePropertyName(JsonKeys.ConnectionStyleOverrides);
                    writer.WriteStartArray();
                    foreach (var (key, value) in childUi.ConnectionStyleOverrides)
                    {
                        writer.WriteStartObject();
                        writer.WriteObject(JsonKeys.Id, key);
                        writer.WriteObject(JsonKeys.Style, value);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }


    private static void WriteOutputUis(SymbolUi symbolUi, JsonTextWriter writer)
    {
        writer.WritePropertyName(JsonKeys.OutputUis);
        writer.WriteStartArray();

        foreach (var outputEntry in symbolUi.OutputUis)
        {
            writer.WriteStartObject(); // output entry
            writer.WriteObject(JsonKeys.OutputId, outputEntry.Key);
            var outputName = symbolUi.Symbol.OutputDefinitions.Single(outputDef => outputDef.Id == outputEntry.Key).Name;
            writer.WriteComment(outputName);
            var outputUi = outputEntry.Value;
            writer.WritePropertyName(JsonKeys.Position);
            _vector2ToJson(writer, outputUi.PosOnCanvas);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteAnnotations(SymbolUi symbolUi, JsonTextWriter writer)
    {
        if (symbolUi.Annotations.Count == 0)
            return;
        writer.WritePropertyName(JsonKeys.Annotations);
        writer.WriteStartArray();

        foreach (var annotation in symbolUi.Annotations.Values.OrderBy(x => x.Id))
        {
            writer.WriteStartObject();
            writer.WriteObject(JsonKeys.Id, annotation.Id);
            if (!string.IsNullOrEmpty(annotation.Title))
            {
                writer.WriteObject(JsonKeys.Title, annotation.Title);
            }

            if (!string.IsNullOrEmpty(annotation.Label))
            {
                writer.WriteObject(JsonKeys.Label, annotation.Label);
            }

            if (annotation.Collapsed)
                writer.WriteObject(JsonKeys.Collapsed, annotation.Collapsed);

            writer.WritePropertyName(JsonKeys.Color);
            _vector4ToJson(writer, annotation.Color.Rgba);

            writer.WritePropertyName(JsonKeys.Position);
            _vector2ToJson(writer, annotation.PosOnCanvas);

            writer.WritePropertyName(JsonKeys.Size);
            _vector2ToJson(writer, annotation.Size);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLinks(SymbolUi symbolUi, JsonTextWriter writer)
    {
        if (symbolUi.Links.Count == 0)
            return;

        writer.WritePropertyName(JsonKeys.Links);
        writer.WriteStartArray();

        foreach (var link in symbolUi.Links.Values.OrderBy(x => x.Id))
        {
            writer.WriteStartObject();
            writer.WriteObject(JsonKeys.Id, link.Id.ToString());
            writer.WriteObject(JsonKeys.Title, link.Title ?? string.Empty);
            writer.WriteObject(JsonKeys.Description, link.Description ?? string.Empty);
            writer.WriteObject(JsonKeys.LinkUrl, link.Url ?? string.Empty);
            writer.WriteObject(JsonKeys.LinkType, link.Type);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTourPoints(SymbolUi symbolUi, JsonTextWriter writer)
    {
        if (symbolUi.TourPoints.Count == 0)
            return;

        writer.WritePropertyName(JsonKeys.TourPoints);
        writer.WriteStartArray();

        foreach (var tourPoint in symbolUi.TourPoints)
        {
            writer.WriteStartObject();
            {
                writer.WriteObject(JsonKeys.Id, tourPoint.Id.ToString());

                if (tourPoint.ChildId != Guid.Empty)
                    writer.WriteObject(JsonKeys.ChildId, tourPoint.ChildId.ToString());

                if (tourPoint.InputId != Guid.Empty)
                    writer.WriteObject(JsonKeys.InputId, tourPoint.InputId.ToString());

                writer.WriteObject(JsonKeys.Description, tourPoint.Description ?? string.Empty);

                if (tourPoint.Style != TourPoint.Styles.Info)
                    writer.WriteObject(JsonKeys.Style, tourPoint.Style);
            }
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    internal static bool TryReadSymbolUiExternal(JToken mainObject, Symbol symbol, [NotNullWhen(true)] out SymbolUi? symbolUi)
    {
        symbolUi = null;
        var guidToken = mainObject[JsonKeys.Id];
        if (guidToken == null)
            return false;

        var guidString = guidToken.Value<string>();
        if (!Guid.TryParse(guidString, out _))
        {
            Log.Warning($"Error parsing guid {guidString}");
            return false;
        }

        var success = TryReadSymbolUi(mainObject, symbol, out symbolUi);
        if (!success || symbolUi == null)
            return success;

        symbolUi.UpdateConsistencyWithSymbol(symbol);
        return true;
    }

    internal static bool TryReadSymbolUi(JToken mainObject, Symbol symbol, [NotNullWhen(true)] out SymbolUi? symbolUi)
    {
        var inputDict = new OrderedDictionary<Guid, IInputUi>();
        if (TryGetJArray(JsonKeys.InputUis, mainObject, symbol, out var inputUiArray))
        {
            foreach (JToken uiInputToken in inputUiArray)
            {
                //inputId = Guid.Parse(uiInputEntry[JsonKeys.InputId].Value<string>() ?? string.Empty);

                if (!JsonUtils.TryGetGuid(uiInputToken[JsonKeys.InputId], out var inputId))
                {
                    Log.Error("Skipping input with missing or invalid id");
                    continue;
                }

                var inputDefinition = symbol.InputDefinitions.SingleOrDefault(def => def.Id == inputId);
                if (inputDefinition == null)
                {
                    Log.Warning($"Found input entry in ui file for symbol '{symbol.Name}', but no corresponding input in symbol. " +
                                $"Assuming that the input was removed and ignoring the ui information.");
                    continue;
                }

                var type = inputDefinition.DefaultValue.ValueType;

                // get the symbol input definition
                var inputUi = InputUiFactory.Instance.CreateFor(type);
                inputUi.InputDefinition = inputDefinition;
                inputUi.Read(uiInputToken);
                inputDict.Add(inputId, inputUi);
            }
        }

        var outputDict = new OrderedDictionary<Guid, IOutputUi>();
        if (TryGetJArray(JsonKeys.OutputUis, mainObject, symbol, out var outputUiArray))
        {
            foreach (var uiOutputToken in outputUiArray)
            {
                if (!JsonUtils.TryGetGuid(uiOutputToken[JsonKeys.OutputId], out var outputId))
                {
                    Log.Error("Skipping input with missing or invalid id");
                    continue;
                }

                var outputDefinition = symbol.OutputDefinitions.SingleOrDefault(def => def.Id == outputId);
                if (outputDefinition == null)
                {
                    Log.Warning($"Found output entry in ui file for symbol '{symbol.Name}', but no corresponding output in symbol. " +
                                $"Assuming that the output was removed and ignoring the ui information.");
                    continue;
                }

                var outputUi = OutputUiFactory.Instance.CreateFor(outputDefinition.ValueType);
                outputUi.OutputDefinition = symbol.OutputDefinitions.First(def => def.Id == outputId);
                outputUi.PosOnCanvas = GetVec2OrDefault(uiOutputToken[JsonKeys.Position]);
                outputDict.Add(outputId, outputUi);
            }
        }

        var annotationDict = ReadAnnotations(mainObject);
        var linksDict = ReadLinks(mainObject);
        var tourPoints = ReadTourPoints(mainObject);

        IEnumerable<JToken> symbolChildUiJsonEnumerable;
        if (TryGetJArray(JsonKeys.SymbolChildUis, mainObject, symbol, out var symbolChildUiJson))
        {
            symbolChildUiJsonEnumerable = symbolChildUiJson;
        }
        else
        {
            symbolChildUiJsonEnumerable = Array.Empty<JToken>();
        }

        symbolUi = new SymbolUi(symbol: symbol,
                                childUis: parent => CreateSymbolUiChildren(parent, symbolChildUiJsonEnumerable),
                                inputs: inputDict,
                                outputs: outputDict,
                                annotations: annotationDict,
                                links: linksDict,
                                tourPoints: tourPoints,
                                updateConsistency: false);

        var descriptionEntry = mainObject[JsonKeys.Description];
        if (descriptionEntry?.Value<string>() != null)
            symbolUi.Description = descriptionEntry.Value<string>() ?? string.Empty;

        var tagsEntry = mainObject[JsonKeys.SymbolTags];
        if (tagsEntry?.Value<int>() != null)
            symbolUi.Tags = (SymbolUi.SymbolTags)tagsEntry.Value<int>();

        return true;
    }

    private static bool TryGetJArray(string key, JToken token, Symbol symbol, [NotNullWhen(true)] out JArray? array)
    {
        var exceptionRaised = false;
        array = null;

        try
        {
            array = (JArray?)token[key];
            if (array == null)
                return false;
        }
        catch
        {
            exceptionRaised = true;
        }

        if (!exceptionRaised && array != null)
        {
            return true;
        }

        Log.Error($"Error parsing {key} array from {symbol}'s {EditorSymbolPackage.SymbolUiExtension} file - invalid format");
        //BlockingWindow.Instance.ShowMessageBox($"Error parsing symbol ui ({EditorSymbolPackage.SymbolUiExtension}) file of {symbol}.\n\n" +
        //                                      $"It will be regenerated next time you save this project.\n\n" +
        //                                     token);
        return false;
    }

    private static List<SymbolUi.Child> CreateSymbolUiChildren(Symbol parentSymbol, IEnumerable<JToken> childJsons)
    {
        var symbolChildUis = new List<SymbolUi.Child>();
        var symbolId = parentSymbol.Id;
        foreach (var childEntry in childJsons)
        {
            if (!JsonUtils.TryGetGuid(childEntry[JsonKeys.ChildId], out var childId))
            {
                Log.Warning($"Skipping UI child definition in {parentSymbol.Name} {symbolId} for invalid child id");
                continue;
            }

            if (!parentSymbol.Children.TryGetValue(childId, out var symbolChild))
            {
                foreach (var child in parentSymbol.Children.Values)
                {
                    // check for duplication / changed child ids
                    if (child.PreviousId.HasValue && child.PreviousId == childId)
                    {
                        symbolChild = child;
                        childId = child.Id;
                        child.ClearPreviousId();
                        break;
                    }
                }

                if (symbolChild == null)
                {
                    Log.Warning($"Skipping UI child definition in {parentSymbol.Name} {symbolId} for undefined child {childId}");
                    continue;
                }
            }

            var childUi = new SymbolUi.Child(symbolChild.Id, parentSymbol.Id, (EditorSymbolPackage)parentSymbol.SymbolPackage);

            if (childEntry[JsonKeys.Comment] != null)
            {
                childUi.Comment = childEntry[JsonKeys.Comment]?.Value<string>() ?? string.Empty;
            }

            var positionToken = childEntry[JsonKeys.Position];
            childUi.PosOnCanvas = GetVec2OrDefault(positionToken);

            if (childEntry[JsonKeys.Size] != null)
            {
                var sizeToken = childEntry[JsonKeys.Size];
                childUi.Size = GetVec2OrDefault(sizeToken);
            }

            if (childEntry[nameof(SymbolUi.Child.SnapshotGroupIndex)] != null)
            {
                childUi.SnapshotGroupIndex = (childEntry[nameof(SymbolUi.Child.SnapshotGroupIndex)] ?? -1).Value<int>();
            }

            childUi.Style = JsonUtils.TryGetEnumValue(childEntry[JsonKeys.Style], out SymbolUi.Child.Styles childStyle)
                                ? childStyle
                                : SymbolUi.Child.Styles.Default;

            if (JsonUtils.TryGetGuid(childEntry[JsonKeys.AnnotationId], out var annotationId))
            {
                childUi.CollapsedIntoAnnotationFrameId = annotationId;
            }

            var conStyleEntry = childEntry[JsonKeys.ConnectionStyleOverrides];
            if (conStyleEntry != null)
            {
                var dict = childUi.ConnectionStyleOverrides;
                foreach (var styleEntry in (JArray)conStyleEntry)
                {
                    if (!JsonUtils.TryGetGuid(styleEntry[JsonKeys.Id], out var id)
                        || !JsonUtils.TryGetEnumValue(styleEntry[JsonKeys.Style], out SymbolUi.Child.ConnectionStyles style))
                    {
                        Log.Warning($"Skipping connection style override for invalid id or style");
                        continue;
                    }

                    dict.Add(id, style);
                }
            }

            symbolChildUis.Add(childUi);
        }

        return symbolChildUis;
    }

    private static OrderedDictionary<Guid, Annotation> ReadAnnotations(JToken token)
    {
        var annotationDict = new OrderedDictionary<Guid, Annotation>();

        var annotationsToken = token[JsonKeys.Annotations];
        if (annotationsToken is not JArray annotationsArray)
            return annotationDict;

        foreach (var annotationEntry in annotationsArray)
        {
            if (!JsonUtils.TryGetGuid(annotationEntry[JsonKeys.Id], out var id))
            {
                Log.Warning("Skipping annotation with missing or invalid id");
                continue;
            }

            var annotation = new Annotation
                                 {
                                     Id = id,
                                     Title = annotationEntry[JsonKeys.Title]?.Value<string>() ?? string.Empty,
                                     Label = annotationEntry[JsonKeys.Label]?.Value<string>() ?? string.Empty,
                                     PosOnCanvas = GetVec2OrDefault(annotationEntry[JsonKeys.Position]),
                                     Collapsed = annotationEntry[JsonKeys.Collapsed]?.Value<bool>() ?? false,
                                 };

            var colorEntry = annotationEntry[JsonKeys.Color];
            if (colorEntry != null)
            {
                annotation.Color = new Color((Vector4)_jsonToVector4(colorEntry));
            }

            annotation.Size = GetVec2OrDefault(annotationEntry[JsonKeys.Size]);
            annotationDict[annotation.Id] = annotation;
        }

        return annotationDict;
    }

    private static OrderedDictionary<Guid, ExternalLink> ReadLinks(JToken token)
    {
        var linkDict = new OrderedDictionary<Guid, ExternalLink>();

        var linksToken = token[JsonKeys.Links];
        if (linksToken is not JArray linksArray)
            return linkDict;

        foreach (var linkEntry in linksArray)
        {
            if (!Enum.TryParse<ExternalLink.LinkTypes>(linkEntry[JsonKeys.LinkType]?.Value<string>(), out var type))
                type = ExternalLink.LinkTypes.Other;

            if (!JsonUtils.TryGetGuid(linkEntry[JsonKeys.Id], out var id))
            {
                Log.Warning("Skipping annotation with missing or invalid id");
                continue;
            }

            var link = new ExternalLink
                           {
                               Id = id,
                               Title = linkEntry[JsonKeys.Title]?.Value<string>() ?? string.Empty,
                               Description = linkEntry[JsonKeys.Description]?.Value<string>() ?? string.Empty,
                               Url = linkEntry[JsonKeys.LinkUrl]?.Value<string>() ?? string.Empty,
                               Type = type,
                           };

            linkDict[link.Id] = link;
        }

        return linkDict;
    }

    private static List<TourPoint> ReadTourPoints(JToken token)
    {
        var tourPoints = new List<TourPoint>();

        var tourPointsToken = token[JsonKeys.TourPoints];
        if (tourPointsToken is not JArray tourPointsArray)
            return tourPoints;

        foreach (var tourPointEntry in tourPointsArray)
        {
            if (!Enum.TryParse<TourPoint.Styles>(tourPointEntry[JsonKeys.Style]?.Value<string>(), out var style))
                style = TourPoint.Styles.Info;

            if (!JsonUtils.TryGetGuid(tourPointEntry[JsonKeys.Id], out var id))
            {
                Log.Warning("Skipping tour point with missing or invalid id");
                continue;
            }

            JsonUtils.TryGetGuid(tourPointEntry[JsonKeys.ChildId], out var childId);
            JsonUtils.TryGetGuid(tourPointEntry[JsonKeys.InputId], out var inputId);

            var description = tourPointEntry[JsonKeys.Description]?.Value<string>() ?? string.Empty;
            if (string.IsNullOrEmpty(description))
            {
                description = tourPointEntry[JsonKeys.Title]?.Value<string>() ?? string.Empty;
            }

            tourPoints.Add(new TourPoint
                               {
                                   Id = id,
                                   ChildId = childId,
                                   InputId = inputId,
                                   Description = description,
                                   Style = style,
                               });
        }

        return tourPoints;
    }

    internal static Vector2 GetVec2OrDefault(JToken? token)
    {
        return token == null ? default : (Vector2)_jsonToVector2(token);
    }

    private readonly struct JsonKeys
    {
        public const string InputUis = nameof(InputUis);
        public const string InputId = nameof(InputId);
        public const string OutputUis = nameof(OutputUis);
        public const string OutputId = nameof(OutputId);
        public const string SymbolChildUis = nameof(SymbolChildUis);
        public const string ChildId = nameof(ChildId);
        public const string Position = nameof(Position);
        public const string Annotations = nameof(Annotations);
        public const string AnnotationId = nameof(AnnotationId);
        public const string Collapsed = nameof(Collapsed);
        public const string Links = nameof(Links);
        public const string Comment = nameof(Comment);
        public const string Id = nameof(Id);
        public const string Title = nameof(Title);
        public const string Label = nameof(Label);
        public const string Color = nameof(Color);
        public const string Size = nameof(Size);
        public const string Description = nameof(Description);
        public const string Style = nameof(Style);
        public const string ConnectionStyleOverrides = nameof(ConnectionStyleOverrides);
        public const string LinkType = nameof(LinkType);
        public const string LinkUrl = nameof(LinkUrl);
        public const string SymbolTags = nameof(SymbolTags);
        public const string TourPoints = nameof(TourPoints);
    }

    private static readonly Func<JToken, object> _jsonToVector2 = JsonToTypeValueConverters.Entries[typeof(Vector2)];
    private static readonly Func<JToken, object> _jsonToVector4 = JsonToTypeValueConverters.Entries[typeof(Vector4)];
    private static readonly Action<JsonTextWriter, object> _vector2ToJson = TypeValueToJsonConverters.Entries[typeof(Vector2)];
    private static readonly Action<JsonTextWriter, object> _vector4ToJson = TypeValueToJsonConverters.Entries[typeof(Vector4)];
}
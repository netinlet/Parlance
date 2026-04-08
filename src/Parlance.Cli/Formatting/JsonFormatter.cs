using System.Text.Json;
using System.Text.Json.Serialization;
using Parlance.Analysis;

namespace Parlance.Cli.Formatting;

internal sealed class JsonFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Format(FileAnalysisResult result) =>
        JsonSerializer.Serialize(result, SerializerOptions);
}

using Parlance.Analysis;

namespace Parlance.Cli.Formatting;

internal interface IOutputFormatter
{
    string Format(FileAnalysisResult result);
}

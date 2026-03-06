namespace Parlance.Cli.Formatting;

internal interface IOutputFormatter
{
    string Format(AnalysisOutput output);
}

using System.Collections.Immutable;
using System.ComponentModel;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Parlance.CSharp.Workspace;

namespace Parlance.Mcp.Tools;

[McpServerToolType]
public sealed class GetSymbolDocsTool
{
    [McpServerTool(Name = "get-symbol-docs", ReadOnly = true)]
    [Description("Returns XML documentation for a symbol (type, method, property, field). " +
                 "Handles inheritdoc by walking to the base symbol.")]
    public static async Task<GetSymbolDocsResult> GetSymbolDocs(
        WorkspaceSessionHolder holder, WorkspaceQueryService query,
        ILogger<GetSymbolDocsTool> logger, string symbolName, CancellationToken ct)
    {
        using var _ = ToolDiagnostics.TimeToolCall(logger, "get-symbol-docs");

        if (holder.LoadFailure is { } failure)
            return GetSymbolDocsResult.LoadFailed(failure.Message);
        if (!holder.IsLoaded)
            return GetSymbolDocsResult.NotLoaded();

        var symbols = await query.FindSymbolsAsync(symbolName, ct: ct);
        if (symbols.IsEmpty)
            return GetSymbolDocsResult.NotFound(symbolName);

        if (symbols.Count > 1 && !symbolName.Contains('.'))
            return GetSymbolDocsResult.Ambiguous(symbolName, symbols.Select(s => s.ToCandidate()).ToImmutableList());

        var symbol = symbols[0].Symbol;
        var docs = GetDocs(symbol, logger);

        if (docs is null)
            return GetSymbolDocsResult.NoDocs(symbolName);

        return new GetSymbolDocsResult(
            Status: "found",
            SymbolName: symbol.ToDisplayString(),
            Summary: docs.Summary,
            Returns: docs.Returns,
            Remarks: docs.Remarks,
            Params: docs.Params,
            TypeParams: docs.TypeParams,
            Exceptions: docs.Exceptions,
            Candidates: [],
            Message: null);
    }

    private static ParsedDocs? GetDocs(ISymbol symbol, ILogger? logger = null)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return null;

            // Handle inheritdoc — try to get base symbol's docs
            if (root.Element("inheritdoc") is not null)
            {
                var baseDocs = GetInheritedDocs(symbol, logger);
                if (baseDocs is not null) return baseDocs;
                // Fall through and try to parse what we have
            }

            return ParseDocs(root);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to parse XML docs for {Symbol}", symbol.ToDisplayString());
            return null;
        }
    }

    private static ParsedDocs? GetInheritedDocs(ISymbol symbol, ILogger? logger = null)
    {
        if (symbol is IMethodSymbol method)
        {
            var overridden = method.OverriddenMethod;
            if (overridden is not null)
            {
                var docs = GetDocs(overridden, logger);
                if (docs is not null) return docs;
            }
            foreach (var iface in method.ExplicitInterfaceImplementations)
            {
                var docs = GetDocs(iface, logger);
                if (docs is not null) return docs;
            }
            // Check implicit interface implementations
            foreach (var iface in method.ContainingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers(method.Name).OfType<IMethodSymbol>())
                {
                    if (method.ContainingType.FindImplementationForInterfaceMember(member) is IMethodSymbol impl &&
                        SymbolEqualityComparer.Default.Equals(impl, method))
                    {
                        var docs = GetDocs(member, logger);
                        if (docs is not null) return docs;
                    }
                }
            }
        }
        else if (symbol is INamedTypeSymbol type)
        {
            if (type.BaseType is not null)
            {
                var docs = GetDocs(type.BaseType, logger);
                if (docs is not null) return docs;
            }
        }
        return null;
    }

    private static ParsedDocs ParseDocs(XElement root)
    {
        var summary = ExtractText(root.Element("summary"));
        var returns = ExtractText(root.Element("returns"));
        var remarks = ExtractText(root.Element("remarks"));

        var parms = root.Elements("param")
            .Select(e => new DocParam(e.Attribute("name")?.Value ?? "", ExtractText(e) ?? ""))
            .ToImmutableList();

        var typeParams = root.Elements("typeparam")
            .Select(e => new DocParam(e.Attribute("name")?.Value ?? "", ExtractText(e) ?? ""))
            .ToImmutableList();

        var exceptions = root.Elements("exception")
            .Select(e => new DocParam(e.Attribute("cref")?.Value ?? "", ExtractText(e) ?? ""))
            .ToImmutableList();

        return new ParsedDocs(summary, returns, remarks, parms, typeParams, exceptions);
    }

    private static string? ExtractText(XElement? element)
    {
        if (element is null) return null;

        // Replace <see cref="..."/> with just the cref value (strip T:, M:, P: prefixes)
        foreach (var see in element.Descendants("see").ToList())
        {
            var cref = see.Attribute("cref")?.Value ?? see.Attribute("langword")?.Value ?? "";
            var name = cref.Contains(':') ? cref[(cref.IndexOf(':') + 1)..] : cref;
            see.ReplaceWith(new XText(name));
        }

        foreach (var paramRef in element.Descendants("paramref").ToList())
        {
            var name = paramRef.Attribute("name")?.Value ?? "";
            paramRef.ReplaceWith(new XText(name));
        }

        var text = element.Value.Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private sealed record ParsedDocs(
        string? Summary, string? Returns, string? Remarks,
        ImmutableList<DocParam> Params, ImmutableList<DocParam> TypeParams,
        ImmutableList<DocParam> Exceptions);
}

public sealed record GetSymbolDocsResult(
    string Status, string? SymbolName, string? Summary, string? Returns,
    string? Remarks, ImmutableList<DocParam> Params, ImmutableList<DocParam> TypeParams,
    ImmutableList<DocParam> Exceptions, ImmutableList<SymbolCandidate> Candidates, string? Message)
{
    public static GetSymbolDocsResult NotFound(string symbolName) => new(
        "not_found", symbolName, null, null, null, [], [], [], [], $"Symbol '{symbolName}' not found");
    public static GetSymbolDocsResult NotLoaded() => new(
        "not_loaded", null, null, null, null, [], [], [], [], "Workspace is still loading");
    public static GetSymbolDocsResult LoadFailed(string message) => new(
        "load_failed", null, null, null, null, [], [], [], [], message);
    public static GetSymbolDocsResult NoDocs(string symbolName) => new(
        "no_docs", symbolName, null, null, null, [], [], [], [], $"No documentation found for '{symbolName}'");
    public static GetSymbolDocsResult Ambiguous(string symbolName, ImmutableList<SymbolCandidate> candidates) => new(
        "ambiguous", symbolName, null, null, null, [], [], [], candidates,
        $"Multiple symbols match '{symbolName}'. Use a fully qualified name to disambiguate.");
}

public sealed record DocParam(string Name, string Description);

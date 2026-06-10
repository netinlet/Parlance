using System.Reflection;
using Parlance.Mcp.Tools;

namespace Parlance.Mcp.Tests.Tools;

public sealed class FindReferencesSnippetTests
{
    [Fact]
    public void FindReferences_HasIncludeSnippetsParameter_DefaultFalse()
    {
        var method = typeof(FindReferencesTool).GetMethods()
            .Single(m => m.GetCustomAttributesData()
                .Any(a => a.AttributeType.Name == "McpServerToolAttribute"));
        var param = method.GetParameters().SingleOrDefault(p => p.Name == "includeSnippets");
        Assert.NotNull(param);
        Assert.True(param!.HasDefaultValue);
        Assert.Equal(false, param.DefaultValue);
    }
}

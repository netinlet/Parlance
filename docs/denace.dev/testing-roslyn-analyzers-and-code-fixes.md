# Testing Roslyn Analyzers and Code Fixes

[Skip to main content](#main-content)

[Hashnode](https://hashnode.com/?utm_source=https%3A%2F%2Fdenace.dev&utm_medium=referral&utm_campaign=blog_header_logo&utm_content=logo)[![The Dev Domain](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676059304472%2F-clIoEy--.png&w=1080&q=75)The Dev Domain](/)

Open search (press Control or Command and K)Toggle themeOpen menu

[Hashnode](https://hashnode.com/?utm_source=https%3A%2F%2Fdenace.dev&utm_medium=referral&utm_campaign=blog_header_logo&utm_content=logo)[![The Dev Domain](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676059304472%2F-clIoEy--.png&w=1080&q=75)The Dev Domain](/)

Open search (press Control or Command and K)

Toggle themeSubscribe[Write](https://hn.new)

## Command Palette

Search for a command to run...

# Testing Roslyn Analyzers and Code Fixes

UpdatedMarch 1, 2023

•

14 min read

![Testing Roslyn Analyzers and Code Fixes](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1677095073165%2Ff158179f-5e0c-4f68-bcb4-3834d852b67e.png&w=3840&q=75)

[![Denis Ekart](https://cdn.hashnode.com/res/hashnode/image/upload/v1674506798653/obIlPL2T5.png?auto=compress,format&format=webp)](https://hashnode.com/@devmenace)

[

Denis Ekart

](https://hashnode.com/@devmenace)

[

Part of seriesExploring Roslyn

](/series/exploring-roslyn)

In the [previous article](https://denace.dev/fixing-mistakes-with-roslyn-code-fixes), we implemented a Roslyn analyzer and code fix. This article will focus on various ways to properly test them. We will leverage existing test libraries and explore the means to write one ourselves using Roslyn workspaces. This will ensure our Roslyn components behave correctly and speed up our future development efforts.

---

## The story so far

We have been calling ourselves self-proclaimed compiler development experts for a few weeks now. During this time, we managed to

-   alienate our colleagues by creating this _really cool_ analyzer that will completely block any and all development efforts if multiple subsequent empty lines are encountered in our codebase,

-   enrage our boss by failing all of our builds and effectively shutting down our product development department, and

-   making a savior move by providing the ability to automatically fix all errors caused by the original analyzer.


And here we are. Our mouse is hovering over the `Merge` button. We are seconds away from either becoming a hero to our company or being sued by the legal department. Suddenly we hear a voice in our head. A long-dead Greek philosopher is speaking to us.

> Quality is not an act. It is a habit.

Oh man, our psychologist warned us about this.

We slowly move the pointer away from the button and take a second to regroup. We make a decision then and there. From now on, we will **test before we ship!**

## Why test at all?

I will not spend too much time defending the various reasons why testing is beneficial in software development. A list of reasons (_pros_, if you will) usually includes

-   ensuring the **quality** of the delivered product,

-   reducing **flaws** and unexpected behavior in the software,

-   making development easier and especially **faster**,

-   ensuring the **stability** of the codebase and allowing developers to refactor code safely (or at least safer), and

-   serving as **living documentation**, the kind that doesn't get outdated over time (at least in cases where tests are used as [quality gates](https://linearb.io/blog/quality-gates/)).


There are, of course, numerous resources available to you on the Wild Wild Web. For starters, navigate to the Microsoft Learn platform and find articles like [this](https://learn.microsoft.com/en-us/visualstudio/test/unit-test-basics?view=vs-2022) or [that](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices).

## Testing Roslyn

I could have skipped the previous chapter and written a general article about testing instead. The thing is, Roslyn is a bit different. Roslyn is a compiler for all intents and purposes (well, for our purpose, at least). And you typically need a compiler to compile your code before you can test it. But we are testing the actions performed by the compiler. See the issue?

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1677322295702/874f1b22-3d39-4b52-8f5c-d8d31206356e.png)

Okay, I'm exaggerating. Lucky for us, _our_ compiler platform is entirely written C#. It is also open, extensible, and accessible enough to think of it as just _any_ other .NET library. That being said, we need to be aware of some new concepts that testing a compiler will introduce.

Analyzers, code fixes, and other Roslyn extensions are typically encountered in **projects**. These reside in **solutions** open in various **workspaces** such as your Visual Studio IDE, JetBrains Rider, VS Code, or even the dotnet CLI. Finally, the actual diagnostic messages usually relate to a particular **document** in your project, such as the `Program.cs` application entry point.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1677325369193/cef2c7d1-b7d3-4de4-940c-515104b9e6bb.gif)

To effectively test the compiler, we need to recreate all of these components, states, and actions for each of our unit tests.

## Starting from scratch

Before we start, I want to assure you there are already several libraries and tools that can get you started with unit testing. If you are just looking for that initial push, jump to the next chapter where we go over the existing libraries used for testing.

If you are curious to learn how Roslyn works _under the covers_, this section will focus on building a straightforward and transparent means of testing Roslyn from the ground up.

Writing unit tests for our particular use case should be the same as what we usually do (and we do write unit tests, right?!).

Let's open up the analyzer solution we implemented in the previous articles and add a test project.

```bash
 dotnet new nunit -f net7.0 -n RoslynTests
```

Head over to our \`RoslynTests.csproj\` project file next and add a reference to our analyzer and code fix project. The project file should now look something like this.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\EmptyLinesAnalyzerAndCodeFix\EmptyLinesAnalyzerAndCodeFix.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.3.2" />
    <PackageReference Include="NUnit" Version="3.13.3" />
    <!-- ... -->
  </ItemGroup>
</Project>
```

We have an [NUnit](https://nunit.org/) testing project ready to run. Of course, you could easily use some other testing framework, such as [XUnit](https://xunit.net/) or [MSTest](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest).

> All actions performed in this article are done using the CLI or directly implemented in the code. Depending on which IDE you use, there may be a more convenient way of achieving the same things. The CLI approach, at least, will guarantee that the actions performed will work regardless of the platform or IDE being used.

Next, we need a NuGet package that will enable us to build virtual solutions. And, of course, let's remember our trusty [FluentAssertions](https://fluentassertions.com/) package. Run the following scripts.

```bash
dotnet add RoslynTests package Microsoft.CodeAnalysis.CSharp.Features
dotnet add RoslynTests package FluentAssertions
```

Essentially, we are writing unit tests for how the compiler treats our source code. Let's start with wrapping some code in the `SourceText` abstraction available from `Microsoft.CodeAnalysis.Text` namespace.

```bash
var source = SourceText.From("""
            namespace DemoConsoleApp;

            /// <summary> </summary>
            public class EmptyLines
            {
            }
            """);
```

> For our benefit, we are using the [raw string literal](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-11#raw-string-literals) notation introduced with C# 11

Okay, so the `source` construct will be the unit we are testing. The first thing we need is to create a workspace.

### Create a virtual workspace

As we learned in the [first article](https://denace.dev/exploring-roslyn-net-compiler-platform-sdk) of the series, the workspaces API acts as an entry point into our application. It exists to help us organize all the information about our source code. It ties everything into a neatly packed object model. It offers us direct access to the compiler layer and all the syntax and semantic information the compiler has. To put these words into a picture,

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1677337722338/af4fb232-627e-4794-888b-45cfa577b55d.png)

this is what we need to build. Only the entire workspace must exist for the short duration of running a single unit test.

When you see a solution like the one pictured above, it is usually built from a [`MSBuildWorkspace`](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/Core/MSBuild/MSBuild/MSBuildWorkspace.cs) or one of its siblings such as [`VisualStudioWorkspace`](https://github.com/dotnet/roslyn/blob/main/src/VisualStudio/Core/Impl/RoslynVisualStudioWorkspace.cs) . While the used workspace type is generally specific to the workload or your development environment, these workspaces usually have at least one thing in common. They allow you, the developer, to point to a folder, a `csproj`, or a `sln` file and automatically load the entire workspace for you.

There is, however, another workspace `AdhocWorkspace` available in the NuGet package we installed just moments before. To quote the authors, it is _"a workspace that allows full manipulation of projects and documents but does not persist changes"_. Perfect. This is precisely what we need to build our virtual workspace.

Let's start by creating a `static` class named `RoslynTestExtensions` and, in it, an extension method that will be responsible for creating the workspace. We are modeling a solution that will contain a single project. That project will include a single document, our `source`.

We will also need to provide our solution with some references. These allow us to access types and functionalities from external assemblies. Referencing `System` and `System.Linq` will do just fine for our use case.

```csharp
private static readonly MetadataReference[] CommonReferences =
{
    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
};
```

Let's also define a couple of default values. We know our solution will always contain a single project. This project will also have a single document containing our syntax.

```csharp
private const string DefaultProjectName = "ProjectUnderTest.csproj";
private const string DefaultDocumentName = "SourceUnderTest.cs";
```

Now we have enough information to compile a simple workspace.

```csharp
private static AdhocWorkspace CreateWorkspace(this SourceText source)
{
    var projectId = ProjectId.CreateNewId();
    var documentId = DocumentId.CreateNewId(projectId, DefaultDocumentName);

    var sourceTextLoader = TextLoader.From(
        TextAndVersion.Create(source, VersionStamp.Create()));
    var document = DocumentInfo
        .Create(documentId, DefaultDocumentName)
        .WithTextLoader(sourceTextLoader);

    var project = ProjectInfo.Create(
        id: projectId,
        version: VersionStamp.Create(),
        name: DefaultProjectName,
        assemblyName: DefaultProjectName,
        language: LanguageNames.CSharp)
        .WithCompilationOptions(new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary));

    var workspace = new AdhocWorkspace();
    var updatedSolution = workspace
        .CurrentSolution
        .AddProject(project)
        .AddMetadataReferences(projectId, CommonReferences)
        .AddDocument(document);

    workspace.TryApplyChanges(updatedSolution);

    return workspace;
}
```

A couple of things to note here.

-   **_one:_** Our project will be compiled as a dynamically linked library. This allows us to not worry about the compilation failing when our `source` does not contain a valid `static void Main(...)` application entry point.

-   **_and two:_** If you look closely at the code, you can also notice that our `source` code, `document` `project`, and `solution` appear immutable. Looking back to what we discussed in the first article of the series, most of the basic Roslyn building blocks are, in fact, immutable. With one exception. A workspace provides access to all of the abovementioned building blocks. It is designed to change over time by supporting live interactions from the environment or via a call to the [`workspace.TryApplyChanges(updatedSolution)`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.workspace.tryapplychanges?view=roslyn-dotnet-4.3.0#microsoft-codeanalysis-workspace-tryapplychanges\(microsoft-codeanalysis-solution\)).


### Find the diagnostic

Now that we have a solution, we can manipulate it and attempt to produce an analyzer diagnostic we are trying to test. Starting from the `source` text, we can create another extension method.

The extension method will return a collection of diagnostics in the `source` code. More specifically, only the diagnostics that the analyzer `TAnalyzer` is reporting. Using the standard Roslyn APIs, that is quite easy to achieve.

```csharp
static async Task<ImmutableArray<Diagnostic>> GetDiagnostics<TAnalyzer>(this SourceText source) where TAnalyzer : DiagnosticAnalyzer, new()
{
    var workspace = source.CreateWorkspace();
    var document = /*the default document*/
    var analyzer = new TAnalyzer();
    var diagnosticDescriptor = analyzer.SupportedDiagnostics.Single();
    var compilation = await /*the default project*/.GetCompilationWithAnalyzerAsync(analyzer);
    var allDiagnostics = await compilation.GetAllDiagnosticsAsync();

    return allDiagnostics.Where(x =>
            x.Id == diagnosticDescriptor.Id &&
            x.Location.SourceTree?.FilePath == document.Name)
        .ToImmutableArray();
}
```

> I will let you fill in the _blanks_. Feel free to check the `roslyn-playground` repository (_link at the end of this article_) if you get stuck anywhere.

We can now finally create a simple unit test to wrap everything up.

```csharp
[Test]
public async Task EmptyLinesAnalyzer_ShouldReportDiagnostic_WhenMultipleEmptyLinesExist()
{
    // Arrange
    var source = /*we know this already*/

    // Act
    var actual = await source.GetDiagnostics<EmptyLinesAnalyzer>();

    // Assert
    actual.ShouldContainDiagnosticWithId("DM0001");
}
```

Ahh, one bit is missing. Let's create another extension method `ShouldContainDiagnosticWithId` . It should receive an `ImmutableArray` of `Diagnostic`s, and properly assert the existence of a diagnostic with a provided `Id`.

```csharp
static void ShouldContainDiagnosticWithId(this ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
{
    diagnostics.Should().NotBeNull().And.HaveCountGreaterOrEqualTo(1);
    diagnostics.Should().Contain(diagnostic => diagnostic.Id == diagnosticId);
}
```

✅ Let's move on!

### Apply the code fix

We can use a similar approach for testing our code fix. Since we know our diagnostic contains an accompanying code fix, it gives us a perfect place to start modifying the virtual solution.

Similarly to what we did when testing the diagnostic in the previous chapter, we need to find the diagnostic to fix. Let's assume we are left with a `singleDiagnostic` that was reported by the analyzer under test. Because we checked that the diagnostic has the correct id, we also know that our `TCodeFixProvider` supports a code action to fix it.

I suppose we are making way too many assumptions at this point. Naturally, our code will have proper validation in place so that nothing will be left to chance 😉.

```csharp
static async Task<SourceText> ApplyCodeFixes<TAnalyzer, TCodeFixProvider>(this SourceText source)
where TAnalyzer : DiagnosticAnalyzer, new()
where TCodeFixProvider : CodeFixProvider, new()
{
    // create the workspace, find the diagnostic, and ...
    await workspace.ApplyCodeFix<TCodeFixProvider>(document, singleDiagnostic);

     return /*the modified source*/
}
```

The only thing left to do is to implement the `ApplyCodeFix<TCodeFixProvider>` extension method.

If you remember [the article](https://denace.dev/fixing-mistakes-with-roslyn-code-fixes) where we implemented the code fix provider, we needed to implement the `CodeFixProvider` abstract class. As a part of that implementation, we implemented the `RegisterCodeFixesAsync(CodeFixContext)` method. This will be our access point to the code fix provider.

We can surely create an instance of the `CodeFixContext` since we have all the needed building blocks. A point of interest for us is the `registerCodeFix` argument. This delegate gets invoked whenever the code fix encounters a fixable diagnostic. As a result, we are left with a `CodeAction`.

This is something we are already familiar with. Remember, a code action contains all the information needed to apply the fix to our solution. This is achieved through the set of operations it exposes. Calling `codeAction.GetOperationsAsync` will enable us to access the `ApplyChangesOperation`, which will contain a `ChangedSolution` . This is the fix we need. The only thing left is to apply that changed solution to our workspace.

💡_I, for one, love it when things start falling in_to _place._

Let's type this up real quick.

```csharp
static async Task ApplyCodeFix<TCodeFixProvider>(this AdhocWorkspace workspace, Document document, Diagnostic singleDiagnostic)
where TCodeFixProvider : CodeFixProvider, new()
{
    var codeFixProvider = new TCodeFixProvider();
    List<CodeAction> actions = new();
    var context = new CodeFixContext(document,
        singleDiagnostic,
        (a, _) => actions.Add(a),
        CancellationToken.None);
    await codeFixProvider.RegisterCodeFixesAsync(context);
    foreach (var codeAction in actions)
    {
        var operations = await codeAction.GetOperationsAsync(CancellationToken.None);
        if (operations.IsDefaultOrEmpty)
        {
            continue;
        }

        var changedSolution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        workspace.TryApplyChanges(changedSolution);
    }
}
```

Again, we have everything we need in place to finally write the unit test. The following snippet is self-explanatory.

```csharp
[Test]
public async Task EmptyLinesCodeFix_ShouldApplyFix_WhenMultipleEmptyLinesExist()
{
    // Arrange
    var source = SourceText.From("""
        namespace DemoConsoleApp;

        public class EmptyLines
        { }
        """);
    var expected = SourceText.From("""
        namespace DemoConsoleApp;

        public class EmptyLines
        { }
        """);

    // Act
    var actual = await source.ApplyCodeFixes<EmptyLinesAnalyzer, EmptyLinesCodeFix>();

    // Assert
    actual.ShouldBeEqualTo(expected);
}
```

✅ Pass. We are done.

Well, not quite. As mentioned, the approach we just implemented was meant to demonstrate how Roslyn works and should not be considered a valid approach to test your analyzers and code fixes (please). Let's look at how testing should actually be attempted.

## Using Roslyn's testing library

Luckily for us, we can skip the previous chapter altogether. The team behind Roslyn provides a comprehensive suite of utilities designed to test analyzers, code fixes, and other components.

Getting started here is easy. Make sure you install the library first.

```bash
dotnet add RoslynTests package Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.NUnit
```

Keep in mind that similar packages also exist for [XUnit](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.XUnit) and [MSTest](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.MSTest).

The only thing we are left with is to use a _verifier_ for the code fix we want to test. We can be tricky here and create an [alias](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/using-directive#using-alias) for the verifier.

```csharp
using EmptyLinesAnalyzerAndFix;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTests;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.NUnit.CodeFixVerifier<EmptyLinesAnalyzer, EmptyLinesCodeFix>;

public class EmptyLinesTests
{
    // ...
}
```

The previous test can now be rewritten to use the `CodeFixVerifier` like so.

```csharp
[Test]
public async Task EmptyLinesCodeFix_ShouldApplyFix_WhenMultipleEmptyLinesExist()
{
    // Arrange
    var source = /*...*/
    var expected = /*...*/
    var diagnostic = Verify.Diagnostic()
        .WithSpan(startLine: 2, startColumn: 1, endLine: 4, endColumn: 1)
        .WithSpan(startLine: 4, startColumn: 1, endLine: 4, endColumn: 7);

    // Assert
    await Verify.VerifyCodeFixAsync(source, diagnostic, expected);
}
```

Two things to note. The `Verify` alias already _knows_ the diagnostic we are testing. The only thing we are left with is to define where the diagnostic is supposed to occur. The `WithSpan` extension, along with [numerous other extensions](https://github.com/dotnet/roslyn-sdk/blob/main/src/Microsoft.CodeAnalysis.Testing/README.md#verifier-overview), can be used to specify the constraints of the diagnostic we are testing.

That's all good, but aren't we supposed to get a single diagnostic in this test? Why are we specifying two locations, then?

Looking back at [the article](https://denace.dev/getting-started-with-roslyn-analyzers) where we implemented the analyzer, we added an `additionalLocation` that was later used by the code fix provider. This is the second span we want to test for.

Calling the `Verify.VerifyCodeFixAsync` will cycle over numerous verifications that will generally go over the same steps we went over in the previous chapter. Only the verifications executed here are far more precise and elaborate. The [documentation](https://github.com/dotnet/roslyn-sdk/blob/main/src/Microsoft.CodeAnalysis.Testing/README.md) in the Roslyn repository is a great entry point if you wish to explore the numerous capabilities of the testing library further.

> We intentionally skipped testing the analyzer, since the library also provides the same assertions when testing an accompanying code fix provider. General guidelines and [best practices](https://github.com/dotnet/roslyn-sdk/blob/main/src/Microsoft.CodeAnalysis.Testing/README.md#best-practices) for using the Roslyn test library suggest testing only the code fix provider when possible.
>
> If this is not possible, you can freely use the `AnalyzerVerifier` from the [Analyzer NuGet package](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.NUnit), to test the analyzer on its own.

But wait, there's more.

### Streamline testing using the Roslyn testing library

There are various cool and somewhat undocumented features included in this testing library. A couple of them, at least, can significantly streamline your test development.

The testing library supports a special markup syntax to aid in your test development.

Let's take the test of our code fix provider (the one above). Instead of explicitly creating a `Diagnostic()` with expected spans, we can embed that information into the source code.

```csharp
var source = """
    namespace DemoConsoleApp;
    {|DM0001:

    |}public class EmptyLines
    { }
    """;
```

Notice the `{|DM0001: ... |}` markup, which tells the verifier that there is an expected diagnostic with the id `DM0001` supposed to appear in the same position as denoted by the brackets.

The assertion part can, therefore, also be simplified since the `diagnostic` was implicitly created for us by the library.

```csharp
await Verify.VerifyCodeFixAsync(source, expected);
```

We can use similar syntax to specify the location of _any_ reported diagnostic using an alternative bracket markup `[| ... |]` .

```csharp
[Test]
public async Task EmptyLinesAnalyzer_ShouldReportDiagnostic_WhenMultipleEmptyLinesExist_UsingRoslynLibraryCustomSyntax()
{
    // Arrange
    var source = """
        namespace DemoConsoleApp;
        [|

        |]public class EmptyLines
        { }
        """;

    // Assert
    await Verify.VerifyAnalyzerAsync(source);
}
```

Pretty cool, right?! Now replace the above markup with a marker `$$` that specifies the expected location of a diagnostic. We have another passing test ✅.

```csharp
var source = """
    namespace DemoConsoleApp;
    $$

    public class EmptyLines
    { }
    """;
```

## In summary

We managed to get our hands dirty with Roslyn's workspaces. This API gave us enough insight into code analysis in C#. Additionally, we learned how to properly and easily test our inventions.

If you made it this far, head over to the [**denisekart/exploring-roslyn**](https://github.com/denisekart/exploring-roslyn) repository and start exploring⭐.

---

... As the sun is starting to rise, we glance at our updated merge request. Soothing green light from the generated code coverage report shines blissfully in our eyes. As we move our mouse closer to the `Merge` button, we wonder. _What other secrets lie beneath this compiler's shining API?_

Until next time, ✌

[#roslyn](/tag/roslyn)[#csharp](/tag/csharp)[#dotnet](/tag/dotnet)[#metaprogramming](/tag/metaprogramming)[#programming](/tag/programming)

## More from this blog

[

### Using Razor Outside of Web

Repurposing the Razor Templating Engine to Generate HTML in a Console Application

Feb 1, 2024·7 min read

![Using Razor Outside of Web](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fstock%2Funsplash%2Fp9OkL4yW3C8%2Fupload%2F0aca45762bc60212f5e7e3c0e88ad269.jpeg&w=3840&q=75)

](/using-razor-outside-of-web)

Subscribe to the newsletter

Get new posts delivered to your inbox.

Subscribe

[

### Fixing Mistakes With Roslyn Code Fixes

A Guide to Spending an Entire Afternoon Fixing a Single Line of Code with Roslyn

Feb 14, 2023·7 min read

![Fixing Mistakes With Roslyn Code Fixes](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676225794750%2F65ffcb79-9925-4d22-9884-e0639b0f226e.png&w=3840&q=75)

](/fixing-mistakes-with-roslyn-code-fixes)

[

### Getting Started With Roslyn Analyzers

A Hands-On Guide to Building and Understanding Roslyn Analyzers

Feb 5, 2023·10 min read

![Getting Started With Roslyn Analyzers](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1675286191591%2F63279503-a0d4-4143-a877-a94307dd6a9d.png&w=3840&q=75)

](/getting-started-with-roslyn-analyzers)

[

### Exploring Roslyn .NET Compiler Platform SDK

Making developers lives easier by utilizing an open compiler platform

Jan 25, 2023·5 min read

![Exploring Roslyn .NET Compiler Platform SDK](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1674686378484%2F4db51c58-5b7f-496c-aca2-970c1c03f562.png&w=3840&q=75)

](/exploring-roslyn-net-compiler-platform-sdk)

![Publication avatar](https://cdn.hashnode.com/res/hashnode/image/upload/v1676059212984/tdWOpBmgz.png?auto=compress,format&format=webp)

The Dev Domain

5 posts published

I'm a highly driven software engineer with experience in developing performant cloud-native solutions in .NET. The Future is Written in Code.

[](https://twitter.com/EkartDenis)[](https://instagram.com/EkartDenis)[](https://github.com/denisekart)[](https://www.linkedin.com/in/denis-ekart/)[](https://hashnode.com/@devmenace)


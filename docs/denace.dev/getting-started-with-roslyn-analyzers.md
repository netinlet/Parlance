# Getting Started With Roslyn Analyzers

[Skip to main content](#main-content)

[Hashnode](https://hashnode.com/?utm_source=https%3A%2F%2Fdenace.dev&utm_medium=referral&utm_campaign=blog_header_logo&utm_content=logo)[![The Dev Domain](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676059304472%2F-clIoEy--.png&w=1080&q=75)The Dev Domain](/)

Open search (press Control or Command and K)Toggle themeOpen menu

[Hashnode](https://hashnode.com/?utm_source=https%3A%2F%2Fdenace.dev&utm_medium=referral&utm_campaign=blog_header_logo&utm_content=logo)[![The Dev Domain](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676059304472%2F-clIoEy--.png&w=1080&q=75)The Dev Domain](/)

Open search (press Control or Command and K)

Toggle themeSubscribe[Write](https://hn.new)

## Command Palette

Search for a command to run...

# Getting Started With Roslyn Analyzers

A Hands-On Guide to Building and Understanding Roslyn Analyzers

UpdatedFebruary 5, 2023

•

10 min read

![Getting Started With Roslyn Analyzers](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1675286191591%2F63279503-a0d4-4143-a877-a94307dd6a9d.png&w=3840&q=75)

[![Denis Ekart](https://cdn.hashnode.com/res/hashnode/image/upload/v1674506798653/obIlPL2T5.png?auto=compress,format&format=webp)](https://hashnode.com/@devmenace)

[

Denis Ekart

](https://hashnode.com/@devmenace)

[

Part of seriesExploring Roslyn

](/series/exploring-roslyn)

This article will focus on the most basic setup needed to create and use a Roslyn analyzer. Starting with an empty solution, we will go through the necessary configuration to develop and utilize a Roslyn analyzer.

---

The [last article](https://denace.dev/exploring-roslyn-net-compiler-platform-sdk) ended with the words, "_let's write some code._" While I'm all for writing code, perhaps some purpose is needed first. One of the goals of an open compiler platform is, for sure, the ability to extend and modify it to solve your problems. I have problems (yikes!).

### First, define the problem

While this makes complete sense when written by a stranger on a blog post, it is always good to be reminded that to solve a problem, you must first know what the problem is (duh, get on with it already).

Let's say you get employed at a cool, trending company with a product wholly written in the latest flavor of C#. It's your first day on the job with your brand new 14" Mac, and a colleague gives you a link to the codebase you will be working on. You Set everything up, open up your favorite code editor, and load `Program.cs` . You see this.

```csharp
// this is the entrypoint to the CoolCorp application

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
```

And then the screen ends. There is no more vertical space on your brand-new MacBook. It has been completely eaten up by empty lines and, _gulp_, whitespace!

Now, just forget about the handy scrolling capabilities of that state-of-the-art haptic feedback trackpad thingy. You hate that you can only see 4 relevant lines of code on your screen. It hurts, and you want to... No, you need to fix it now!

The goal is to quickly turn the above monster into something more concise, clean, and structured, like so.

```csharp
// this is the entrypoint to the CoolCorp application

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();

app.MapDefaultControllerRoute();
app.MapRazorPages();

app.Run();
```

**Bonus**, you wish to punish the people writing such code, so you also want to make sure the build fails if there are multiple sequential empty lines anywhere in the solution.

Great, we have a problem to solve. To be entirely honest, you could just browse the Roslyn documentation and find the diagnostic with ID `IDE2000`. Then set the severity to `error` and grab a cold beverage with one of your normal non-developer friends who don't enjoy torturing themselves with coding abstractions at 3 AM on Saturday.

No? No! Let's dive in!

### Set up the environment - take one

There are several ways to start developing Roslyn extensions. A straightforward way to start is to open up your latest Visual Studio 2022, find the **_Analyzer with Code Fix (.NET Standard)_** template, and dive into it.

Of course, you must ensure that you have installed the necessary workloads first (hint, hint **_Visual Studio extension development_** workload with the optional **_.NET Compiler Platform SDK_** ☑ component).

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1675362024104/eb1a910e-1c30-42dd-b66c-d729996bd2ec.png)

Once you create a project, you are greeted with an example analyzer and code fix, including the NuGet or a VSIX Visual Studio extension boilerplate code.

Project setup, as well as the means of distribution, are all essential things to consider, but we will dive into that topic at a later time. Right now, we need to get up and running ASAP. Your boss is watching, and the haptic feedback trackpad is about to give. To get up and running fast, we are going to start from scratch, literally (I know, I know... bear with me).

### Set up the environment - take two

Okay, let's start with an empty solution. I'll let you figure out the best way to create it. My preference is always `dotnet new sln`.

#### Create a basic console application

While waiting on your Mac to load the entire CoolCorp solution, let's create a simple demo console app `dotnet new console -n DemoConsoleApp`. We will use this console application to quickly get feedback on our analyzer (_no, you shouldn't use a console application to test your work_).

> Side note; if you are new to `dotnet` and are still not impressed by the advances made in the last couple of years, just run `dotnet run --project DemoConsoleApp`, and you have a working console application ready to run in a single step. Mind you, the only file in the project has a single line of syntax written in it.
>
> ```plaintext
> Hello, World!
> ```

#### Create the analyzer project outline

Create a new class library project and name it something cool like `NewLinesAreRelevantSyntaxTooAnalyzer.csproj` (or don't, please). Roslyn components should target the netstandard2.0 TFM to ensure that analyzers load in various runtimes available today (mono, .NET Framework, and .NET Core).

Since we are already adding new functionalities to our solution, let's reference the packages needed to create an analyzer.

```bash
dotnet new classlib -f netstandard2.0 --langVersion latest -n EmptyLinesAnalyzerAndCodeFix
dotnet add EmptyLinesAnalyzerAndCodeFix package Microsoft.CodeAnalysis.CSharp
dotnet add EmptyLinesAnalyzerAndCodeFix package Microsoft.CodeAnalysis.Analyzers
```

If everything worked out, the structure of the solution should look like this.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1675372780805/7871063b-17e2-4e2f-82cd-4dd3447f0709.png)

There is now only a single piece of the puzzle missing. We need to reference the analyzer in our _DemoConsoleApp._

To do that, open the `DemoConsoleApp.csproj`, and create an `<ItemGroup>` that adds a project reference to the analyzer. The file should look something like this.

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net7.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="..\EmptyLinesAnalyzerAndCodeFix\EmptyLinesAnalyzerAndCodeFix.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="All"/>
    </ItemGroup>
</Project>
```

What we just did was referenced our analyzer from the console project. We then instructed the compiler that the referenced project should be used as a part of the project analysis. Additionally, we told the compiler that our console application will not be referencing the outputs of the analyzer (but we still need it to be built beforehand). If you wish to learn about other specifics of referencing projects, have a look at [common MSBuild project items](https://learn.microsoft.com/en-us/visualstudio/msbuild/common-msbuild-project-items?view=vs-2022) and [how to control dependency assets](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#controlling-dependency-assets).

We just achieved the ability to consume the analyzer from the same solution that the analyzer is being developed in (just hit _rebuild - and read up on the wonders of_ [_MEF parts_](https://learn.microsoft.com/en-us/dotnet/framework/mef/) _and_ [dynamic recomposition](https://github.com/microsoft/vs-mef/blob/main/doc/dynamic_recomposition.md) if you ever need to reload the solution and have no idea why).

Let's take the analyzer for a spin!

### A basic diagnostic analyzer

Head over to our analyzer class library and create a new class named `EmptyLinesAnalyzer.cs`. There are a couple of namespaces we will be using. Make sure that the following are included.

```csharp
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
```

Since we are positive we have no idea what VisualBasic even looks like, we will add an attribute specifying that the created class will be a `[DiagnosticAnalyzer(LanguageNames.CSharp)]`.

Next, we need is to describe what the diagnostic we are producing will report. We need to instantiate a very simple `DiagnosticDescriptor` (the one from the `Microsoft.CodeAnalysis` namespace) to report the relevant information to the developer using our analyzer.

```csharp
// ...
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EmptyLinesAnalyzer : DiagnosticAnalyzer
{
    internal const string DiagnosticId = "DM0001";
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: DiagnosticId,
        title: "I work!",
        messageFormat: "I Work!",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "I work!");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        //...
    }
}
```

If you recall from the [first post](https://denace.dev/exploring-roslyn-net-compiler-platform-sdk) in this series, analyzers can interact with several stages of compilation. Our use case will involve only syntax analysis. The diagnostics, however, will always point to a location in the analyzed syntax.

I would invite you to read up on [syntax analysis](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis), but for now, just imagine our console application has a single syntax tree (the contents of `Program.cs` ). That tree represents every keyword, variable, expression, comment, and every other character within this file.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1675376528921/2f951b04-75ce-4806-b635-e4fad86c0d08.gif)

Each token in that syntax tree has a span. The character index at which the node begins and its length. The most simple job of an analyzer is to find the location of a code issue (the hard part) and report it to the compiler (the easy part).

Let's do the easy part first.

```csharp
public override void Initialize(AnalysisContext context)
{
    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    context.RegisterSyntaxTreeAction(context =>
    {
        var location = Location.Create(context.Tree, TextSpan.FromBounds(0, context.Tree.Length));
        var diagnostic = Diagnostic.Create(Rule, location);
        context.ReportDiagnostic(diagnostic);
    });
}
```

We just instructed the compiler to report an error for any syntax tree encountered and that the _error_ should span the entire document.

> Side note; you would not believe how many autogenerated files exist in a single-line console application. `AssemblyInfo`, `AssemblyAttributes`, `GlobalUsings`, you name it. We can ensure this code is not analyzed by setting the `GeneratedCodeAnalysisFlags` to `None`.

Okay, if all the semicolons are in place, rebuilding the solution will most certainly produce an error.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1675377661953/682f58a4-5f50-468f-be95-2288ba59003d.png)

Now try to remember the last time you were happy that your code didn't work?!

Anyway, we have a real problem to solve now...

### Implement `empty lines analyzer` logic

Since solving this problem was not intended to be a part of the article (_what?!_), I will try to leave you with some hints on how to attempt this yourself. If, however, you feel like the extra tinker time is not worth it, I will include a link to a fully working analyzer at the bottom (_don't you dare touch that haptic feedback trackpad now!_).

To try and solve this problem, we first need to know what parts of the syntax tree the empty lines usually occur. The fastest way to see this is by inspecting the syntax tree of the program code. There is handy tooling already available that allows you to traverse a live syntax tree either in [Visual Studio](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/syntax-visualizer?tabs=csharp#syntax-visualizer), [Rider](https://plugins.jetbrains.com/plugin/16902-rossynt), or even [online](https://roslynquoter.azurewebsites.net/).

Most of the empty lines are a part of [syntax trivia](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis#understanding-syntax-trees). Trivia is a special kind of token found in the syntax tree. It contains syntactically insignificant parts of your code, such as comments, preprocessing directives, whitespaces, and, _bingo_, new lines.

One way to start is to traverse all leaf tokens in a syntax tree. Once a token with leading trivia is encountered, we need to verify if it is structured so that it contains multiple subsequent empty lines. If found, we need to report the location to the compiler (and we already know how to do that).

> Side note; we should define what multiple empty lines truly mean. Are these just sequential `\n` characters? How about platform-specific line terminators such as `\r\n` ? Do we ignore the `whitespace` and `\t` characters between new lines?

One way we could implement the above solution is to analyze each observed syntax tree in the following way.

```csharp
private void Recurse(SyntaxTreeAnalysisContext context, SyntaxNode node)
{
    foreach (var nodeOrToken in node.ChildNodesAndTokens())
    {
        if (nodeOrToken.IsNode)
            Recurse(context, nodeOrToken.AsNode());
        else if (nodeOrToken.IsToken)
            AnalyzeToken(context, nodeOrToken.AsToken());
    }
}

private void AnalyzeToken(SyntaxTreeAnalysisContext context, SyntaxToken token)
{
    if (!token.HasLeadingTrivia)
        return;
    // ...
}
```

After finding and reporting all locations at which multiple subsequent empty lines occur, the analyzer will ensure that the compilation process encounters an exception and report the diagnostic we created.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1675547139200/c4a400c1-1813-4447-87ba-21b7a898c624.gif)

### One more thing

You might notice that your IDE will begin to issue warnings that _a project containing analyzers or source generators should specify the property_ `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>`. The compiler platform comes equipped with a [plethora of analyzers](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/Microsoft.CodeAnalysis.Analyzers.md) intended to help with developing and extending the Roslyn compiler.

When the abovementioned property is set in your `EmptyLinesAnalyzerAndCodeFix.csproj`, these analyzers start analyzing your analyzer and offer valuable guidance on properly handling common scenarios when developing for Roslyn.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1675597191254/e072cf3a-8fd6-4b34-b9b1-615eb9d54a69.png)

### To sum up

If you managed to follow through to the end, you should have a working analyzer that does a couple of really interesting things.

-   It is capable of identifying locations that are basically empty space,

-   it can fail the build when such locations are found within your codebase and offer information on where to find them, and

-   is fully equipped to annoy your coworkers to no avail.


That may present a challenge but worry not. In [the following article](https://denace.dev/fixing-mistakes-with-roslyn-code-fixes), we will create an accompanying code fix that can fix issues reported by our empty lines analyzer.

And, as promised, check out the [denisekart/exploring-roslyn](https://github.com/denisekart/exploring-roslyn) repository, where you can find all the samples from this article (and more).

Until next time, ✌

[#roslyn](/tag/roslyn)[#csharp](/tag/csharp)[#dotnet](/tag/dotnet)[#metaprogramming](/tag/metaprogramming)[#diagnostics](/tag/diagnostics)

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

### Testing Roslyn Analyzers and Code Fixes

In the previous article, we implemented a Roslyn analyzer and code fix. This article will focus on various ways to properly test them. We will leverage existing test libraries and explore the means to write one ourselves using Roslyn workspaces. This...

Mar 1, 2023·14 min read

![Testing Roslyn Analyzers and Code Fixes](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1677095073165%2Ff158179f-5e0c-4f68-bcb4-3834d852b67e.png&w=3840&q=75)

](/testing-roslyn-analyzers-and-code-fixes)

[

### Fixing Mistakes With Roslyn Code Fixes

A Guide to Spending an Entire Afternoon Fixing a Single Line of Code with Roslyn

Feb 14, 2023·7 min read

![Fixing Mistakes With Roslyn Code Fixes](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676225794750%2F65ffcb79-9925-4d22-9884-e0639b0f226e.png&w=3840&q=75)

](/fixing-mistakes-with-roslyn-code-fixes)

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


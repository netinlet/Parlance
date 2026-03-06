# Fixing Mistakes With Roslyn Code Fixes

[Skip to main content](#main-content)

[Hashnode](https://hashnode.com/?utm_source=https%3A%2F%2Fdenace.dev&utm_medium=referral&utm_campaign=blog_header_logo&utm_content=logo)[![The Dev Domain](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676059304472%2F-clIoEy--.png&w=1080&q=75)The Dev Domain](/)

Open search (press Control or Command and K)Toggle themeOpen menu

[Hashnode](https://hashnode.com/?utm_source=https%3A%2F%2Fdenace.dev&utm_medium=referral&utm_campaign=blog_header_logo&utm_content=logo)[![The Dev Domain](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676059304472%2F-clIoEy--.png&w=1080&q=75)The Dev Domain](/)

Open search (press Control or Command and K)

Toggle themeSubscribe[Write](https://hn.new)

## Command Palette

Search for a command to run...

# Fixing Mistakes With Roslyn Code Fixes

A Guide to Spending an Entire Afternoon Fixing a Single Line of Code with Roslyn

UpdatedFebruary 14, 2023

•

7 min read

![Fixing Mistakes With Roslyn Code Fixes](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676225794750%2F65ffcb79-9925-4d22-9884-e0639b0f226e.png&w=3840&q=75)

[![Denis Ekart](https://cdn.hashnode.com/res/hashnode/image/upload/v1674506798653/obIlPL2T5.png?auto=compress,format&format=webp)](https://hashnode.com/@devmenace)

[

Denis Ekart

](https://hashnode.com/@devmenace)

[

Part of seriesExploring Roslyn

](/series/exploring-roslyn)

In this part of the series, we will focus on creating a code fix provider that will fix a diagnostic we reported with _empty lines analyzer_, a Roslyn analyzer we implemented in the [previous article](https://denace.dev/getting-started-with-roslyn-analyzers).

---

#### Storytime!

A week ago, we had a brilliant idea to implement an analyzer that would fail the entire build whenever multiple empty lines were encountered in our solution. Of course, someone made you push the code we implemented into production (hey, don't look at me). It just so happens that your company, CoolCorp, has ground to a halt because of that.

You get called into a web meeting titled "_Fw: URGENT Who the hell broke our builds?!!"_. You enter the chat unbeknownst to the amount of trouble your Roslyn learning journey has caused the company. Your tech lead is bisecting the git history in front of senior management, trying to find the commit that broke everything. Ahh, here it is `feat: disallow multiple subsequent empty lines (that will teach them)`.

The commit author, you, is now known to everyone. As you unmute your mic, you angle your web camera slightly to the right to reveal the famous quote from a guy that has something to do with faces, or books, or lizards.

!["Move Fast and Break Things" by rossbelmont is licensed under CC BY-NC-SA 2.0. To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-sa/2.0/?ref=openverse.](https://cdn.hashnode.com/res/hashnode/image/upload/v1676225530211/2659c347-028f-420a-8be0-0f64fbe0c3d7.jpeg)

You speak up with confidence: **I can fix it!**

### Enter code fixes

Roslyn allows you to write a code fix provider, that can enable the IDE to calculate a change to the solution that would fix the reported diagnostic. Let's break this down.

An analyzer will report a diagnostic with a unique id (e.g. `DM0001`). A code fix provider can register a _code action_ capable of providing a fix for the issue from that diagnostic. This means that any time there is an _error_, _warning_, or even an _informational_ squiggle in your IDE, chances are there is an accompanying code fix that can make that squiggle go away.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1676234599557/17d7023c-1b47-45e4-a664-6ae68bc3c2fb.gif)

The code fix provider will take the existing solution, make the necessary syntax changes, and return a new, fixed solution.

> Side note; you must follow several rules when implementing analyzers or code fixes for Roslyn. The diagnostic id needs to be unique across all analyzers and be in a specified format, must not be null, must have the correct category, ...
>
> When developing a for Roslyn, there are several [helpful analyzers and code fixes](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/Microsoft.CodeAnalysis.Analyzers.md) to aid in the development.

### Writing a code fix provider

Starting with our existing analyzer project `EmptyLinesAnalyzerAndCodeFix.csproj`, let's create a new class and call it `EmptyLinesCodeFix`.

Before doing anything else, we need another NuGet package to help us manipulate common workspace items such as [Documents](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.document?view=roslyn-dotnet-3.2). Head over to the CLI and run the following command.

```bash
dotnet add EmptyLinesAnalyzerAndCodeFix package Microsoft.CodeAnalysis.Workspaces.Common
```

With proper dependencies installed, the class can derive from `Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider` abstract class. For the IDE to recognize that this is a code fix provider, we must also decorate it with an `ExportCodeFixProviderAttribute`. This is what a barebones code fix provider should look like.

```csharp
using System;
using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace EmptyLinesAnalyzerAndFix;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EmptyLinesCodeFix)), Shared]
public class EmptyLinesCodeFix : CodeFixProvider
{

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        // ...
    }

    public override ImmutableArray<string> FixableDiagnosticIds { get; }
}
```

If you are keen on exploring GitHub and you run across any code fix provider in the [Roslyn repository](https://github.com/dotnet/roslyn), you might notice it is also decorated by a `[Shared]` attribute. The reason for that is code fix providers are [MEF](https://learn.microsoft.com/en-us/dotnet/framework/mef/) components. As per Roslyn guidelines, they should also be stateless. This means an IDE using a particular code fix provider can optimize its usage by creating a single _shared_ instance of said code fix provider.

#### Link the code fix to the analyzer

We first need is to provide our code fix provider with a list of diagnostics it can fix. Since we are only fixing _our_ diagnostics and we also conveniently remembered to expose the `DiagnosticId` constant in the `EmptyLinesAnalyzer`, we can use it here.

```csharp
public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(EmptyLinesAnalyzer.DiagnosticId);
```

This will ensure that any time the referenced diagnostic is encountered, this code fix provider will be able to fix it.

To be fair, our implementation can't fix anything yet. For this to happen, we also need to register a _code action. A code action describes an intent to change one or more documents in a solution. Simply put, a code action allows the host (e.g. an IDE), to display a light bulb_ 💡, which enables you to apply a code fix.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1676312103698/c554c612-731f-418a-ba55-c3669debb32f.gif)

> For JetBrains Rider users, there is an [annoying limitation feature](https://youtrack.jetbrains.com/issue/RIDER-18372/Roslyn-quick-fix-does-not-provide-an-option-to-fix-the-issue-in-file-solution) in the IDE that allows you to apply a code fix only to a particular occurrence. Visual Studio, for example, will display several additional options for fixing your code and previewing the code fix.
>
> ![](https://cdn.hashnode.com/res/hashnode/image/upload/v1676315300257/a7a3af48-327c-4895-8e56-7164d0f47bcd.gif)

#### Enable fixing multiple diagnostics

For the code fix provider to be able to fix multiple occurrences simultaneously, we also need to provide a "smart" way to achieve this. Luckily, Roslyn already offers a built-in solution for fixing batches of diagnostics in a single go (just be mindful of its [limitations](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md#limitations-of-the-batchfixer)).

```csharp
public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;
```

#### Register the code fix

To enable the compiler to see and the IDE to display our code fix action, we need to (_hint hint_) implement the `RegisterCodeFixesAsync` method.

```csharp
public override Task RegisterCodeFixesAsync(CodeFixContext context)
{
    const string action = "Remove redundant empty lines";

    var diagnostic = context.Diagnostics.First();
    context.RegisterCodeFix(
        CodeAction.Create(
            title: action,
            createChangedDocument: cancellationToken => /*...*/,
            equivalenceKey: action),
        diagnostic);

    return Task.CompletedTask;
}
```

The text specified in the `title` will be displayed when the user hovers over the light bulb. "_Remove redundant empty lines_" will also be used as an `equivalenceKey`. This means that by fixing multiple occurrences of this diagnostic (say, by executing the code action over the entire solution), all diagnostics with the same equivalence key will be fixed.

With all that ceremony out of the way, we are left with the final piece of the puzzle. To fix the code, we need to provide a factory that will be invoked by the compiler once the change is necessary. The `createChangedDocument` parameter takes a delegate. When invoked, this delegate will receive a cancellation token provided by the compiler and return a document or solution that was modified by our code fix provider.

> The code fix provider needs to utilize the received `cancellationToken` properly. The compiler may, at any time, cancel the operation. For the IDE to remain responsive, any extensions to the compiler (e.g. our code fix provider) should react to cancellation requests immediately.

```csharp
private static async Task<Document> RemoveEmptyLines(Document document,
    Diagnostic diagnostic,
    CancellationToken cancellationToken)
{
    // ...
}
```

#### Implement the code fix provider logic

We now have all the information available to fix our diagnostic. The `document` defines the syntax tree we will be modifying. It also represents the document where the `diagnostic` is located. The `cancellationToken`, as mentioned, should be passed to any time-consuming operations (or we could just `cancellationToken.ThrowIfCancellationRequested();` when doing any CPU-bound work in our code).

And now, the fun part, let's figure out how to change the document to fix the diagnostic. 👨‍💻

What? No. No peeking! Alright, I will include a link to the demo repository at the end of this article.

If you are adamant about trying to solve this yourself, here are a couple of tips to help you on the way.

-   Similarly to the previous article, we only need to deal with the [syntax tree](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis). This means that we can skip [semantic analysis](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis) altogether and focus on a single document source code,

-   referencing the analyzer we implemented in the [previous article](https://denace.dev/getting-started-with-roslyn-analyzers), we were smart enough to include an `additionalLocation` which is a good place to start looking for the part of the syntax tree that needs to change,

-   remember, [Roslyn syntax trees are immutable](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis#understanding-syntax-trees). There are several benefits to this. Although for us, this means that any time we need to change a node in that tree, it needs to be rebuilt from the ground up.


Let's see how our implemented solution works in the demo console app.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1676321306123/e0942ef3-dc48-4088-9d51-a2f7087ae5e4.gif)

Great! We are done. Ship it. Now.

But wait, let's take a step back this time. Let's imagine not hearing Mark Zuckerberg uttering the words that decorate our office walls.

Let's be sane this time around and test our code instead of our boss's patience.

As promised, here is a link to the [**denisekart/exploring-roslyn**](https://github.com/denisekart/exploring-roslyn) repository, where you can find all the samples from this series. We will explore various ways of testing Roslyn analyzers and code fixes in the next installment.

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

### Testing Roslyn Analyzers and Code Fixes

In the previous article, we implemented a Roslyn analyzer and code fix. This article will focus on various ways to properly test them. We will leverage existing test libraries and explore the means to write one ourselves using Roslyn workspaces. This...

Mar 1, 2023·14 min read

![Testing Roslyn Analyzers and Code Fixes](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1677095073165%2Ff158179f-5e0c-4f68-bcb4-3834d852b67e.png&w=3840&q=75)

](/testing-roslyn-analyzers-and-code-fixes)

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

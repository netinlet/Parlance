# Exploring Roslyn .NET Compiler Platform SDK

[Skip to main content](#main-content)

[Hashnode](https://hashnode.com/?utm_source=https%3A%2F%2Fdenace.dev&utm_medium=referral&utm_campaign=blog_header_logo&utm_content=logo)[![The Dev Domain](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676059304472%2F-clIoEy--.png&w=1080&q=75)The Dev Domain](/)

Open search (press Control or Command and K)Toggle themeOpen menu

[Hashnode](https://hashnode.com/?utm_source=https%3A%2F%2Fdenace.dev&utm_medium=referral&utm_campaign=blog_header_logo&utm_content=logo)[![The Dev Domain](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1676059304472%2F-clIoEy--.png&w=1080&q=75)The Dev Domain](/)

Open search (press Control or Command and K)

Toggle themeSubscribe[Write](https://hn.new)

## Command Palette

Search for a command to run...

# Exploring Roslyn .NET Compiler Platform SDK

Making developers lives easier by utilizing an open compiler platform

UpdatedJanuary 25, 2023

•

5 min read

![Exploring Roslyn .NET Compiler Platform SDK](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1674686378484%2F4db51c58-5b7f-496c-aca2-970c1c03f562.png&w=3840&q=75)

[![Denis Ekart](https://cdn.hashnode.com/res/hashnode/image/upload/v1674506798653/obIlPL2T5.png?auto=compress,format&format=webp)](https://hashnode.com/@devmenace)

[

Denis Ekart

](https://hashnode.com/@devmenace)

[

Part of seriesExploring Roslyn

](/series/exploring-roslyn)

In this series of articles, I will explore how Roslyn enhances the development process and allows developers to write code analysis, transformation, and manipulation tools.

_(This series is a work in progress. I will update the article with relevant links as I progress through the series)_

-   Episode 1: Exploring Roslyn .NET Compiler Platform SDK (_this article_)

-   Episode 2: [Getting Started With Roslyn Analyzers](https://denace.dev/getting-started-with-roslyn-analyzers)

-   Episode 3: [**Fixing Mistakes With Roslyn Code Fixes**](https://denace.dev/fixing-mistakes-with-roslyn-code-fixes)

-   Episode 4: (soon)


---

What is Roslyn anyway? _Okay, let me just.._. `CTRL+C,` `CTRL+V`

> .NET Compiler Platform, also known by its codename Roslyn, is a set of open-source compilers and code analysis APIs for C# and Visual Basic languages from Microsoft.

Clear, concise. That makes sense, right? But wait, there's more. To better understand Roslyn, we need to go back to its inception (well, we don't, but why not). Here is a brief replay of the events that lead up to all the fun stuff I'm about to describe in this series.

### A (very) brief history of Roslyn

-   On December 2010, Eric Lippert posted an article, [_Hiring for Roslyn_](https://learn.microsoft.com/sl-si/archive/blogs/ericlippert/hiring-for-roslyn) announcing a major re-architecture of the C# compiler (and VB compiler too).

-   At a 2014 Build conference, Microsoft made the Roslyn project open-source under the stewardship of the newly founded .NET Foundation. Roslyn gets shipped with VisualStudio 2015.

-   In 2015, C# 6 gets released with several [prominent language features](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-version-history#c-version-60), which somewhat hides the fact, that the C# compiler (and VB compiler too) has been completely rewritten in C# (and VB too), making this year a significant milestone for the language.


### Compiler as a service

[The .NET compiler SDK](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/compiler-api-model) consists of several layers of APIs, allowing you to effectively integrate into the compilation process, starting at the parsing phase and ending in the emit phase.

![](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/media/compiler-api-model/compiler-pipeline-lang-svc.png)

The **Compiler API** surface allows you to access the information exposed by the compiler at each stage of the compilation process.

As a part of the compilation process, the compiler may produce diagnostics of varying severity that may be purely informational or expose a compilation error. The **Diagnostic API** allows you to integrate into the pipeline naturally. It enables you to produce a set of custom diagnostic messages or even provide the ability to execute code fixes.

**Scripting API** allows you to execute various code snippets effectively, enabling you to use C# as a scripting language. The C# interactive (Read-Evaluate-Print-Loop) is one tool that utilizes this API.

Of course, modern C# tooling allows you to perform various types of code analysis and refactoring jobs. These are all possible because the **Workspaces API** provides a virtual, well, workspace making it possible to format code across projects, find type references and even generate source code.

Okay, now I have a bunch of APIs to which I somehow have access. Any idea what to do with them?

### Analyze code and report diagnostics

Roslyn allows you to inject components that interact with the compilation process and can ultimately emit diagnostics. These diagnostics can have [varying severities](https://learn.microsoft.com/en-us/visualstudio/code-quality/roslyn-analyzers-overview?view=vs-2022#severity-levels-of-analyzers) and can be used to inform the developer of non-significant compilation events (_missing comment on a public member_) or can halt the compilation process entirely (_missing semicolon at the end of a statement_).

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1674593797140/f0f226c8-e13d-43c5-8e07-ee39d6ceba0e.gif)

This allows you to tailor the entire compilation process to your project needs and standards. While writing custom code analysis tools may be a good exercise, there is already [a vast ecosystem of Roslyn analyzers](https://github.com/topics/roslyn-analyzer) that might fit your needs. These can be installed as [IDE extensions](https://learn.microsoft.com/en-us/visualstudio/code-quality/install-roslyn-analyzers?view=vs-2022) or injected into your project as standard NuGet package dependencies.

### Provide code fixes

As it turns out, nagging about things without having the ability to fix them is pretty useless(_a life lesson right here_). For that reason, Roslyn allows you to define a code fix corresponding to a diagnostic.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1674593843162/8c0ac4ea-fc67-404c-8d2d-acc086ff0da2.gif)

Code fixes can be applied manually through [editor actions](https://learn.microsoft.com/en-us/visualstudio/ide/quick-actions?view=vs-2022) or automatically be applied using a tool such as [dotnet format](https://github.com/dotnet/format) (so many tools).

### Rewrite existing code

There are various reasons why you would want to rewrite your code. Perhaps a method has become too complex. Possibly you can improve the code readability by hiding away some implementation details (e.g. [writing declarative code](https://en.wikipedia.org/wiki/Declarative_programming)). Whichever reason you may have to refactor your solutions - manually doing so can be tedious work.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1674595408597/a6e53cea-dcdd-4d78-ba0f-44a510e1b435.gif)

Roslyn allows you to plug in a refactoring solution that can analyze your syntax to provide intelligent means of automatically refactoring code.

### Generate source code

Have you heard of the saying _the best code is no code at all_? It turns out source code is evil. It has bugs. It rots over time. It requires maintenance. It needs engineers to write it.

Wait, what am I saying? I'm an engineer. I love writing code, though not all code. There is code I am always hesitant to start writing. Not because it's hard but because it's solving the same problem I have solved numerous times before.

![](https://cdn.hashnode.com/res/hashnode/image/upload/v1674596146334/2160b9ca-3f9b-4d8e-8040-32a793b6a4bd.png)

Roslyn allows you to automate this too. You can use create a source generator that can automatically analyze relevant parts of your solution to spit out code without you typing a single syntax token (well, apart from writing the actual source generator).

### Before we proceed

Just a fair warning. The tools described in this article integrate perfectly with [Microsoft VisualStudio IDE](https://visualstudio.microsoft.com/) and will work on any updated version of VisualStudio 2022 (and above). That being said, the Roslyn Compiler Platform is a part of the mentioned IDE, meaning that the utilities described in this series of articles will (with minor exceptions) work in any IDE that supports modern .NET and will even work without an IDE([dotnet CLI](https://dotnet.microsoft.com/en-us/download/dotnet)).

All examples in these articles were developed using .NET 7, VisualStudio 2022 Community Edition, and JetBrains Rider.

[Let's write some code!](https://denace.dev/getting-started-with-roslyn-analyzers)

[#dotnet](/tag/dotnet)[#csharp](/tag/csharp)[#compilers](/tag/compilers)[#metaprogramming](/tag/metaprogramming)

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

### Getting Started With Roslyn Analyzers

A Hands-On Guide to Building and Understanding Roslyn Analyzers

Feb 5, 2023·10 min read

![Getting Started With Roslyn Analyzers](/_next/image?url=https%3A%2F%2Fcdn.hashnode.com%2Fres%2Fhashnode%2Fimage%2Fupload%2Fv1675286191591%2F63279503-a0d4-4143-a877-a94307dd6a9d.png&w=3840&q=75)

](/getting-started-with-roslyn-analyzers)

![Publication avatar](https://cdn.hashnode.com/res/hashnode/image/upload/v1676059212984/tdWOpBmgz.png?auto=compress,format&format=webp)

The Dev Domain

5 posts published

I'm a highly driven software engineer with experience in developing performant cloud-native solutions in .NET. The Future is Written in Code.

[](https://twitter.com/EkartDenis)[](https://instagram.com/EkartDenis)[](https://github.com/denisekart)[](https://www.linkedin.com/in/denis-ekart/)[](https://hashnode.com/@devmenace)


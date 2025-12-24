# Agent Guidelines

## Build & Test Commands
- **Build**: `dotnet build`
- **Test all**: `dotnet test`
- **Single test**: `dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"`
- **Test project**: `dotnet test test/SymbolCollector.Core.Tests/`
- **Run server**: `dotnet run --project src/SymbolCollector.Server/`

## Code Style (C#)
- Use `var` for all variable declarations
- Private fields: `_camelCase` prefix with underscore
- Constants/static fields: `PascalCase`
- Always use braces for control flow statements
- Sort usings with `System.*` first (implicit usings enabled)
- Nullable reference types enabled; treat warnings as errors
- Use expression-bodied members for simple properties/accessors
- Prefer pattern matching, null propagation (`?.`), and null coalescing (`??`)
- Allman brace style (opening brace on new line)
- 4-space indentation for C#, 2-space for JSON/csproj

## Error Handling
- Use nullable annotations; avoid suppressing nullability warnings
- Prefer throwing specific exceptions over generic ones

# Repository Guidelines

## Project Structure
- `NT.Blazor.ErrorBoundary/`: Razor class library with the reusable boundary component, reporting contracts, DTOs, and client-side HTTP reporter.
- `NT.Blazor.ErrorBoundary.AspNetCore/`: ASP.NET Core host integration for server logging and telemetry endpoint mapping.
- `Tests/NT.Blazor.ErrorBoundary.Tests/`: xUnit and bUnit coverage for the component, reporters, and endpoint logging.

## Design Rules
- Keep the core library safe for Blazor WebAssembly. Do not add server-only framework references to `NT.Blazor.ErrorBoundary`.
- Keep host-specific policy outside the library. Endpoint paths, authorization, and Application Insights/OpenTelemetry setup belong to the consuming app.
- Report errors through `ILogger` or configurable `HttpClient`; do not depend directly on Application Insights SDKs.
- Error UI must render generic fallback content by default. Exception details may render only when explicitly enabled or in development-only mode.
- Blazor components must keep markup in `.razor`, logic in `.razor.cs`, and styles in `.razor.scss`.

## Build and Test
- Build the submodule with `dotnet build ./NT.Blazor.ErrorBoundary.slnx`.
- Run tests with `dotnet test ./NT.Blazor.ErrorBoundary.slnx`.
- Keep public APIs documented with XML `<summary>` comments.

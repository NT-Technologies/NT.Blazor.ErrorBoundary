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
- Pack both libraries with `dotnet pack ./NT.Blazor.ErrorBoundary/NT.Blazor.ErrorBoundary.csproj --configuration Release` and `dotnet pack ./NT.Blazor.ErrorBoundary.AspNetCore/NT.Blazor.ErrorBoundary.AspNetCore.csproj --configuration Release`.
- Run the manual `Release` GitHub Actions workflow to create a semantic-release tag and GitHub release, then publish both packages and their symbols to NuGet.org using the `NUGET_API_KEY` secret.
- Use Conventional Commits: `fix` produces a patch release, `feat` produces a minor release, and a breaking change produces a major release.
- `Directory.Build.props` enables SDK analyzers and treats all warnings as errors across the repository.
- Packable projects generate XML documentation; missing public API documentation fails the build.
- Keep public APIs documented with XML `<summary>` comments.

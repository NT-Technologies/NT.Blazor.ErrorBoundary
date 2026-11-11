# Repository Guidelines

## Project Structure
- `NT.Blazor.ErrorBoundary/`: Razor class library with the reusable boundary component, reporting contracts, DTOs, and client-side HTTP reporter.
- `NT.Blazor.ErrorBoundary.AspNetCore/`: ASP.NET Core host integration for server logging and telemetry endpoint mapping.
- `NT.Blazor.ErrorBoundary.AzureMonitor/`: optional add-on that sends every error and event to Application Insights as native envelopes through `INTBlazorErrorBoundaryExporter`, and keeps the same log categories out of the OpenTelemetry log pipeline.
- `Tests/NT.Blazor.ErrorBoundary.Tests/`: xUnit and bUnit coverage for the component, reporters, and endpoint logging.
- `Tests/NT.Blazor.ErrorBoundary.AspNetCore.Tests/`: endpoint, server reporter, circuit, metrics, and OpenTelemetry coverage.
- `Tests/NT.Blazor.ErrorBoundary.AzureMonitor.Tests/`: stack parsing, envelope, sender, and endpoint coverage for the Azure Monitor add-on.
- `Tests/ClientAssets/`: `node:test` coverage for the browser script.
- `samples/`: a Blazor Web App (server + WebAssembly client) wired as the README describes; it is not packed and doubles as the host for end-to-end tests.
- `docs/azure-monitor/`: an Application Insights workbook and Bicep alert rules built on the logged fields; not packed.
- `Tests/NT.Blazor.ErrorBoundary.EndToEnd.Tests/`: Playwright (Chromium) tests that run the sample on Kestrel and assert browser behavior and server logs.

## Design Rules
- Keep the core library safe for Blazor WebAssembly. Do not add server-only framework references to `NT.Blazor.ErrorBoundary`.
- Package dependencies flow one way: `NT.Blazor.ErrorBoundary.AzureMonitor` → `NT.Blazor.ErrorBoundary.AspNetCore` → `NT.Blazor.ErrorBoundary`. Never add a reference that creates a cycle. Reference sibling packages with `ProjectReference` so each packs as a minimum-version dependency on the same version (`>=`), with no upper bound.
- Keep host-specific policy outside the library. Endpoint paths, authorization, and Application Insights/OpenTelemetry setup belong to the consuming app.
- Report errors through `ILogger` or configurable `HttpClient`; do not depend directly on Application Insights SDKs. The one exception is `NT.Blazor.ErrorBoundary.AzureMonitor`, which posts Application Insights envelopes over HTTP because the Azure Monitor exporter can only describe exceptions from thrown .NET objects and drops logs from sampled-out traces; it references only `Azure.Core` (for `TokenCredential`) and `OpenTelemetry` (for `SuppressInstrumentationScope`), and the core and ASP.NET Core packages must not depend on it.
- Attach server log fields to the log entry through `NTBlazorLog`, not `BeginScope`, and pass every logged exception and event to an optional `INTBlazorErrorBoundaryExporter` alongside the log: the Azure Monitor distro drops scopes by default, and its exporter maps `enduser.id`, `microsoft.session.id`, `user_agent.original`, and `microsoft.custom_event.name` only from entry attributes. Log non-exception signals with `NTBlazorLog.WriteEvent`.
- Name log fields, metric attributes, and event names with OpenTelemetry semantic conventions where one exists and the `blazor.*` namespace otherwise. Write `session.id` with `microsoft.session.id` and `user.id` with `enduser.id`, because the Azure Monitor exporter maps only the second name of each pair.
- Error UI must render generic fallback content by default. Exception details may render only when explicitly enabled or in development-only mode.
- Blazor components must keep markup in `.razor`, logic in `.razor.cs`, and styles in `.razor.scss`.

## Build and Test
- Build the submodule with `dotnet build ./NT.Blazor.ErrorBoundary.slnx`.
- Run tests with Microsoft.Testing.Platform using `dotnet test --solution ./NT.Blazor.ErrorBoundary.slnx`.
- Run release-tooling and browser-script tests with `npm test` after building; the browser-script tests load the built static asset.
- End-to-end tests skip when Playwright's Chromium is missing. Install it with `pwsh ./Tests/NT.Blazor.ErrorBoundary.EndToEnd.Tests/bin/Debug/net11.0/playwright.ps1 install chromium` after building; set `NT_E2E_REQUIRED=true` to make a missing browser fail instead (CI does).
- To add a target framework (for example `net12.0` at GA): raise `global.json` to that SDK; append the TFM to `<TargetFrameworks>` in all three libraries and all three unit-test projects; copy each `net10.0`-conditioned `ItemGroup` to the new TFM with that major's `.0.0` versions (`.0.x` for test-only packages); move the samples and the end-to-end project to the new TFM and package major; and update the `bin/<configuration>/<tfm>/playwright.ps1` path in the workflow and docs; and add the previous major's runtime to each workflow's `setup-dotnet` `dotnet-version`, because `global.json` installs only the newest SDK. `net11.0` was added this way at .NET 11 GA.
- Component styles in `.razor.scss` compile to scoped `.razor.css` through `AspNetCore.SassCompiler` during the build; the generated `.razor.css` files are not committed.
- Collect Microsoft code coverage for each test project on .NET 10 with `--coverage --coverage-output-format cobertura` and a distinct `--results-directory`; do not give parallel solution modules the same coverage output path.
- Pack the libraries with `dotnet pack ./NT.Blazor.ErrorBoundary/NT.Blazor.ErrorBoundary.csproj --configuration Release`, `dotnet pack ./NT.Blazor.ErrorBoundary.AspNetCore/NT.Blazor.ErrorBoundary.AspNetCore.csproj --configuration Release`, and `dotnet pack ./NT.Blazor.ErrorBoundary.AzureMonitor/NT.Blazor.ErrorBoundary.AzureMonitor.csproj --configuration Release`; the workflows pack all three.
- Pushes to `main` run `Publish Prerelease`; semantic-release creates a `vX.Y.Z-preview.N` tag and GitHub prerelease, then publishes all three packages and their symbols to NuGet.org through trusted publishing (`NuGet/login` with the `NUGET_USER` secret and `id-token: write`).
- Run the manual `Release` workflow to create `vX.Y.Z`, publish all three stable packages and their symbols, and unlist prerelease versions at or below the stable version.
- Use Conventional Commits: `fix` produces a patch release, `feat` produces a minor release, and a breaking change produces a major release.
- `Directory.Build.props` enables SDK analyzers and treats all warnings as errors across the repository.
- Packable projects generate XML documentation; missing public API documentation fails the build.
- Keep public APIs documented with XML `<summary>` comments.

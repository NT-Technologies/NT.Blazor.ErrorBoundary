# NT.Blazor.ErrorBoundary

Reusable Blazor error-boundary reporting with a WebAssembly-safe core package and optional ASP.NET Core logging integration. The component renders generic fallback UI by default and reports failures through `ILogger` or a configurable HTTP endpoint without depending directly on Application Insights or OpenTelemetry SDKs.

## Packages

| Package | Use it for |
| --- | --- |
| `NT.Blazor.ErrorBoundary` | The `NTErrorBoundary` component, shared reporting contracts, and HTTP reporting from Blazor WebAssembly. |
| `NT.Blazor.ErrorBoundary.AspNetCore` | Direct server-side exception logging and an ASP.NET Core endpoint that receives WebAssembly error reports. |

Both packages target .NET 9 and .NET 10. The ASP.NET Core package depends on the core package, so installing it in a server project provides both. Install the core package directly in a separate `.Client` or standalone WebAssembly project.

```shell
# Server or API project
dotnet add package NT.Blazor.ErrorBoundary.AspNetCore

# Separate .Client or standalone WebAssembly project
dotnet add package NT.Blazor.ErrorBoundary
```

## Choose the setup for your render mode

| Render mode | Server project | Client project |
| --- | --- | --- |
| Interactive Server | Register server logging. The HTTP endpoint is optional. | Not applicable. |
| Interactive WebAssembly | Register server logging and map the telemetry endpoint. | Register the HTTP reporter. |
| Interactive Auto | Register server logging and map the telemetry endpoint. | Register the HTTP reporter so the same boundary continues working after WebAssembly activation. |
| Standalone WebAssembly | The separate ASP.NET Core API registers server logging and maps the endpoint. | Register the HTTP reporter with the API URI. |

## Complete error-boundary setup for a Blazor Web App

This is the typical setup for a Blazor Web App that uses Interactive Server, Interactive WebAssembly, or Interactive Auto render modes.

### 1. Configure the server project

Install `NT.Blazor.ErrorBoundary.AspNetCore` in the server project, then register the server reporter before building the application. The report path is host-owned and can be changed to match the application's API conventions.

```csharp
using NT.Blazor.ErrorBoundary.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddBlazorErrorBoundaryServerLogging("/api/blazor-errors");

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

app.MapBlazorErrorBoundaryTelemetry();

app.Run();
```

`AddBlazorErrorBoundaryServerLogging` performs three registrations:

- It registers `NTBlazorServerErrorReporter` as the scoped `INTBlazorErrorReporter` used during server rendering.
- It stores the report path used by `MapBlazorErrorBoundaryTelemetry`.
- It reads `IHostEnvironment` so exception details are rendered by default only in Development.

`MapBlazorErrorBoundaryTelemetry` maps a JSON `POST` endpoint at the configured path. Successful reports return `204 No Content` and are written through `ILogger`.

If the application never uses a WebAssembly render mode, calling `MapBlazorErrorBoundaryTelemetry` is optional. Server-side exceptions are logged directly and do not make an HTTP round trip.

### 2. Configure the `.Client` project

Install `NT.Blazor.ErrorBoundary` in the client project and register the HTTP reporter in its `Program.cs`.

```csharp
using Microsoft.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services
    .AddNTBlazorErrorBoundaryHttpClient("/api/blazor-errors", builder.HostEnvironment.Environment)
    .ConfigureHttpClient(client => {
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
    });

await builder.Build().RunAsync();
```

The base address is required when `reportUri` is relative. An absolute report URI can be used instead, in which case the reporter does not require `BaseAddress`.

The environment name controls the default exception-detail behavior in the browser. Pass `builder.HostEnvironment.Environment` so `DevelopmentOnly` behaves consistently between the server and client DI containers.

For cross-origin APIs, use an absolute endpoint URI and configure CORS on the API host. If the endpoint requires authentication, configure the returned `IHttpClientBuilder` with the application's authorization message handler.

### 3. Load browser telemetry

Add the compiled static web asset before `blazor.web.js` in `App.razor`:

```razor
<script src="@Assets["_content/NT.Blazor.ErrorBoundary/nt-blazor-error-boundary.js"]"
        data-report-uri="/api/blazor-errors"
        data-application-version="1.2.3"
        data-stall-timeout-ms="15000"></script>
<script src="@Assets["_framework/blazor.web.js"]" defer></script>
```

Use the same report URI for server registration, the client reporter, and `data-report-uri`. The script doesn't initialize when `data-report-uri` is absent.

`data-application-version` is optional. `data-stall-timeout-ms` is optional and defaults to 15 seconds.

Browser telemetry:

- Adds browser and navigation context to boundary reports.
- Captures JavaScript errors and unhandled promise rejections.
- Stores up to 20 recent navigation breadcrumbs in `sessionStorage`.
- Queues failed report submissions and retries them when connectivity returns.
- Uses keepalive requests and a page-hide beacon when available.
- Never interrupts application behavior when telemetry storage or delivery fails.


### 4. Add the boundary to the component tree

Place `NTErrorBoundary` around the part of the component tree that should fail and recover together. Wrapping the layout body is a common application-wide boundary.

```razor
@using NT.Blazor.ErrorBoundary.Components
@inherits LayoutComponentBase

<NTErrorBoundary>
    @Body
</NTErrorBoundary>
```

You can instead wrap `Routes`, a page, or a smaller feature. Smaller boundaries isolate failures so the rest of the page remains usable.

When a descendant throws, the default fallback:

- Displays a generic message.
- Hides exception details unless they are explicitly enabled or the environment is Development.
- Displays a **Try again** button for interactive rendering.
- Displays a **Reload page** link for static rendering.
- Attempts to report the error without allowing reporting failures to replace the fallback UI.

## Interactive Server-only setup

For an application that never activates WebAssembly, only the server registration and component are required.

```csharp
builder.Services.AddBlazorErrorBoundaryServerLogging("/api/blazor-errors");
```

```razor
@using NT.Blazor.ErrorBoundary.Components

<NTErrorBoundary>
    @Body
</NTErrorBoundary>
```

The path must be supplied because it is part of the shared options contract, but the endpoint does not need to be mapped when no browser-side reports will be posted.

## Standalone WebAssembly with a separate API

Configure the ASP.NET Core API exactly like the server portion of the complete setup:

```csharp
builder.Services.AddBlazorErrorBoundaryServerLogging("/api/blazor-errors");

var app = builder.Build();

app.MapBlazorErrorBoundaryTelemetry();
```

Configure the standalone WebAssembly application with the API's absolute URI:

```csharp
builder.Services.AddNTBlazorErrorBoundaryHttpClient(
    "https://api.example.com/api/blazor-errors",
    builder.HostEnvironment.Environment);
```

The API host must explicitly allow the WebAssembly application's origin through CORS.

## Exception-detail policy

`RenderExceptionDetails` controls what the default fallback renders:

```razor
@using NT.Blazor.ErrorBoundary.Components

<NTErrorBoundary RenderExceptionDetails="ExceptionDetailsMode.Never">
    @Body
</NTErrorBoundary>
```

| Value | Behavior |
| --- | --- |
| `DevelopmentOnly` | Default. Render details only when the registered environment is Development. |
| `Always` | Always render exception details. Use cautiously because messages and stack traces can contain sensitive information. |
| `Never` | Never render exception details. |

The rendering policy affects only the fallback UI. Reports and server logs still contain diagnostic exception fields.

## Custom fallback content

`NTErrorBoundary` inherits Blazor's `ErrorBoundary` content parameters. Supplying `ErrorContent` replaces the package's default fallback UI.

```razor
@using NT.Blazor.ErrorBoundary.Components

<NTErrorBoundary>
    <ChildContent>
        @Body
    </ChildContent>
    <ErrorContent Context="exception">
        <section role="alert">
            This section is temporarily unavailable.
        </section>
    </ErrorContent>
</NTErrorBoundary>
```

The exception is still reported before the custom fallback renders. Avoid displaying `exception.Message` outside a trusted development environment.

## Size limits

String fields in an HTTP report and rendered exception details default to a maximum of 16,384 characters. Configure smaller limits after registering the reporter:

```csharp
using NT.Blazor.ErrorBoundary.Services;

builder.Services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
    options.MaxReportFieldLength = 4_096;
});
builder.Services.Configure<NTErrorBoundaryOptions>(options => {
    options.MaxExceptionDetailsLength = 4_096;
});
```

Configure `MaxReportFieldLength` in both the client and server DI containers when using WebAssembly. The client truncates before sending, and the server truncates again before logging. A limit less than or equal to zero disables truncation.

## Logging and telemetry

The package writes through `ILogger`; the consuming application decides where those logs go.

```csharp
builder.Logging.AddConsole();
// Add the application's Application Insights, OpenTelemetry, Serilog,
// or other ILogger provider here.
```

Server-rendered exceptions log the original `Exception` with these structured scope values:

- `BlazorBoundaryName`
- `BlazorIsInteractive`
- `BlazorRenderMode`
- `BlazorUri`

Reports received from WebAssembly additionally include:

- `BlazorExceptionMessage`
- `BlazorExceptionStackTrace`
- `BlazorExceptionType`
- `BlazorOccurredAtUtc`
- `ApplicationVersion`
- `BlazorBreadcrumbs`
- `BlazorClientSessionId`
- `BlazorIsNavigationIntercepted`
- `BlazorIsOnline`
- `BlazorJavaScriptColumn`
- `BlazorJavaScriptLine`
- `BlazorJavaScriptSource`
- `BlazorNavigationId`
- `BlazorNavigationPhase`
- `BlazorOriginUri`
- `BlazorReportKind`
- `BlazorTargetUri`
- `RequestId`
- `RequestMethod`
- `RequestPath`
- `UserId`, when a `ClaimTypes.NameIdentifier` or `sub` claim is present

The HTTP reporter treats network and non-success response failures as reporting failures, logs a warning, and does not rethrow them into the component tree. Cancellation requested by the caller is preserved.

### OpenTelemetry host setup

Configure OpenTelemetry in the consuming ASP.NET Core host. Both the server reporter and the browser-report endpoint use the host's `ILogger` pipeline. Install these packages in the host project (use compatible versions from the same OpenTelemetry release):

```shell
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
```

Add the following to `Program.cs`, alongside the app's existing Blazor registration and endpoint mapping:

```csharp
using NT.Blazor.ErrorBoundary.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var serviceName = builder.Environment.ApplicationName;

builder.Services.AddBlazorErrorBoundaryServerLogging("/api/blazor-errors");

builder.Logging.AddOpenTelemetry(options => {
    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));
    options.IncludeScopes = true;
    options.IncludeFormattedMessage = true;
    options.AddOtlpExporter();
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();
app.MapBlazorErrorBoundaryTelemetry();
// Keep the app's existing Blazor endpoints and middleware here.
app.Run();
```

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to your collector endpoint. For example, `http://localhost:4317` with `OTEL_EXPORTER_OTLP_PROTOCOL=grpc` uses OTLP over gRPC. The report URI (`/api/blazor-errors`) is a separate application endpoint accepting the package's JSON reports; it is not an OTLP collector endpoint. For WebAssembly, configure the client reporter and browser bridge with that same report URI as described above.

If the host already configures OpenTelemetry (for example through shared service defaults), update that registration instead of adding another exporter pipeline. Ensure that logging is enabled as well as tracing, that filters allow `Error` logs from `NT.Blazor.ErrorBoundary`, and that `IncludeScopes` is enabled. Most boundary, browser, and request fields are scope values; `IncludeFormattedMessage` additionally preserves the readable message. See the official [logging configuration](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/logs/customizing-the-sdk/README.md) and [log correlation](https://opentelemetry.io/docs/languages/dotnet/logs/correlation/) documentation.

Current correlation and exception behavior:

- Server reports retain the original exception. OpenTelemetry populates the log's trace and span IDs when an `Activity` is active. The library does not create an activity for interactive callbacks that lack one.
- Browser reports correlate with the receiving HTTP request's activity. The browser transport does not capture or explicitly propagate the original operation's W3C trace context, including across queued retries. `BlazorClientSessionId` and `BlazorNavigationId` are searchable diagnostic fields, not distributed trace IDs.
- Browser exceptions are logged as `ClientBlazorErrorReportException`. Their original type, message, stack, and full details are preserved in `BlazorExceptionType`, `BlazorExceptionMessage`, `BlazorExceptionStackTrace`, and `BlazorExceptionDetails`. Backends grouping by standard exception fields may group them by the wrapper type.
- A successful upload returns HTTP 204 even though its log describes an error. The library does not mark the upload span as failed or emit custom spans or metrics. Queued reports retain their occurrence time in `BlazorOccurredAtUtc`; the server log is emitted when the report arrives.

The integration tests use the real OpenTelemetry SDK and in-memory log/trace exporters to verify both reporting paths on .NET 9 and .NET 10. They cover scope preservation, exception data, and correlation with an exported span, including an HTTP request through the mapped endpoint. They do not validate collector connectivity or backend-specific exception grouping. Preserving original browser trace context and mapping browser exceptions to standard telemetry fields remain follow-up work.

## Securing the telemetry endpoint

The package intentionally does not choose authorization, CORS, rate-limiting, or telemetry-retention policy for the host. The mapped endpoint accepts exception messages and stack traces, so treat it as diagnostic ingestion rather than a general public API.

`MapBlazorErrorBoundaryTelemetry` returns a `RouteHandlerBuilder`, allowing normal ASP.NET Core endpoint policy:

```csharp
app.MapBlazorErrorBoundaryTelemetry()
    .RequireAuthorization("BlazorTelemetry")
    .RequireRateLimiting("blazor-telemetry");
```

Choose policies that match the application:

- Require authorization when browser reports are sent with an authenticated identity.
- Configure the client reporter to attach the corresponding cookie or bearer token.
- Apply rate limits and request-size limits to prevent telemetry abuse.
- Configure CORS explicitly for cross-origin WebAssembly applications.
- Treat logged URIs, exception messages, and stack traces as potentially sensitive data.
- Apply redaction and retention in the configured logging provider.

## Data flow

Server-rendered exception:

```text
NTErrorBoundary
    -> INTBlazorErrorReporter
    -> NTBlazorServerErrorReporter
    -> ILogger
```

WebAssembly exception:

```text
NTErrorBoundary
    -> NTHttpClientBlazorErrorReporter
    -> POST /api/blazor-errors
    -> MapBlazorErrorBoundaryTelemetry
    -> ILogger
```

## Detect stalled interactive navigation

Stall detection is opt-in for navigation that must reach an interactive Blazor render. Place `NTBlazorInteractiveReady` in the destination's interactive component tree:

```razor
@page "/claims/recovery/{ClaimId:int}"
@rendermode InteractiveAuto

<NTBlazorInteractiveReady />
<RecoveryContent />
```

Mark links whose telemetry should wait for that signal:

```razor
<a href="@($"/claims/recovery/{claimId}")"
   data-nt-require-interactive-ready="true">
    Recovery Management
</a>
```

`data-nt-require-interactive-ready="true"` affects telemetry only. It does not change routing, cancel navigation, delay rendering, or enable interactivity.

- Without the attribute, a document load or Blazor enhanced-load signal completes the navigation measurement.
- With the attribute, telemetry waits until `NTBlazorInteractiveReady` completes its first interactive render.
- If the marker doesn't report before `data-stall-timeout-ms`, the browser submits a `NavigationStalled` report with the last recorded navigation phase.

`NTBlazorInteractiveReady` deliberately has no render mode and renders no HTML. It inherits the renderer used by its containing page or component. `OnAfterRenderAsync` doesn't execute during prerendering, and the marker reports readiness only when `RendererInfo.IsInteractive` is true.

Only use the link attribute when the destination contains the marker beneath an `InteractiveServer`, `InteractiveWebAssembly`, or `InteractiveAuto` boundary. A marker in a static SSR tree can't report interactive readiness. The marker signals once per component instance, so parameter-only navigation that reuses the same instance requires a destination-specific readiness strategy.

## Browser telemetry data

Browser reports can include:

- Application version and browser session identifier.
- Boundary name, report kind, exception type, message, details, and stack trace.
- Current, origin, and target URIs with query strings and fragments removed.
- Navigation identifier, phase, interception state, and recent breadcrumbs.
- Renderer name and interactive state.
- JavaScript source, line, and column for browser errors.
- Browser online state.

String fields are truncated before logging according to `NTBlazorErrorBoundaryHttpClientOptions.MaxReportFieldLength`.

## TypeScript static asset

The browser source of truth is `NT.Blazor.ErrorBoundary/wwwroot/nt-blazor-error-boundary.ts`. Don't edit the generated JavaScript directly.

```shell
npm ci
npm run check
npm run build
```

Building `NT.Blazor.ErrorBoundary` automatically restores the locked npm dependencies when necessary, compiles the TypeScript, and minifies the result. The generated `nt-blazor-error-boundary.js` is packed and published as the static web asset. The TypeScript source is neither packed nor published.


## Development

The repository uses the SDK selected by `global.json` and builds both .NET 9 and .NET 10 targets.

```shell
dotnet restore ./NT.Blazor.ErrorBoundary.slnx
dotnet build ./NT.Blazor.ErrorBoundary.slnx --configuration Release --no-restore
dotnet test --solution ./NT.Blazor.ErrorBoundary.slnx --configuration Release --no-build --no-restore
```

Tests use xUnit v3 on Microsoft.Testing.Platform v2. The .NET 10 SDK selects Microsoft.Testing.Platform through `global.json`; the test projects do not depend on VSTest, `Microsoft.NET.Test.Sdk`, or Coverlet.

Generate Cobertura coverage with Microsoft's testing-platform coverage extension:

```shell
dotnet test --project ./Tests/NT.Blazor.ErrorBoundary.Tests/NT.Blazor.ErrorBoundary.Tests.csproj --framework net10.0 --configuration Release --no-build --no-restore --results-directory ./artifacts/coverage/core --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
dotnet test --project ./Tests/NT.Blazor.ErrorBoundary.AspNetCore.Tests/NT.Blazor.ErrorBoundary.AspNetCore.Tests.csproj --framework net10.0 --configuration Release --no-build --no-restore --results-directory ./artifacts/coverage/aspnetcore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

The full suite validates both target frameworks. Coverage runs once against .NET 10 for each test project so parallel target frameworks cannot overwrite a shared output file. The `Build and Pack` workflow uploads both reports as the `code-coverage` artifact.

Warnings are treated as errors across the repository. Packable projects generate XML documentation, so missing or invalid public API documentation also fails the build.

The library build also runs the TypeScript compilation and minification pipeline. Run `npm run check` separately for a no-emit TypeScript validation.


Create local packages with:

```shell
dotnet pack ./NT.Blazor.ErrorBoundary/NT.Blazor.ErrorBoundary.csproj --configuration Release --no-build --output ./artifacts/packages
dotnet pack ./NT.Blazor.ErrorBoundary.AspNetCore/NT.Blazor.ErrorBoundary.AspNetCore.csproj --configuration Release --no-build --output ./artifacts/packages
```

## CI and releases

The `Build and Pack` workflow runs for pull requests and manual validation. It builds, tests, creates both NuGet packages and symbol packages, and uploads them as workflow artifacts.

Publication follows the same preview and stable semantic-release model as NTComponents.

1. Add the NuGet.org API key as the `NUGET_API_KEY` GitHub Actions secret.
2. Merge release-worthy Conventional Commits into `main`.
3. `Publish Prerelease` builds and tests the repository automatically.
4. Semantic-release updates the `preview` branch and creates a `vX.Y.Z-preview.N` tag and GitHub prerelease.
5. The same workflow packs both packages with that preview version and publishes them and their symbols to NuGet.org.
6. When the preview is ready, run the manual `Release` workflow from GitHub Actions.
7. The release workflow builds and tests again, creates the stable `vX.Y.Z` tag and GitHub release, and publishes both stable packages.
8. After stable publication succeeds, the workflow unlists prerelease versions whose base version is at or below the stable release.

Release calculation follows Conventional Commits:

- `fix:` creates a patch release.
- `feat:` creates a minor release.
- `BREAKING CHANGE:` creates a major release.
- Documentation, CI, and chore-only changes do not create a release by default.

Do not create release tags manually. If a push to `main` contains no release-worthy commits, semantic-release creates no preview tag and prerelease publication is skipped. If the manual stable release has no release-worthy commits, no stable package is published.

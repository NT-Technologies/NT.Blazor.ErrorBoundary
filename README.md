# NT.Blazor.ErrorBoundary

Reusable Blazor error-boundary reporting with a WebAssembly-safe core package and optional ASP.NET Core logging integration. The component renders generic fallback UI by default and reports failures through `ILogger` or a configurable HTTP endpoint without depending directly on Application Insights or OpenTelemetry SDKs.

## Packages

| Package | Use it for |
| --- | --- |
| `NT.Blazor.ErrorBoundary` | The `NTErrorBoundary` component, shared reporting contracts, and HTTP reporting from Blazor WebAssembly. |
| `NT.Blazor.ErrorBoundary.AspNetCore` | Direct server-side exception logging and an ASP.NET Core endpoint that receives WebAssembly error reports. |

Both packages target .NET 10. The ASP.NET Core package depends on the core package, so installing it in a server project provides both. Install the core package directly in a separate `.Client` or standalone WebAssembly project.

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

builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

app.MapNTBlazorErrorBoundaryTelemetry();

app.Run();
```

`AddNTBlazorErrorBoundaryServerLogging` performs four registrations:

- It registers `NTBlazorServerErrorReporter` as the scoped `INTBlazorErrorReporter` used during server rendering.
- It stores the report path used by `MapNTBlazorErrorBoundaryTelemetry` and `NTBlazorErrorBoundaryScript`.
- It reads `IHostEnvironment` so exception details are rendered by default only in Development.
- It registers a circuit handler that logs Blazor Server circuit opened, closed, connection down, and connection up events with a `BlazorCircuitId` scope value, and adds that ID to server-rendered error logs.

`MapNTBlazorErrorBoundaryTelemetry` maps a JSON `POST` endpoint at the configured path. Accepted reports return `204 No Content` and are written through `ILogger`. Invalid reports return `400`, and oversized requests return `413` (see [Securing the telemetry endpoint](#securing-the-telemetry-endpoint)).

The previous names `AddBlazorErrorBoundaryServerLogging` and `MapBlazorErrorBoundaryTelemetry` still work but are obsolete.

If the application never uses a WebAssembly render mode, calling `MapNTBlazorErrorBoundaryTelemetry` is optional. Server-side exceptions are logged directly and do not make an HTTP round trip.

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

The reporter sends each report with this `HttpClient`, so any authorization handler applies. If the request fails with a network error, `5xx`, `408`, or `429`, the report is handed to the browser script's retry queue, which resends it on the next page load, when connectivity returns, or with a page-hide beacon. Other `4xx` responses mean the server rejected the report, and it is not retried.

The client registration also reports unhandled exceptions that occur outside any `NTErrorBoundary`, such as exceptions from components that no boundary wraps, which Blazor otherwise only shows as its "An unhandled error has occurred" bar. They are reported with the Blazor log category as the boundary name. Set `NTBlazorErrorBoundaryHttpClientOptions.ReportUnhandledExceptions` to `false` to turn this off.

### 3. Load browser telemetry

Add the script component before `blazor.web.js` in `App.razor`:

```razor
@using NT.Blazor.ErrorBoundary.Components

<NTBlazorErrorBoundaryScript ApplicationVersion="1.2.3" StallTimeoutMs="15000" />
<script src="@Assets["_framework/blazor.web.js"]" defer></script>
```

`NTBlazorErrorBoundaryScript` renders the package's fingerprinted `<script>` tag with the report URI registered by `AddNTBlazorErrorBoundaryServerLogging`, so the path is configured in one place on the server. It also writes the current request's W3C `traceparent` into the tag so browser reports correlate with the server request that rendered the page. `ApplicationVersion` and `StallTimeoutMs` are optional; the stall timeout defaults to 15 seconds.

To load the script by hand instead, use `data-report-uri` (required), `data-application-version`, `data-stall-timeout-ms`, and `data-traceparent` on a `<script src="@Assets["_content/NT.Blazor.ErrorBoundary/nt-blazor-error-boundary.js"]">` tag. The script doesn't initialize when `data-report-uri` is absent.

Browser telemetry:

- Adds browser and navigation context to boundary reports.
- Captures JavaScript errors and unhandled promise rejections.
- Reports Blazor framework files (`_framework/*`, `blazor.*.js`) that fail to load, for example after a deployment replaces them. Other failed resources, such as images, are ignored.
- Reports Blazor Server reconnection that fails, fails to resume, or is rejected. With a custom reconnect UI (the `#components-reconnect-modal` element, as in the .NET 10 Blazor Web App template), all three states are detected. With the framework's built-in reconnect UI, failure is detected from its status text; on rejection the built-in UI reloads the page, so there is nothing to report.
- Stores up to 20 recent navigation breadcrumbs in `sessionStorage`.
- Gives every report a reference ID, and sends the page's `traceparent` as a request header and report field.
- Drops duplicate reports raised within 5 seconds, so a crash loop can't flood the endpoint.
- Queues failed report submissions and retries them when connectivity returns. Reports the server rejects with a `4xx` other than `408` or `429` are not retried.
- Uses keepalive requests and a page-hide beacon when available.
- Never interrupts application behavior when telemetry storage or delivery fails.

Reports raised by the browser script itself use the browser's `fetch` with same-origin credentials, so cookie authentication applies but an `HttpClient` authorization handler does not. Allow browser reports through the endpoint's authorization policy, or use cookie authentication, if you want JavaScript errors, stalls, and load failures.


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
- Displays a short reference ID, such as `Reference: 7F3K2QAB`. The same ID is logged as `BlazorErrorReferenceId`, so support can find the exact report a user saw.
- Hides exception details unless they are explicitly enabled or the environment is Development.
- Displays a **Try again** button for interactive rendering.
- Displays a **Reload page** link for static rendering.
- Attempts to report the error without allowing reporting failures to replace the fallback UI.

The boundary also clears its error when the user navigates, so a boundary around the layout body doesn't keep showing the fallback on the next page. Set `RecoverOnNavigation="false"` to keep the error until **Try again**.

The default fallback's scoped styles ship in the package's CSS bundle, which the host loads through its `{AssemblyName}.styles.css` link included by the Blazor Web App template.

### Fallback text and theming

Every fallback string is a parameter, so apps can pass localized text, for example from `IStringLocalizer`:

```razor
<NTErrorBoundary Title="@L["ErrorTitle"]"
                 Message="@L["ErrorMessage"]"
                 ReferenceLabel="@L["Reference"]"
                 RetryText="@L["TryAgain"]"
                 ReloadText="@L["ReloadPage"]"
                 HeadingLevel="3">
    @Body
</NTErrorBoundary>
```

`HeadingLevel` (1–6, default 2) sets the title's `aria-level`, so the fallback fits the page's heading structure.

The fallback reads these CSS custom properties and falls back to the defaults below when they are unset. Set them on any ancestor in the app's own stylesheet, for example `:root { --nt-error-boundary-accent: #0b57d0; }`:

| Property | Default |
| --- | --- |
| `--nt-error-boundary-accent` | `#b42318` |
| `--nt-error-boundary-background` | `#fff4f4` |
| `--nt-error-boundary-color` | `#5f1414` |
| `--nt-error-boundary-button-color` | `#ffffff` |
| `--nt-error-boundary-radius` | `0` |
| `--nt-error-boundary-spacing` | `0.5rem` |

Its elements use the classes `nt-blazor-error-boundary`, `nt-blazor-error-title`, `nt-blazor-error-message`, `nt-blazor-error-reference`, and `nt-blazor-error-details`.

## Interactive Server-only setup

For an application that never activates WebAssembly, only the server registration and component are required.

```csharp
builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");
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
builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");

var app = builder.Build();

app.MapNTBlazorErrorBoundaryTelemetry();
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

Supply `FallbackContent` to replace the package's default fallback UI. Its context is an `NTErrorBoundaryFailure` with the `Exception` and the `ReferenceId`:

```razor
@using NT.Blazor.ErrorBoundary.Components

<NTErrorBoundary>
    <ChildContent>
        @Body
    </ChildContent>
    <FallbackContent Context="failure">
        <section role="alert">
            This section is temporarily unavailable. Reference: @failure.ReferenceId
        </section>
    </FallbackContent>
</NTErrorBoundary>
```

Blazor's inherited `ErrorContent` parameter also still works, but it receives only the exception. When both are set, `FallbackContent` wins.

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
- `BlazorCircuitId`, when the error occurred in a Blazor Server circuit
- `BlazorErrorReferenceId`
- `BlazorIsInteractive`
- `BlazorOriginUri`
- `BlazorRenderMode`
- `BlazorUri`

URIs are logged without query strings or fragments, on the server path as well as the browser path.

Reports received from the browser include `BlazorBoundaryName`, `BlazorErrorReferenceId`, `BlazorIsInteractive`, `BlazorOriginUri`, `BlazorRenderMode`, and `BlazorUri`, plus:

- `BlazorExceptionDetails`
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
- `BlazorPageTraceParent`, the `traceparent` of the request that rendered the page
- `BlazorReportKind`
- `BlazorTargetUri`
- `RequestId`
- `RequestMethod`
- `RequestPath`
- `UserAgent`
- `TraceId` and `SpanId`, when an `Activity` is active
- `UserId`, when a `ClaimTypes.NameIdentifier` or `sub` claim is present

The HTTP reporter treats network and non-success response failures as reporting failures, logs a warning, and does not rethrow them into the component tree. Cancellation requested by the caller is preserved.

Blazor Server circuit events are logged at `Information` (opened, closed, connection up) and `Warning` (connection down) with a `BlazorCircuitId` scope value.

### Metrics

The server package records the counter `nt.blazor.error_boundary.errors` on the meter `NT.Blazor.ErrorBoundary` (`NTBlazorErrorBoundaryAspNetCoreExtensions.MeterName`), once per logged report. Its tags are `source` (`server` or `client`), `report_kind`, and, for server-rendered errors only, `boundary`. Client reports are never tagged with a boundary name, because browser-supplied values would allow unbounded tag cardinality. Reports that are rejected or dropped by a filter are not counted.

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(NTBlazorErrorBoundaryAspNetCoreExtensions.MeterName)
        .AddOtlpExporter());
```

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

builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");

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
app.MapNTBlazorErrorBoundaryTelemetry();
// Keep the app's existing Blazor endpoints and middleware here.
app.Run();
```

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to your collector endpoint. For example, `http://localhost:4317` with `OTEL_EXPORTER_OTLP_PROTOCOL=grpc` uses OTLP over gRPC. The report URI (`/api/blazor-errors`) is a separate application endpoint accepting the package's JSON reports; it is not an OTLP collector endpoint. For WebAssembly, configure the client reporter and browser bridge with that same report URI as described above.

If the host already configures OpenTelemetry (for example through shared service defaults), update that registration instead of adding another exporter pipeline. Ensure that logging is enabled as well as tracing, that filters allow `Error` logs from `NT.Blazor.ErrorBoundary`, and that `IncludeScopes` is enabled. Most boundary, browser, and request fields are scope values; `IncludeFormattedMessage` additionally preserves the readable message. See the official [logging configuration](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/logs/customizing-the-sdk/README.md) and [log correlation](https://opentelemetry.io/docs/languages/dotnet/logs/correlation/) documentation.

Current correlation and exception behavior:

- Server reports retain the original exception. OpenTelemetry populates the log's trace and span IDs when an `Activity` is active. The library does not create an activity for interactive callbacks that lack one.
- Browser reports carry the `traceparent` of the request that rendered the page, written by `NTBlazorErrorBoundaryScript`. Reports sent with `fetch` or `HttpClient` send it as a `traceparent` header, so ASP.NET Core instrumentation makes the upload span part of the page request's trace; beacon uploads, which can't set headers, carry it only in `BlazorPageTraceParent`. Cross-origin endpoints must allow the `traceparent` request header through CORS. `BlazorClientSessionId` and `BlazorNavigationId` remain searchable diagnostic fields.
- Browser exceptions are logged as `ClientBlazorErrorReportException`, whose `Message`, `StackTrace`, and `ToString()` return the original browser or WebAssembly values, so exporters' standard exception message and stack fields carry the original data. Only the exception type is the wrapper's; the original type is in `BlazorExceptionType`.
- A successful upload returns HTTP 204 even though its log describes an error. The library does not mark the upload span as failed. Queued reports retain their occurrence time in `BlazorOccurredAtUtc`; the server log is emitted when the report arrives.

The integration tests use the real OpenTelemetry SDK and in-memory log/trace exporters to verify both reporting paths on every target framework. They cover scope preservation, exception data, and correlation with an exported span, including an HTTP request through the mapped endpoint. They do not validate collector connectivity or backend-specific exception grouping.

## Securing the telemetry endpoint

The package intentionally does not choose authorization, CORS, rate-limiting, or telemetry-retention policy for the host. The mapped endpoint accepts exception messages and stack traces, so treat it as diagnostic ingestion rather than a general public API.

`MapNTBlazorErrorBoundaryTelemetry` returns a `RouteHandlerBuilder`, allowing normal ASP.NET Core endpoint policy:

```csharp
app.MapNTBlazorErrorBoundaryTelemetry()
    .RequireAuthorization("BlazorTelemetry")
    .RequireRateLimiting("blazor-telemetry");
```

The endpoint applies these limits itself:

- Request bodies over `NTBlazorErrorBoundaryHttpClientOptions.MaxReportRequestBytes` (default 1 MiB) are rejected with `413`.
- Reports with an unknown `ReportKind`, or an `OccurredAtUtc` more than 5 minutes in the future or older than 7 days, are rejected with `400`. Register a `TimeProvider` in DI to control the clock in tests.
- String fields are truncated to `MaxReportFieldLength`.

Use `FilterReport` to redact or drop reports. The client applies it before sending, and the server applies it before logging browser reports and server-rendered errors. Return a modified copy to redact, or `null` to drop:

```csharp
builder.Services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
    options.FilterReport = report => report.ExceptionType == "MyApp.ExpectedException"
        ? null
        : report;
});
```

For server-rendered errors, the filter controls whether the error is logged and the scope values logged with it, but the original `Exception` object is still logged unchanged; apply message redaction for it in the logging provider.

If the filter throws, the report is dropped rather than sent or logged unfiltered, and a warning with the filter's exception is logged instead. The endpoint still answers `204`, so browsers do not retry the report.

Choose policies that match the application:

- Require authorization when browser reports are sent with an authenticated identity.
- Configure the client reporter to attach the corresponding cookie or bearer token.
- Apply rate limits to prevent telemetry abuse.
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
NTErrorBoundary (or an unhandled exception outside any boundary)
    -> NTHttpClientBlazorErrorReporter
    -> POST /api/blazor-errors (HttpClient; on retryable failure, the browser retry queue)
    -> MapNTBlazorErrorBoundaryTelemetry
    -> ILogger
```

Browser-detected problem (JavaScript error, stalled navigation, failed framework load, failed reconnection):

```text
nt-blazor-error-boundary.js
    -> POST /api/blazor-errors (fetch; on retryable failure, the browser retry queue)
    -> MapNTBlazorErrorBoundaryTelemetry
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

- Without the attribute, the navigation is recorded as breadcrumbs only, and no stall report is scheduled.
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
npm test
```

`npm test` runs the release-tooling tests and the browser-script tests in `Tests/ClientAssets`, which load the built `nt-blazor-error-boundary.js` in a sandbox with fake browser APIs.

Building `NT.Blazor.ErrorBoundary` automatically restores the locked npm dependencies when necessary, compiles the TypeScript, and minifies the result. The generated `nt-blazor-error-boundary.js` is packed and published as the static web asset. The TypeScript source is neither packed nor published.


## Development

The repository uses the SDK selected by `global.json` and builds every target framework.

```shell
dotnet restore ./NT.Blazor.ErrorBoundary.slnx
dotnet build ./NT.Blazor.ErrorBoundary.slnx --configuration Release --no-restore
dotnet test --solution ./NT.Blazor.ErrorBoundary.slnx --configuration Release --no-build --no-restore
```

Tests use xUnit v3 on Microsoft.Testing.Platform v2. The .NET 10 SDK selects Microsoft.Testing.Platform through `global.json`; the test projects do not depend on VSTest, `Microsoft.NET.Test.Sdk`, or Coverlet.

`samples/` contains a small Blazor Web App (server and WebAssembly client) wired exactly as this README describes, with pages that fail under static SSR, Interactive Server, and Interactive WebAssembly. `Tests/NT.Blazor.ErrorBoundary.EndToEnd.Tests` runs it on Kestrel and drives it with Playwright's Chromium to verify the fallback, reference IDs in server logs, recovery, unhandled WebAssembly exceptions, JavaScript errors, stall detection, and the scoped styles. Install the browser once after building:

```shell
pwsh ./Tests/NT.Blazor.ErrorBoundary.EndToEnd.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Without the browser, the end-to-end tests are skipped. Set `NT_E2E_REQUIRED=true` to make them fail instead, as the `Build and Pack` workflow does.

Generate Cobertura coverage with Microsoft's testing-platform coverage extension:

```shell
dotnet test --project ./Tests/NT.Blazor.ErrorBoundary.Tests/NT.Blazor.ErrorBoundary.Tests.csproj --framework net10.0 --configuration Release --no-build --no-restore --results-directory ./artifacts/coverage/core --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
dotnet test --project ./Tests/NT.Blazor.ErrorBoundary.AspNetCore.Tests/NT.Blazor.ErrorBoundary.AspNetCore.Tests.csproj --framework net10.0 --configuration Release --no-build --no-restore --results-directory ./artifacts/coverage/aspnetcore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

The full suite runs on every target framework. Coverage runs once against .NET 10 for each test project so parallel target frameworks cannot overwrite a shared output file. The `Build and Pack` workflow uploads both reports as the `code-coverage` artifact.

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

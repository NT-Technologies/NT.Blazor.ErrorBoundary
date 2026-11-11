# NT.Blazor.ErrorBoundary

Reusable Blazor error-boundary reporting with a WebAssembly-safe core package, an ASP.NET Core logging integration, and an optional Application Insights add-on. The component renders generic fallback UI by default and reports failures through `ILogger` or a configurable HTTP endpoint. The core and ASP.NET Core packages don't depend on Application Insights or the OpenTelemetry SDK; only the optional `NT.Blazor.ErrorBoundary.AzureMonitor` add-on does.

## Packages

| Package | Use it for |
| --- | --- |
| `NT.Blazor.ErrorBoundary` | The `NTErrorBoundary` component, shared reporting contracts, and HTTP reporting from Blazor WebAssembly. |
| `NT.Blazor.ErrorBoundary.AspNetCore` | Direct server-side exception logging and an ASP.NET Core endpoint that receives WebAssembly error reports. |
| `NT.Blazor.ErrorBoundary.AzureMonitor` | Optional. Sends every error and event to Application Insights directly, as native exceptions the Failures view groups and as custom events, unaffected by trace sampling. See [Why the add-on](#why-the-add-on). |

All packages target .NET 10 and .NET 11. The ASP.NET Core package depends on the core package, so installing it in a server project provides both. Install the core package directly in a separate `.Client` or standalone WebAssembly project.

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

## Upgrading from 1.x

Version 2.0.0 changes target frameworks, log field names, and some defaults. Work through this list before deploying.

- **.NET 9 is no longer supported.** All packages target .NET 10 and .NET 11.
- **The ASP.NET Core package has a new dependency**, `OpenTelemetry.Api.ProviderBuilderExtensions` 1.15.3 or later. It registers this package's meter and activity source with the host's OpenTelemetry pipeline, and does not pull in the OpenTelemetry SDK.
- **Obsolete names now warn.** `AddBlazorErrorBoundaryServerLogging` and `MapBlazorErrorBoundaryTelemetry` still work but raise `CS0618`, which fails builds that treat warnings as errors. Use `AddNTBlazorErrorBoundaryServerLogging` and `MapNTBlazorErrorBoundaryTelemetry`.
- **Server log fields moved from logging scopes to attributes on the log entry, and were renamed.** Update Application Insights queries, workbooks, and alerts. For example, `customDimensions.BlazorBoundaryName` becomes `customDimensions['blazor.boundary.name']`. The log message templates also use the new names as placeholders, such as `{blazor.report.kind}` in place of `{ReportKind}` and `{blazor.boundary.name}` in place of `{BoundaryName}`. Version 1.x wrote only the first five fields in the table for server-rendered errors; the rest applied to browser reports only.

  | 1.x scope key | 2.0.0 attribute |
  | --- | --- |
  | `BlazorBoundaryName` | `blazor.boundary.name` |
  | `BlazorIsInteractive` | `blazor.interactive` |
  | `BlazorOriginUri` | `blazor.navigation.origin_url` |
  | `BlazorRenderMode` | `blazor.render_mode` |
  | `BlazorUri` | `url.full` |
  | `BlazorBreadcrumbs` | `blazor.navigation.breadcrumbs` |
  | `ApplicationVersion` | `blazor.app.version` |
  | `BlazorClientSessionId` | `session.id` and `microsoft.session.id` |
  | `BlazorExceptionDetails` | `blazor.exception.details` |
  | `BlazorExceptionMessage` | `blazor.exception.message` |
  | `BlazorExceptionStackTrace` | `blazor.exception.stacktrace` |
  | `BlazorExceptionType` | `blazor.exception.type` |
  | `BlazorIsNavigationIntercepted` | `blazor.navigation.intercepted` |
  | `BlazorIsOnline` | `blazor.client.online` |
  | `BlazorJavaScriptColumn` | `code.column.number` |
  | `BlazorJavaScriptLine` | `code.line.number` |
  | `BlazorJavaScriptSource` | `code.file.path` |
  | `BlazorNavigationId` | `blazor.navigation.id` |
  | `BlazorNavigationPhase` | `blazor.navigation.phase` |
  | `BlazorOccurredAtUtc` | `blazor.error.occurred_at` |
  | `BlazorReportKind` | `blazor.report.kind` |
  | `BlazorTargetUri` | `blazor.navigation.target_url` |
  | `UserAgent` | `user_agent.original` |
  | `UserId` | `user.id` and `enduser.id` |
  | `RequestId`, `RequestMethod`, `RequestPath` | No replacement. ASP.NET Core's own request logs and traces carry them. |
  | `TraceId`, `SpanId` | No replacement. The log entry's trace context carries them. |

  The new attributes `blazor.error.fingerprint`, `blazor.error.reference_id`, and `blazor.page.traceparent` have no 1.x equivalent. See [Log fields](#log-fields).
- **`NavigationStalled` reports are logged as events without an exception.** They no longer appear in the `exceptions` table. Query `customEvents` for `blazor.navigation.stalled` instead.
- **Browser exceptions are logged as `NT.Blazor.ErrorBoundary.AspNetCore.ClientBlazorErrorReportException`.** In 1.x this was a nested type of the extensions class. Its `Message` is now the browser's original message, so Application Insights problem IDs for browser errors change.
- **The fallback markup changed.** The heading is a `div` with `role="heading"` and an `aria-level` instead of an `h2`, the details class is `nt-blazor-error-details` instead of `nt-blazor-error-boundary__details`, and a reference line is shown. Update any CSS or tests that depend on the old markup.
- **The telemetry endpoint validates reports.** It rejects unknown report kinds and out-of-range timestamps with `400`, and request bodies over `MaxReportRequestBytes` with `413`.
- **Some defaults now report or recover more.**
  - `NTErrorBoundary.RecoverOnNavigation` is `true`. Set it to `false` to keep the previous behavior.
  - `NTBlazorErrorBoundaryHttpClientOptions.ReportUnhandledExceptions` is `true`, so WebAssembly clients report exceptions that occur outside any boundary. Expect more reports.
  - Blazor Server circuit connection down and up events are logged.
  - Query strings and fragments are removed from reported URLs.
- **`NTBlazorErrorReport` is a record.** It has value equality and supports `with`. Its `ToString()` prints only the kind, exception type, and reference ID.

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

`AddNTBlazorErrorBoundaryServerLogging` does the following:

- It registers `NTBlazorServerErrorReporter` as the scoped `INTBlazorErrorReporter` used during server rendering.
- It stores the report path used by `MapNTBlazorErrorBoundaryTelemetry` and `NTBlazorErrorBoundaryScript`.
- It reads `IHostEnvironment` so exception details are rendered by default only in Development.
- It registers a circuit handler that logs Blazor Server connection down and connection up events with `aspnetcore.components.circuit.id`, and adds that ID to server-rendered error logs. Circuit opens and closes are not logged; ASP.NET Core's `aspnetcore.components.circuit.active` and `aspnetcore.components.circuit.duration` metrics cover them (see [Blazor's built-in telemetry](#blazors-built-in-telemetry)).
- It registers this package's meter and activity source with the host's OpenTelemetry pipeline, if the host configures one.

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

The reporter sends each report with this `HttpClient`, so any authorization handler applies. Reports up to 64,000 bytes are sent with the browser's `keepalive` fetch option, so an upload still in flight when the page navigates away is finished by the browser instead of lost; the browser script does the same for its own uploads. Browsers refuse `keepalive` for larger bodies, so larger reports are sent without it. If the request fails with a network error, `5xx`, `408`, or `429`, the report is handed to the browser script's retry queue, which resends it on the next page load, when connectivity returns, or with a page-hide beacon. Other `4xx` responses mean the server rejected the report, and it is not retried.

The client registration also reports unhandled exceptions that occur outside any `NTErrorBoundary`, such as exceptions from components that no boundary wraps, which Blazor otherwise only shows as its "An unhandled error has occurred" bar. They are reported with the Blazor log category as the boundary name. Set `NTBlazorErrorBoundaryHttpClientOptions.ReportUnhandledExceptions` to `false` to turn this off.

### 3. Load browser telemetry

Add the script component before `blazor.web.js` in `App.razor`:

```razor
@using NT.Blazor.ErrorBoundary.Components

<NTBlazorErrorBoundaryScript StallTimeoutMs="15000" />
<script src="@Assets["_framework/blazor.web.js"]" defer></script>
```

`NTBlazorErrorBoundaryScript` renders the package's fingerprinted `<script>` tag with the report URI registered by `AddNTBlazorErrorBoundaryServerLogging`, so the path is configured in one place on the server. It also writes the current request's W3C `traceparent` into the tag so browser reports correlate with the server request that rendered the page. `StallTimeoutMs` is optional and defaults to 15 seconds. The tag also carries the server app's version, which reports the browser script raises, such as JavaScript errors and stalled navigations, are sent with.

To load the script by hand instead, use `data-report-uri` (required), `data-application-version`, `data-stall-timeout-ms`, and `data-traceparent` on a `<script src="@Assets["_content/NT.Blazor.ErrorBoundary/nt-blazor-error-boundary.js"]">` tag. The script doesn't initialize when `data-report-uri` is absent.

Browser telemetry:

- Adds browser and navigation context to boundary reports.
- Captures JavaScript errors and unhandled promise rejections.
- Reports Blazor framework files (`_framework/*`, `blazor.*.js`) that fail to load, for example after a deployment replaces them. Other failed resources, such as images, are ignored.
- Reports Blazor Server reconnection that fails, fails to resume, or is rejected. With a custom reconnect UI (the `#components-reconnect-modal` element, as in the .NET 10 Blazor Web App template), all three states are detected. With the framework's built-in reconnect UI, failure is detected from its status text; on rejection the built-in UI reloads the page, so there is nothing to report.
- Stores up to 20 recent navigation breadcrumbs in `sessionStorage`.
- Gives every report a reference ID, and sends the page's `traceparent` as a request header and report field.
- Drops a report that repeats an earlier one (same kind, exception type, message, boundary, and source location) within 5 seconds of its last occurrence. Every repeat restarts the window, so a crash loop is sent once and stays suppressed until it pauses for 5 seconds. This keeps it from flooding the endpoint.
- Queues failed report submissions and retries them when connectivity returns. Reports the server rejects with a `4xx` other than `408` or `429` are not retried.
- Remembers the reference IDs of the last 10 delivered reports for the browser session, so a report is never sent twice. This covers an upload still in flight when the page navigates away: the browser completes it, but the page sees it fail, and without this the next page would resend it. A page entering the back-forward cache may come back, so its uploads are left to settle normally instead.
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

Give a boundary a `Name` to tell its errors apart. The name is logged as `blazor.boundary.name` and tags the `blazor.errors` metric:

```razor
<NTErrorBoundary Name="Checkout">
    <CheckoutForm />
</NTErrorBoundary>
```

When a descendant throws, the default fallback:

- Displays a generic message.
- Displays a short reference ID, such as `Reference: 7F3K2QAB`. The same ID is logged as `blazor.error.reference_id`, so support can find the exact report a user saw.
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

## Custom reporters

`NTErrorBoundary` reports through whichever `INTBlazorErrorReporter` is registered. `AddNTBlazorErrorBoundaryServerLogging` registers one that logs on the server, and `AddNTBlazorErrorBoundaryHttpClient` registers one that posts to the endpoint. To send errors somewhere else, register your own implementation after the built-in call; the last registration wins.

Replace the reporter only to change where errors go. Your reporter then takes over everything the built-in one does:

- Server: the log entry and its fields (including `blazor.error.fingerprint` and the circuit ID), the `blazor.errors` metric, and the call to `INTBlazorErrorBoundaryExporter`.
- Client: the HTTP upload, `keepalive`, the browser retry queue, and the browser navigation context.
- Both: `FilterReport` and `MaxReportFieldLength`, which only the built-in reporters apply.

The registration calls still do their other work: the report path, the exception-detail environment, the circuit handler, and, on the client, forwarding of exceptions that no boundary catches. To send errors to a second destination and keep the log, register an [`INTBlazorErrorBoundaryExporter`](#sending-errors-to-another-backend) instead. To redact or drop reports, use [`FilterReport`](#securing-the-telemetry-endpoint).

`ReportAsync` receives the exception and an `NTBlazorErrorBoundaryContext`:

- `BoundaryName`: the boundary's `Name`, or its type name.
- `ReferenceId`: the reference the fallback shows.
- `IsInteractive` and `RenderMode`: how the boundary rendered.
- `Uri`: the current page URL.
- `OriginUri`: the URL when the boundary was created.

Exceptions forwarded from outside any boundary carry only `BoundaryName`, which is then the Blazor log category, and `ReferenceId`. The boundary does not wait for `ReportAsync` before it shows the fallback. If `ReportAsync` throws, the boundary logs a warning, and the fallback is unaffected.

`NTBlazorErrorReport.FromException` builds the same report the built-in reporters send. It sets the app version, removes query strings and fragments from URLs, and truncates string fields. Apply `FilterReport` yourself to keep honoring it:

```csharp
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;

// IErrorQueue stands for the application's own transport.
public sealed class QueueErrorReporter(IErrorQueue _queue, IOptions<NTBlazorErrorBoundaryHttpClientOptions> _options) : INTBlazorErrorReporter {
    public async Task ReportAsync(Exception exception, NTBlazorErrorBoundaryContext context, CancellationToken cancellationToken = default) {
        var options = _options.Value;
        var report = NTBlazorErrorReport.FromException(exception, context, options.MaxReportFieldLength);
        if ((options.FilterReport is null ? report : options.FilterReport(report)) is { } filtered) {
            await _queue.SendAsync(filtered, cancellationToken);
        }
    }
}
```

```csharp
// Server project
builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");
builder.Services.AddScoped<INTBlazorErrorReporter, QueueErrorReporter>();

// .Client or standalone WebAssembly project
builder.Services.AddNTBlazorErrorBoundaryHttpClient("/api/blazor-errors", builder.HostEnvironment.Environment);
builder.Services.AddScoped<INTBlazorErrorReporter, QueueErrorReporter>();
```

To use the boundary with no built-in registration, for example in Blazor Hybrid, register only your reporter. Then set `NTErrorBoundaryOptions.IsDevelopmentEnvironment` yourself if `DevelopmentOnly` exception details should render in development.

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

The package writes through `ILogger` and publishes OpenTelemetry metrics and traces through `System.Diagnostics`; the consuming application decides where they go. Log fields use OpenTelemetry semantic-convention names where a convention exists, and the `blazor.*` namespace otherwise.

### OpenTelemetry host setup

Configure OpenTelemetry in the consuming ASP.NET Core host as usual. `AddNTBlazorErrorBoundaryServerLogging` adds this package's meter (`MeterName`) and activity source (`ActivitySourceName`) to whatever meter and tracer providers the host builds, so no package-specific OpenTelemetry call is needed; without an OpenTelemetry pipeline, nothing is collected.

```shell
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
```

```csharp
using NT.Blazor.ErrorBoundary.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithLogging()
    .WithMetrics()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .UseOtlpExporter();

var app = builder.Build();
app.MapNTBlazorErrorBoundaryTelemetry();
```

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to your collector endpoint. The report URI (`/api/blazor-errors`) is the application's own endpoint for the package's JSON reports, not an OTLP collector endpoint.

Fields travel as attributes on each log entry, not in logging scopes, so `IncludeScopes` is not required. If the host already configures OpenTelemetry, for example through shared service defaults, keep that pipeline; make sure it enables logging, metrics, and tracing, and that filters allow `Error` logs from the `NT.Blazor.ErrorBoundary` categories.

#### Blazor's built-in telemetry

ASP.NET Core 10 publishes its own Blazor telemetry, which complements this package's: the `Microsoft.AspNetCore.Components` and `Microsoft.AspNetCore.Components.Server.Circuits` meters (navigation, event handling, and active, connected, and total circuit duration) and the `Microsoft.AspNetCore.Components` activity source (circuit start, navigation, and event handling). They are not enabled automatically, because event-handling traces add a span per UI event. Opt in where that volume is acceptable:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Server.Circuits"))
    .WithTracing(tracing => tracing.AddSource("Microsoft.AspNetCore.Components"));
```

### Log fields

Server-rendered exceptions log the original `Exception` at `Error` with these attributes:

| Attribute | Value |
| --- | --- |
| `blazor.boundary.name` | The boundary's `Name`. |
| `blazor.error.fingerprint` | Grouping key, described below. |
| `blazor.error.reference_id` | The reference shown in the fallback UI. |
| `blazor.interactive` | Whether the boundary rendered interactively. |
| `blazor.render_mode` | The renderer name. |
| `url.full` | The current page URL. |
| `blazor.navigation.origin_url` | The page URL when the boundary was created. |
| `aspnetcore.components.circuit.id` | The circuit ID, for Blazor Server circuits. This is ASP.NET Core's own attribute name. |
| `blazor.app.version` | The version of the side that raised the error, described below. |

Reports received from the browser add:

| Attribute | Value |
| --- | --- |
| `blazor.report.kind` | `Exception`, `JavaScriptError`, `JavaScriptUnhandledRejection`, `ResourceLoadFailed`, `NavigationStalled`, or `ReconnectFailed`. |
| `blazor.exception.type`, `blazor.exception.message`, `blazor.exception.stacktrace`, `blazor.exception.details` | The original exception values. |
| `blazor.error.occurred_at` | When the error occurred in the browser; queued reports arrive later. |
| `blazor.app.version` | The version of the side that raised the error, described below. |
| `blazor.client.online` | The browser's online state. |
| `blazor.navigation.id`, `blazor.navigation.phase`, `blazor.navigation.intercepted`, `blazor.navigation.target_url`, `blazor.navigation.breadcrumbs` | Navigation context; breadcrumbs are a JSON array. |
| `blazor.page.traceparent` | The `traceparent` of the request that rendered the page. |
| `code.file.path`, `code.line.number`, `code.column.number` | Source location of a JavaScript error. |
| `user_agent.original` | The upload request's `User-Agent` header. |
| `session.id` and `microsoft.session.id` | The browser session identifier. |
| `user.id` and `enduser.id` | The `ClaimTypes.NameIdentifier` or `sub` claim, when the upload is authenticated. |

`blazor.app.version` is the version of the code that raised the error, so a client and server versioned separately each report their own:

- Server-rendered errors use the server app's version.
- WebAssembly errors use the WebAssembly app's version, read inside the browser.
- Reports the browser script raises use the server app's version, since the server renders the page and script.

Each version is read from that app's entry assembly: its informational version, which the .NET SDK sets to the project's `<Version>` and the Git commit it was built from, such as `1.4.0+3f2a9c1…`. Set `<Version>` in each project to version them separately. A build without Git history gets the project version alone, and a project that sets `IncludeSourceRevisionInInformationalVersion` to `false` gets it without the commit.

The session and user values are written under two names because the Azure Monitor exporter reads only `microsoft.session.id` and `enduser.id`, while OpenTelemetry's conventions use `session.id` and `user.id`. The URL fields (`url.full`, `blazor.navigation.origin_url`, `blazor.navigation.target_url`, breadcrumbs, and `code.file.path`) are sent and logged without query strings or fragments; the server removes them from server-rendered errors, and the browser script and HTTP reporter remove them before sending. The endpoint removes them from every browser report it receives as well, before applying `FilterReport` and logging. Exception messages and stack traces are logged as reported and can contain full URLs, for example a failed request's URL with an access token in its query string. Scrub them with [`FilterReport`](#securing-the-telemetry-endpoint) if that matters for your app. String fields are truncated to `MaxReportFieldLength`.

`JavaScriptError`, `JavaScriptUnhandledRejection`, `ResourceLoadFailed`, and WebAssembly `Exception` reports are logged at `Error` with an exception. Browser exceptions are logged as `ClientBlazorErrorReportException`, whose `Message`, `StackTrace`, and `ToString()` return the original values; exporters that read those members, including OTLP's `exception.message` and `exception.stacktrace`, carry the original data, while `exception.type` is the wrapper's name and the original type is in `blazor.exception.type`.

`NavigationStalled` and `ReconnectFailed` describe a condition rather than a thrown error, so they are logged at `Error` without an exception, as events. Blazor Server circuit connection changes are logged the same way, at `Warning` (connection down) or `Information` (connection up), with `aspnetcore.components.circuit.id`. Each event's `EventId` name, which OpenTelemetry exporters send as the log's event name, is also written to `microsoft.custom_event.name`, which Azure Monitor turns into a `customEvents` row:

| Event name | Logged for |
| --- | --- |
| `blazor.navigation.stalled` | A `NavigationStalled` report. |
| `blazor.reconnect.failed` | A `ReconnectFailed` report. |
| `blazor.circuit.connection_down`, `blazor.circuit.connection_up` | A circuit's connection dropping or returning. |

`blazor.error.fingerprint` is a 16-character hexadecimal key for grouping occurrences of the same error. It hashes the report kind, the exception type, and the top five stack frames after removing file paths, line and column numbers, URL origins, query strings, and native offsets, so it stays the same across hosts and cache-busting URLs. It also stays the same across builds: content hashes in bundled file names (such as `blazor.web.xmryko9e6j.js`) and the numbers the C# compiler gives async state machines, lambdas, closures, and local functions (such as `<Load>d__5` or `<>c__DisplayClass0_0`) are removed, because they change whenever unrelated code does. When a stack has no frames, the exception message is hashed instead.

The HTTP reporter treats network and non-success response failures as reporting failures, logs a warning, and does not rethrow them into the component tree. Cancellation requested by the caller is preserved.

### Metrics

The server package records the counter `blazor.errors` (unit `{error}`) on the meter `NT.Blazor.ErrorBoundary` (`NTBlazorErrorBoundaryAspNetCoreExtensions.MeterName`), once per logged report. Its attributes are `blazor.error.source` (`server` or `client`), `blazor.report.kind`, and, for server-rendered errors only, `blazor.boundary.name`. Client reports are never tagged with a boundary name, because browser-supplied values would allow unbounded attribute cardinality. Reports that are rejected or dropped by a filter are not counted.

### Trace correlation

- Server-rendered errors are logged inside the current `Activity`, if any; the library does not start one for them.
- Browser reports sent with `fetch` or `HttpClient` carry the page's `traceparent` as a header, so ASP.NET Core instrumentation makes the upload part of the page request's trace. Cross-origin endpoints must allow the `traceparent` header through CORS.
- Beacon uploads, sent as the page closes, can't set headers. When an upload is outside the page's trace, the endpoint logs the report under a `blazor.client_report` activity parented to `blazor.page.traceparent`. When the host enables tracing, that span is exported inside the page's trace; otherwise the log still carries the page's trace ID.
- A successful upload returns HTTP 204 even though its log describes an error. The library does not mark the upload span as failed.

The integration tests use the real OpenTelemetry SDK with in-memory exporters to verify field export without scopes, exception data, trace correlation, and that the report span and counter reach a host pipeline with no package-specific registration.

### Sending errors to another backend

To deliver errors somewhere directly as well as logging them, register one `INTBlazorErrorBoundaryExporter` singleton in the server project. The package calls `ExportException` for every server-rendered exception and accepted browser exception report, and `ExportEvent` for every event in the event table above. Each call carries the same attributes that are logged, after `FilterReport` and truncation. The calls run on the reporting thread, so queue the work and send it in the background. If an exporter throws, a warning is logged and the error is still logged and answered normally. Only the last registered exporter is used. `NT.Blazor.ErrorBoundary.AzureMonitor` is an exporter. To replace logging rather than add to it, see [Custom reporters](#custom-reporters).

### Azure Monitor and Application Insights

Use the [Azure Monitor OpenTelemetry distro](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable?tabs=aspnetcore) for the host's telemetry, and add `NT.Blazor.ErrorBoundary.AzureMonitor` so this package's errors and events reach Application Insights in full:

```shell
dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore
dotnet add package NT.Blazor.ErrorBoundary.AzureMonitor
```

```csharp
using Azure.Monitor.OpenTelemetry.AspNetCore;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.AzureMonitor;

builder.Services.AddNTBlazorErrorBoundaryServerLogging("/api/blazor-errors");
builder.Services.AddNTBlazorErrorBoundaryAzureMonitor();
builder.Services.AddOpenTelemetry().UseAzureMonitor();
```

Set `APPLICATIONINSIGHTS_CONNECTION_STRING` to the resource's connection string; both read it. When that setting is present, the sample app enables this registration and Blazor's built-in telemetry.

#### Why the add-on

Through the distro alone, two things go wrong, both verified against a live resource:

- **Browser exceptions cannot be grouped.** The Azure Monitor exporter builds an exception's type, stack, and problem ID from the thrown .NET object. A browser error arrives as text, so it is logged as `ClientBlazorErrorReportException`: every browser error shares one problem ID and has no frames in the Failures view.
- **Errors and events are sampled away.** The distro samples traces and, with `EnableTraceBasedLogsSampler` on (its default), drops every log inside a sampled-out trace. Server-rendered exceptions, browser reports, and events disappeared this way.

The add-on sends every error and event this package reports directly to Application Insights instead:

| Signal | Table | Notes |
| --- | --- | --- |
| Server-rendered exception | `exceptions` | Type, parsed stack, and a `type at method` problem ID; the full exception text, including inner exceptions, is in `blazor.exception.details`. |
| Browser or WebAssembly exception | `exceptions` | The original type, parsed frames (.NET, Mono, Chrome, Firefox, and Safari formats), and a `type at method` problem ID: the same form Application Insights computes for exceptions the JavaScript SDK sends, so the Failures view groups them by where they were thrown. |
| Stalled navigation, failed reconnection, circuit connection down and up | `customEvents` | Named as in the event table above. |
| `blazor.errors` | `customMetrics` | Through the distro, as usual. |
| `blazor.client_report` spans | `dependencies` | Through the distro, only for beacon uploads, as described under trace correlation. |

- **Not sampled.** Direct delivery does not pass through the host's trace sampling, so no error or event is dropped by it. Each one still carries `operation_Id`, the trace it happened in, for correlation.
- **Recorded once.** The add-on keeps this package's log categories (`NT.Blazor.ErrorBoundary.ClientReport` and `NT.Blazor.ErrorBoundary.AspNetCore.*`) out of the OpenTelemetry log pipeline, so nothing is duplicated in `exceptions`, `customEvents`, or `traces`. Logging providers outside OpenTelemetry, such as the console, still receive them. If the host also exports OpenTelemetry logs to another backend, that backend no longer receives these entries.
- **Context.** Browser reports fill `session_Id`, `user_AuthenticatedId` (when the upload is authenticated), the user agent, and `application_Version` (from `blazor.app.version`; server-rendered errors fill it too). Every other attribute, including `blazor.error.fingerprint`, is a custom property in `customDimensions`, whose values Application Insights truncates at 8,192 characters. Query dotted names with brackets, for example `customDimensions['blazor.report.kind']`.
- **Delivery.** Envelopes are queued on the reporting thread and posted to the connection string's `IngestionEndpoint` (`v2.1/track`) by a background sender. Each attempt times out after 10 seconds. Timeouts, network failures, `408`, `429`, and `5xx` are retried twice, after the `Retry-After` the response gives (up to a minute) or otherwise 2 and then 4 seconds; when a `206` reports some items rejected, only those rejected with one of those statuses are resent. Other failures are logged as warnings, with up to 1,024 characters of the response body. The queue holds 1,000 envelopes and about 32 MB of text, and stops taking events at 800 so a burst of circuit events cannot crowd out exceptions; when it drops anything, one warning is logged until it has room again. Shutdown drains the queue, and a warning reports how many items were left if the host's shutdown timeout cuts it short. An exporter that throws is logged as a warning and never fails the report request. Uploads run with OpenTelemetry instrumentation suppressed and without `HttpClient` request logging, so they add no dependency or trace rows of their own.
- **Configuration.** `NTBlazorAzureMonitorOptions.ConnectionString` defaults to `APPLICATIONINSIGHTS_CONNECTION_STRING`, as the distro does. `CloudRoleName` defaults to `OTEL_SERVICE_NAME`, then `unknown_service:<process name>`, matching the distro so these rows sit under the host's role. Set `Credential` to a `TokenCredential` when the resource requires Microsoft Entra authentication. With a credential, the connection string's `IngestionEndpoint` must use `https`, so the token is never sent in clear text; the exporter throws `InvalidOperationException` otherwise.

```csharp
using Azure.Identity;

builder.Services.AddNTBlazorErrorBoundaryAzureMonitor(options => {
    options.CloudRoleName = "storefront";
    options.Credential = new DefaultAzureCredential();
});
```

`DefaultAzureCredential` comes from the `Azure.Identity` package, which the add-on does not reference.

Without the add-on, set `UseAzureMonitor(options => options.EnableTraceBasedLogsSampler = false)` so the distro does not drop this package's logs, and group browser exceptions by `customDimensions['blazor.error.fingerprint']` rather than by problem ID.

#### Workbook and alerts

[`docs/azure-monitor`](https://github.com/NT-Technologies/NT.Blazor.ErrorBoundary/tree/main/docs/azure-monitor) contains a workbook and alert rules built on these fields:

- `blazor-errors.workbook.json` lists errors grouped by fingerprint with occurrence, user, and session counts and the app version each was first and last seen in, so a regression shows which release introduced it and a fix shows whether it stopped; errors first seen in the last day, with their version; error trends by source; and Blazor events. The version is `blazor.app.version`, falling back to Application Insights' `application_Version`. Import it from **Workbooks** > **New** > **Advanced Editor** > **Gallery Template** on the Application Insights resource.
- `blazor-error-alerts.bicep` creates two log search alerts: one fires when a single error fingerprint exceeds a threshold within 15 minutes, and the other fires on stalled navigations or failed reconnections. Deploy it with `az deployment group create --resource-group <group> --template-file docs/azure-monitor/blazor-error-alerts.bicep --parameters applicationInsightsId=<resource-id> actionGroupId=<action-group-id>`.

A test runs the real Azure Monitor exporter against a captured transport and asserts the envelopes it would send for server exceptions, browser exceptions, events, and context fields.

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
- URL fields (`Uri`, `OriginUri`, `TargetUri`, `JavaScriptSource`, and breadcrumb URIs) are stripped of query strings and fragments before `FilterReport` and logging.
- String fields are truncated to `MaxReportFieldLength`.

Use `FilterReport` to redact or drop reports. The client applies it before sending, and the server applies it before logging browser reports and server-rendered errors. Return a modified copy to redact, or `null` to drop:

```csharp
builder.Services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
    options.FilterReport = report => report.ExceptionType == "MyApp.ExpectedException"
        ? null
        : report;
});
```

`NTBlazorErrorReport` is a record, so `with` makes a redacted copy. This example masks email addresses in the free-text fields:

```csharp
var email = new Regex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled);
string? Mask(string? text) => text is null ? null : email.Replace(text, "[email]");

builder.Services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
    options.FilterReport = report => report with {
        ExceptionMessage = Mask(report.ExceptionMessage),
        ExceptionDetails = Mask(report.ExceptionDetails),
        ExceptionStackTrace = Mask(report.ExceptionStackTrace)
    };
});
```

Configure the same filter on the client and the server: the client copy keeps the data off the network, and the server copy covers browser reports, which do not pass through the client filter. A report's `ToString()` prints only its kind, exception type, and reference ID, so a report written to a log by accident does not carry these fields.

For server-rendered errors, the filter controls whether the error is logged and the fields logged with it, but the original `Exception` object is still logged unchanged; apply message redaction for it in the logging provider.

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

On the server, each `ILogger` step also passes the error to the registered `INTBlazorErrorBoundaryExporter`, if there is one.

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
- Current, origin, and target URIs with query strings and fragments removed. Exception messages and stack traces are sent as they are and can contain URLs with query strings.
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

Tests use xUnit v3 on Microsoft.Testing.Platform v2. The .NET SDK selects Microsoft.Testing.Platform through `global.json`; the test projects do not depend on VSTest, `Microsoft.NET.Test.Sdk`, or Coverlet.

`samples/` contains a small Blazor Web App (server and WebAssembly client) wired exactly as this README describes, with pages that fail under static SSR, Interactive Server, and Interactive WebAssembly. `Tests/NT.Blazor.ErrorBoundary.EndToEnd.Tests` runs it on Kestrel and drives it with Playwright's Chromium to verify the fallback, reference IDs in server logs, recovery, unhandled WebAssembly exceptions, JavaScript errors, stall detection, and the scoped styles. Install the browser once after building:

```shell
pwsh ./Tests/NT.Blazor.ErrorBoundary.EndToEnd.Tests/bin/Debug/net11.0/playwright.ps1 install chromium
```

Without the browser, the end-to-end tests are skipped. Set `NT_E2E_REQUIRED=true` to make them fail instead, as the `Build and Pack` workflow does.

Generate Cobertura coverage with Microsoft's testing-platform coverage extension:

```shell
dotnet test --project ./Tests/NT.Blazor.ErrorBoundary.Tests/NT.Blazor.ErrorBoundary.Tests.csproj --framework net10.0 --configuration Release --no-build --no-restore --results-directory ./artifacts/coverage/core --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
dotnet test --project ./Tests/NT.Blazor.ErrorBoundary.AspNetCore.Tests/NT.Blazor.ErrorBoundary.AspNetCore.Tests.csproj --framework net10.0 --configuration Release --no-build --no-restore --results-directory ./artifacts/coverage/aspnetcore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
dotnet test --project ./Tests/NT.Blazor.ErrorBoundary.AzureMonitor.Tests/NT.Blazor.ErrorBoundary.AzureMonitor.Tests.csproj --framework net10.0 --configuration Release --no-build --no-restore --results-directory ./artifacts/coverage/azuremonitor --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

The full suite runs on every target framework. Coverage runs once against .NET 10 for each test project so parallel target frameworks cannot overwrite a shared output file. The `Build and Pack` workflow uploads the reports as the `code-coverage` artifact.

Warnings are treated as errors across the repository. Packable projects generate XML documentation, so missing or invalid public API documentation also fails the build.

The library build also runs the TypeScript compilation and minification pipeline. Run `npm run check` separately for a no-emit TypeScript validation.


Create local packages with:

```shell
dotnet pack ./NT.Blazor.ErrorBoundary/NT.Blazor.ErrorBoundary.csproj --configuration Release --no-build --output ./artifacts/packages
dotnet pack ./NT.Blazor.ErrorBoundary.AspNetCore/NT.Blazor.ErrorBoundary.AspNetCore.csproj --configuration Release --no-build --output ./artifacts/packages
dotnet pack ./NT.Blazor.ErrorBoundary.AzureMonitor/NT.Blazor.ErrorBoundary.AzureMonitor.csproj --configuration Release --no-build --output ./artifacts/packages
```

## CI and releases

The `Build and Pack` workflow runs for pull requests and manual validation. It builds, tests, creates the NuGet packages and symbol packages, and uploads them as workflow artifacts.

Publication follows the same preview and stable semantic-release model as NTComponents.

1. On NuGet.org, add a [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) policy for `publish-prerelease.yml` and another for `release.yml`, then add the NuGet.org profile name (not the email) that owns them as the `NUGET_USER` GitHub Actions secret. Each policy's package scope must cover all three package IDs, for example with the glob `NT.Blazor.ErrorBoundary*`, and must allow publishing new packages, because `NT.Blazor.ErrorBoundary.AzureMonitor` has never been published. When the packages belong to an organization, `NUGET_USER` is the profile name of a member of that organization. Both workflows publish and unlist through `NuGet/login`, so no NuGet API key is stored.
2. Merge release-worthy Conventional Commits into `main`.
3. `Publish Prerelease` builds and tests the repository automatically.
4. Semantic-release updates the `preview` branch and creates a `vX.Y.Z-preview.N` tag and GitHub prerelease.
5. The same workflow packs all three packages with that preview version and publishes them and their symbols to NuGet.org.
6. When the preview is ready, run the manual `Release` workflow from GitHub Actions.
7. The release workflow builds and tests again, creates the stable `vX.Y.Z` tag and GitHub release, and publishes all three stable packages.
8. After stable publication succeeds, the workflow unlists prerelease versions whose base version is at or below the stable release.

Release calculation follows Conventional Commits:

- `fix:` creates a patch release.
- `feat:` creates a minor release.
- `BREAKING CHANGE:` creates a major release.
- Documentation, CI, and chore-only changes do not create a release by default.

Do not create release tags manually. If a push to `main` contains no release-worthy commits, semantic-release creates no preview tag and prerelease publication is skipped. If the manual stable release has no release-worthy commits, the `Release` run fails at "Resolve stable package version" with "No stable release tag found on this commit", and nothing is published.

## License

MIT. See [LICENSE](https://github.com/NT-Technologies/NT.Blazor.ErrorBoundary/blob/main/LICENSE).

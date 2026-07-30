# NT.Blazor.ErrorBoundary

`NT.Blazor.ErrorBoundary` provides a reporting Blazor error boundary and optional browser telemetry for .NET 9 and .NET 10 Blazor applications. The companion `NT.Blazor.ErrorBoundary.AspNetCore` project receives client reports and writes structured `ILogger` events that can flow to Application Insights, OpenTelemetry, or another configured logging provider.

## Packages

- `NT.Blazor.ErrorBoundary` contains `NTErrorBoundary`, the client-side reporter, browser telemetry models, and the static web asset.
- `NT.Blazor.ErrorBoundary.AspNetCore` contains server logging registration and the report endpoint.

Reference the core package from every project that renders the components. Reference the ASP.NET Core package from the web host.

## Configure reporting

Use the same report URI for the server endpoint, the client reporter, and the browser script.

### ASP.NET Core host

Register server-side boundary reporting before building the app:

```csharp
const string blazorErrorReportUri = "/api/client-errors";

builder.Services.AddBlazorErrorBoundaryServerLogging(blazorErrorReportUri);
```

Map the client-report endpoint after building the app:

```csharp
app.MapBlazorErrorBoundaryTelemetry()
    .RequireAuthorization();
```

`MapBlazorErrorBoundaryTelemetry` uses the URI supplied to `AddBlazorErrorBoundaryServerLogging`. Authorization and other endpoint policy remain the responsibility of the consuming application.

Reports are logged under the `NT.Blazor.ErrorBoundary.ClientReport` category. The logging scope includes the boundary name, report kind, exception information, renderer information, navigation identifiers and phases, sanitized URIs, browser session identifier, request trace identifiers, and the authenticated user identifier when available.

### Blazor WebAssembly client

Register the HTTP reporter in the client project. Configure its base address when the report URI is relative:

```csharp
builder.Services
    .AddNTBlazorErrorBoundaryHttpClient(
        "/api/client-errors",
        builder.HostEnvironment.Environment)
    .ConfigureHttpClient(client =>
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));
```

For an Interactive Auto application, register server logging in the host and the HTTP reporter in the client so the same component works with either interactive renderer.

## Load browser telemetry

Add the library's compiled JavaScript static web asset before `blazor.web.js` in `App.razor`:

```razor
<script src="@Assets["_content/NT.Blazor.ErrorBoundary/nt-blazor-error-boundary.js"]"
        data-report-uri="/api/client-errors"
        data-application-version="1.2.3"
        data-stall-timeout-ms="15000"></script>
<script src="@Assets["_framework/blazor.web.js"]" defer></script>
```

`data-report-uri` is required. If it is absent, the script does not initialize. `data-application-version` is optional. `data-stall-timeout-ms` is optional and defaults to 15 seconds.

The script adds browser context to boundary reports, captures JavaScript errors and unhandled promise rejections, records recent navigation breadcrumbs in `sessionStorage`, and queues failed submissions for another delivery attempt. Telemetry failures are contained and do not interrupt application navigation.

## Protect components

Make the component namespace available from `_Imports.razor`:

```razor
@using NT.Blazor.ErrorBoundary.Components
```

Wrap a page or component and give important boundaries a stable diagnostic name:

```razor
<NTErrorBoundary Name="Claims.Recovery">
    <RecoveryContent />
</NTErrorBoundary>
```

The default fallback displays generic recovery guidance. Exception details render only in development by default. `RenderExceptionDetails` can explicitly select `Always`, `Never`, or `DevelopmentOnly`.

`NTErrorBoundary` catches exceptions thrown by descendant Blazor components. Global JavaScript errors and stalled navigations are captured by the browser script instead.

## Detect stalled interactive navigation

Stall detection is opt-in for navigation that must reach an interactive Blazor render. It has two parts.

Place `NTBlazorInteractiveReady` in the destination's interactive component tree:

```razor
@page "/claims/recovery/{ClaimId:int}"
@rendermode InteractiveAuto

<NTBlazorInteractiveReady />
<RecoveryContent />
```

Then mark links whose telemetry should wait for that signal:

```razor
<a href="@($"/claims/recovery/{claimId}")"
   data-nt-require-interactive-ready="true">
    Recovery Management
</a>
```

`data-nt-require-interactive-ready="true"` is a telemetry attribute only. It does not change routing, cancel navigation, delay rendering, or enable interactivity.

- Without the attribute, a document load or Blazor enhanced-load signal completes the navigation measurement.
- With the attribute, telemetry waits until `NTBlazorInteractiveReady` completes its first interactive render and calls the browser readiness marker.
- If the expected marker does not arrive before `data-stall-timeout-ms`, the browser submits a `NavigationStalled` report with the last recorded navigation phase.

`NTBlazorInteractiveReady` deliberately has no render mode of its own and renders no HTML. It inherits the renderer used by its containing page or component. `OnAfterRenderAsync` does not execute during prerendering, and the marker reports readiness only when `RendererInfo.IsInteractive` is true.

Only use the link attribute when the destination contains the marker beneath an `InteractiveServer`, `InteractiveWebAssembly`, or `InteractiveAuto` boundary. A marker in a static SSR tree cannot report interactive readiness. The marker signals once per component instance, so parameter-only navigation that reuses the same instance requires a destination-specific readiness strategy.

## Browser telemetry fields

Client reports can include:

- Application version and browser session identifier.
- Boundary name, report kind, exception type, message, details, and stack trace.
- Current, origin, and target URIs with query strings and fragments removed.
- Navigation identifier, phase, interception state, and recent breadcrumbs.
- Renderer name and interactive state.
- JavaScript source, line, and column for browser errors.
- Browser online state.

String fields are truncated before logging according to `NTBlazorErrorBoundaryHttpClientOptions.MaxReportFieldLength`.

## TypeScript static asset

The browser source of truth is:

```text
NT.Blazor.ErrorBoundary/wwwroot/nt-blazor-error-boundary.ts
```

Do not edit the generated `.js` file directly. From the repository root:

```powershell
npm ci
npm run check
npm run build
```

Building `NT.Blazor.ErrorBoundary` automatically restores the locked npm dependencies when necessary, compiles the TypeScript, and minifies the result. The generated `nt-blazor-error-boundary.js` is published as the static web asset; the TypeScript source is neither packed nor published.

## Build and test

```powershell
dotnet build ./NT.Blazor.ErrorBoundary.slnx
dotnet test ./NT.Blazor.ErrorBoundary.slnx
```

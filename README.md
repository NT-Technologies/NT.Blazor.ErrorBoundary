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

### 3. Add the boundary to the component tree

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
- `RequestId`
- `RequestMethod`
- `RequestPath`
- `UserId`, when a `ClaimTypes.NameIdentifier` or `sub` claim is present

The HTTP reporter treats network and non-success response failures as reporting failures, logs a warning, and does not rethrow them into the component tree. Cancellation requested by the caller is preserved.

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

## Development

The repository uses the SDK selected by `global.json` and builds both .NET 9 and .NET 10 targets.

```shell
dotnet restore ./NT.Blazor.ErrorBoundary.slnx
dotnet build ./NT.Blazor.ErrorBoundary.slnx --configuration Release --no-restore
dotnet test ./NT.Blazor.ErrorBoundary.slnx --configuration Release --no-build --no-restore
```

Warnings are treated as errors across the repository. Packable projects generate XML documentation, so missing or invalid public API documentation also fails the build.

Create local packages with:

```shell
dotnet pack ./NT.Blazor.ErrorBoundary/NT.Blazor.ErrorBoundary.csproj --configuration Release --no-build --output ./artifacts/packages
dotnet pack ./NT.Blazor.ErrorBoundary.AspNetCore/NT.Blazor.ErrorBoundary.AspNetCore.csproj --configuration Release --no-build --output ./artifacts/packages
```

## CI and releases

The `Build and Pack` workflow runs for pull requests and pushes to `main`. It builds, tests, creates both NuGet packages and symbol packages, and uploads them as workflow artifacts.

Stable publication follows the same semantic-release model as NTComponents:

1. Add the NuGet.org API key as the `NUGET_API_KEY` GitHub Actions secret.
2. Merge release-worthy Conventional Commits into `main`.
3. Run the manual `Release` workflow from GitHub Actions.
4. The workflow builds and tests the repository.
5. Semantic-release creates the `vX.Y.Z` tag and GitHub release.
6. The workflow packs both packages with that version and publishes them and their symbols to NuGet.org.

Release calculation follows Conventional Commits:

- `fix:` creates a patch release.
- `feat:` creates a minor release.
- `BREAKING CHANGE:` creates a major release.
- Documentation, CI, and chore-only changes do not create a release by default.

Do not create release tags manually. If there are no release-worthy commits since the previous tag, semantic-release does not create a new version and the release workflow stops without publishing.

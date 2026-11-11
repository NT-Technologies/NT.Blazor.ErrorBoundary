using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NT.Blazor.ErrorBoundary.AspNetCore;
using NT.Blazor.ErrorBoundary.Models;
using NT.Blazor.ErrorBoundary.Services;
using System.Diagnostics;
using System.Security.Claims;

namespace NT.Blazor.ErrorBoundary.AspNetCore.Tests.AspNetCore;

public sealed class NTBlazorErrorBoundaryAspNetCoreExtensionsTests {
    [Fact]
    public void AddNTBlazorErrorBoundaryServerLogging_ConfiguresReportUri() {
        var services = new ServiceCollection();

        services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NTBlazorErrorBoundaryHttpClientOptions>>();

        Assert.Equal("/api/errors", options.Value.ReportUri);
    }

    [Fact]
    public void MapNTBlazorErrorBoundaryTelemetry_UsesConfiguredReportUri() {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Configure<NTBlazorErrorBoundaryHttpClientOptions>(options => {
            options.ReportUri = "/api/errors";
        });
        using var app = builder.Build();

        app.MapNTBlazorErrorBoundaryTelemetry();

        var routeEndpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>());
        Assert.Equal("/api/errors", routeEndpoint.RoutePattern.RawText);
    }

    [Fact]
    public void LogClientReport_LogsStructuredClientErrorFields() {
        var loggerProvider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger("test");
        var httpContext = new DefaultHttpContext {
            TraceIdentifier = "request-123",
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "user-123")
            ], "Test"))
        };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/v1/Telemetry/BlazorError";
        httpContext.Request.Headers.UserAgent = "Telemetry Test Browser";
        var report = new NTBlazorErrorReport {
            ApplicationVersion = "4.13.0",
            BoundaryName = "Boundary",
            Breadcrumbs = [
                new NTBlazorBreadcrumb {
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    Phase = "LocationChanging",
                    TargetUri = "https://example.test/claims/recovery/64469",
                    Uri = "https://example.test/claims/manage/64469"
                }
            ],
            ClientSessionId = "session-123",
            ExceptionDetails = "complete exception",
            ExceptionMessage = "client failure",
            ExceptionStackTrace = "stack",
            ExceptionType = typeof(InvalidOperationException).FullName,
            IsInteractive = true,
            IsNavigationIntercepted = true,
            IsOnline = true,
            NavigationId = "navigation-123",
            NavigationPhase = "LocationChanging",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            OriginUri = "https://example.test/claims/manage/64469",
            RenderMode = "WebAssembly",
            TargetUri = "https://example.test/claims/recovery/64469",
            Uri = "https://example.test/claims"
        };

        using var activity = new Activity("client-report-test").Start();
        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logger, report, httpContext);

        var entry = Assert.Single(loggerProvider.Entries);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.NotNull(entry.Exception);
        Assert.Contains("Blazor client report Exception", entry.Message, StringComparison.Ordinal);
        Assert.Equal("Boundary", entry.Values["blazor.boundary.name"]);
        Assert.Equal("4.13.0", entry.Values["blazor.app.version"]);
        Assert.Contains("LocationChanging", Assert.IsType<string>(entry.Values["blazor.navigation.breadcrumbs"]), StringComparison.Ordinal);
        Assert.Equal("session-123", entry.Values["session.id"]);
        Assert.Equal("session-123", entry.Values["microsoft.session.id"]);
        Assert.Equal("complete exception", entry.Values["blazor.exception.details"]);
        Assert.Equal("client failure", entry.Values["blazor.exception.message"]);
        Assert.Equal("stack", entry.Values["blazor.exception.stacktrace"]);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.Values["blazor.exception.type"]);
        Assert.Equal(true, entry.Values["blazor.interactive"]);
        Assert.Equal(true, entry.Values["blazor.navigation.intercepted"]);
        Assert.Equal(true, entry.Values["blazor.client.online"]);
        Assert.Equal("navigation-123", entry.Values["blazor.navigation.id"]);
        Assert.Equal("LocationChanging", entry.Values["blazor.navigation.phase"]);
        Assert.Equal("https://example.test/claims/manage/64469", entry.Values["blazor.navigation.origin_url"]);
        Assert.Equal("WebAssembly", entry.Values["blazor.render_mode"]);
        Assert.Equal(NTBlazorReportKinds.Exception, entry.Values["blazor.report.kind"]);
        Assert.Equal("https://example.test/claims/recovery/64469", entry.Values["blazor.navigation.target_url"]);
        Assert.Equal("https://example.test/claims", entry.Values["url.full"]);
        Assert.Equal("user-123", entry.Values["user.id"]);
        Assert.Equal("user-123", entry.Values["enduser.id"]);
        Assert.Equal("Telemetry Test Browser", entry.Values["user_agent.original"]);
        Assert.Matches("^[0-9a-f]{16}$", Assert.IsType<string>(entry.Values["blazor.error.fingerprint"]));
        Assert.Equal(activity.TraceId.ToString(), entry.TraceId);
    }

    [Fact]
    public void HandleClientReport_WhenReportFieldsExceedMaximum_TruncatesStructuredFields() {
        var loggerProvider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger("test");
        var httpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/errors";
        var report = new NTBlazorErrorReport {
            BoundaryName = new string('b', 200),
            ExceptionMessage = new string('m', 200),
            ExceptionStackTrace = new string('s', 200),
            ExceptionType = typeof(InvalidOperationException).FullName,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Uri = $"https://example.test/{new string('u', 200)}"
        };

        NTBlazorErrorBoundaryAspNetCoreExtensions.HandleClientReport(logger, report, httpContext, new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors", MaxReportFieldLength = 64 });

        var entry = Assert.Single(loggerProvider.Entries);
        var message = Assert.IsType<string>(entry.Values["blazor.exception.message"]);
        var stackTrace = Assert.IsType<string>(entry.Values["blazor.exception.stacktrace"]);
        var uri = Assert.IsType<string>(entry.Values["url.full"]);
        Assert.True(message.Length <= 64);
        Assert.True(stackTrace.Length <= 64);
        Assert.True(uri.Length <= 64);
        Assert.Contains("report field truncated", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void ServerLogging_SetsDevelopmentFlagFromHostEnvironment(string environmentName, bool expected) {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));
        services.AddNTBlazorErrorBoundaryServerLogging("/api/errors");

        using var provider = services.BuildServiceProvider();

        Assert.Equal(expected, provider.GetRequiredService<IOptions<NTErrorBoundaryOptions>>().Value.IsDevelopmentEnvironment);
    }

    [Fact]
    public void PageTraceDiffersFromCurrentTrace_LogsUnderPageTraceAndRestoresCurrentActivity() {
        var logs = new TestLoggerProvider();
        var pageTraceId = ActivityTraceId.CreateRandom();
        using var source = new ActivitySource("ErrorBoundary.Tests.TraceRestore");
        using var listener = new ActivityListener { ShouldListenTo = candidate => candidate == source, Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData };
        ActivitySource.AddActivityListener(listener);
        using var current = source.StartActivity("upload");
        Assert.NotNull(current);
        Assert.NotEqual(pageTraceId, current.TraceId);

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logs.CreateLogger("test"), new NTBlazorErrorReport { TraceParent = $"00-{pageTraceId}-1234567890abcdef-01" }, new DefaultHttpContext());

        Assert.Equal(pageTraceId.ToString(), Assert.Single(logs.Entries).TraceId);
        Assert.Same(current, Activity.Current);
    }

    [Fact]
    public void PageTraceEqualsCurrentTrace_LogsUnderCurrentActivity() {
        var logs = new TestLoggerProvider();
        using var current = new Activity("upload").Start();

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logs.CreateLogger("test"), new NTBlazorErrorReport { TraceParent = $"00-{current.TraceId}-1234567890abcdef-01" }, new DefaultHttpContext());

        Assert.Equal(current.TraceId.ToString(), Assert.Single(logs.Entries).TraceId);
        Assert.Same(current, Activity.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00-00000000000000000000000000000000-0000000000000000-00")]
    public void UnusableTraceParent_LogsUnderCurrentTrace(string? traceParent) {
        var logs = new TestLoggerProvider();
        using var current = new Activity("upload").Start();

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logs.CreateLogger("test"), new NTBlazorErrorReport { TraceParent = traceParent }, new DefaultHttpContext());

        Assert.Equal(current.TraceId.ToString(), Assert.Single(logs.Entries).TraceId);
        Assert.Same(current, Activity.Current);
    }

    [Fact]
    public void NoClaims_LogsNoUserId() {
        var values = LogUserValues(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.DoesNotContain("user.id", values.Keys);
        Assert.DoesNotContain("enduser.id", values.Keys);
    }

    [Fact]
    public void WhitespaceNameIdentifier_LogsNoUserId() {
        var values = LogUserValues(Principal(new Claim(ClaimTypes.NameIdentifier, "   ")));

        Assert.DoesNotContain("user.id", values.Keys);
        Assert.DoesNotContain("enduser.id", values.Keys);
    }

    [Fact]
    public void NameIdentifierAndSub_LogsNameIdentifierUnderBothNames() {
        var values = LogUserValues(Principal(new Claim("sub", "sub-1"), new Claim(ClaimTypes.NameIdentifier, "name-1")));

        Assert.Equal("name-1", values["user.id"]);
        Assert.Equal("name-1", values["enduser.id"]);
    }

    [Fact]
    public void OnlySub_LogsSubUnderBothNames() {
        var values = LogUserValues(Principal(new Claim("sub", "sub-1")));

        Assert.Equal("sub-1", values["user.id"]);
        Assert.Equal("sub-1", values["enduser.id"]);
    }

    // Pins the current behavior: a whitespace NameIdentifier is non-null, so it shadows sub and no user ID is logged.
    [Fact]
    public void WhitespaceNameIdentifierWithSub_LogsSubUnderBothNames() {
        var values = LogUserValues(Principal(new Claim(ClaimTypes.NameIdentifier, " "), new Claim("sub", "sub-1")));

        Assert.Equal("sub-1", values["user.id"]);
        Assert.Equal("sub-1", values["enduser.id"]);
    }

    [Theory]
    [InlineData(5 * 60, true)]
    [InlineData(5 * 60 + 1, false)]
    [InlineData(-7 * 24 * 3600, true)]
    [InlineData(-7 * 24 * 3600 - 1, false)]
    public void OccurredAtAtWindowEdge_IsAcceptedOnlyWithinFiveMinutesAheadAndSevenDaysBack(int offsetSeconds, bool accepted) {
        var logs = new TestLoggerProvider();
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

        var result = Handle(logs, new NTBlazorErrorReport { OccurredAtUtc = now.AddSeconds(offsetSeconds) }, new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" }, now);

        Assert.Equal(accepted ? StatusCodes.Status204NoContent : StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(accepted ? 1 : 0, logs.Entries.Count);
    }

    [Fact]
    public void DefaultOccurredAt_IsRejected() {
        var logs = new TestLoggerProvider();

        var result = Handle(logs, new NTBlazorErrorReport(), new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors" }, new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public void Filter_ReceivesTruncatedReportExactlyOnce() {
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var received = new List<NTBlazorErrorReport>();
        var options = new NTBlazorErrorBoundaryHttpClientOptions { ReportUri = "/api/errors", MaxReportFieldLength = 64, FilterReport = report => { received.Add(report); return null; } };

        Handle(new TestLoggerProvider(), new NTBlazorErrorReport { ExceptionMessage = new string('m', 500), OccurredAtUtc = now }, options, now);

        var report = Assert.Single(received);
        Assert.Equal(64, report.ExceptionMessage!.Length);
        Assert.Contains("report field truncated", report.ExceptionMessage, StringComparison.Ordinal);
    }

    private static IResult Handle(TestLoggerProvider logs, NTBlazorErrorReport report, NTBlazorErrorBoundaryHttpClientOptions options, DateTimeOffset now) {
        var httpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<TimeProvider>(new FixedTimeProvider(now)).BuildServiceProvider() };
        return NTBlazorErrorBoundaryAspNetCoreExtensions.HandleClientReport(logs.CreateLogger("test"), report, httpContext, options);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "Test"));

    private static IReadOnlyDictionary<string, object?> LogUserValues(ClaimsPrincipal user) {
        var logs = new TestLoggerProvider();

        NTBlazorErrorBoundaryAspNetCoreExtensions.LogClientReport(logs.CreateLogger("test"), new NTBlazorErrorReport(), new DefaultHttpContext { User = user });

        return Assert.Single(logs.Entries).Values;
    }

    private sealed class FakeHostEnvironment(string _environmentName) : IHostEnvironment {
        public string EnvironmentName { get; set; } = _environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

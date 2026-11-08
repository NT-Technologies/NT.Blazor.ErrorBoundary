# Test Guidelines

- Use xUnit for unit tests and bUnit for component tests.
- Maintain one test project per production project, using the production project name plus `.Tests`. The exception is `NT.Blazor.ErrorBoundary.EndToEnd.Tests`, which tests both packages through the sample app in a real browser.
- In end-to-end tests, rely on Playwright auto-waiting and bounded polling of captured logs, never fixed delays.
- Name tests using `[State]_[ExpectedBehavior]`.
- Avoid sleeps and timing-based waits; use bUnit assertions or direct fake services.
- Verify that fallback UI stays generic by default and only exposes exception details when explicitly enabled or in development-only mode.
- Prefer small fakes over mocking frameworks for reporter, HTTP, and logger behavior.
- Test the browser script with `node:test` in `ClientAssets/` against the built `wwwroot/nt-blazor-error-boundary.js`, using small browser fakes and manually fired timers instead of a DOM library or real delays.

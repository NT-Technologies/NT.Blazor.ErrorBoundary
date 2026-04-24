# Test Guidelines

- Use xUnit for unit tests and bUnit for component tests.
- Maintain one test project per production project, using the production project name plus `.Tests`.
- Name tests using `[State]_[ExpectedBehavior]`.
- Avoid sleeps and timing-based waits; use bUnit assertions or direct fake services.
- Verify that fallback UI stays generic by default and only exposes exception details when explicitly enabled or in development-only mode.
- Prefer small fakes over mocking frameworks for reporter, HTTP, and logger behavior.

# AGENTS.md - Developer Guidelines

This file provides agentic coding agents with essential information about building, testing, and coding standards for this functional tests repository.

## Build & Test Commands

### .NET / MSBuild Setup
- **.NET SDK Version**: net11.0
- **Test Framework**: TUnit + TUnit.Aspire on top of the Aspire AppHost (PostgreSQL, Kafka); Testcontainers for WireMock and the dashboard
- **Aspire**: 17.0 — DCP and the dashboard come from the Aspire CLI bundle; the AppHost build fetches it via `dnx aspire.cli` when `aspire` is not on PATH, so the CLI is optional
- **Test Runner**: Microsoft.Testing.Platform

### Running Tests
```bash
# Run all tests
dotnet test Tests/Tests.csproj

# Run a single test class
dotnet test Tests/Tests.csproj --filter "Tests.OrderTests"

# Run a single test method
dotnet test Tests/Tests.csproj --filter "Tests.OrderTests.Should_RejectOrder_When_InventoryIsInsufficient"

# Run with verbose output
dotnet test Tests/Tests.csproj -v detailed

# Choose where the API runs: Aspire (default) | InProcess (debugging) | Container (CI, published image)
TESTS_API_HOST=Container dotnet test Tests/Tests.csproj

# Run the app itself (SomeService stand-in as the third party; set Parameters:some-service-url for a real one)
dotnet run --project AppHost   # same as: aspire run --apphost AppHost/AppHost.csproj
```

### Building
```bash
# Build entire solution
dotnet build FunctionalTests.sln

# Build specific project
dotnet build Api/Api.csproj
dotnet build Tests/Tests.csproj
```

## Code Style Guidelines

### Imports & Namespacing
- **Implicit Usings**: Enabled (`ImplicitUsings=enable` in both .csproj files)
- **Nullable Reference Types**: Enabled (`Nullable=enable`)
- Group imports logically: System, Microsoft, OpenTelemetry, then application namespaces
- Use file-scoped namespaces: `namespace Tests;` (single-line, no braces)
- Order: System.* → Framework → Libraries → Application

### Formatting & Conventions
- **Indentation**: 2 spaces (see OrderTests.cs, GlobalSetup.cs)
- **Braces**: K&R style (opening brace on same line)
- **Spacing**: Single blank line between logical sections
- **Line Length**: No strict limit, but prefer readability (~120 chars is reasonable)
- **Naming**: PascalCase for classes, methods, properties; camelCase for local variables

### Types & Declarations
- Use **records** for DTOs/request-response models (e.g., `CreateOrderRequest`, `InventoryResponse`)
- Use **required properties** with init-only setters for fixtures: `public required ApiFixture App {get; init;}`
- Prefer **nullable reference types** over null coalescing for safety
- Use **string.IsNullOrEmpty()** for string validation (see Program.cs:23)
- Declare private fields with underscore prefix: `private IAsyncDisposable? _app`

### Error Handling
- **Exceptions**: Catch and re-throw only when adding context
- **Async/Await**: Use async patterns throughout (IAsyncInitializer, IAsyncDisposable)
- **Null Checks**: Leverage nullable reference types; use pattern matching where possible
- **Validation**: Return appropriate HTTP status codes (422 for business logic, 404 for not found)

### Naming Conventions
- **Test Methods**: `Should_<Expected>_When_<Condition>()` (e.g., `Should_RejectOrder_When_InventoryIsInsufficient`)
- **Fixtures**: Suffix with `Fixture` (e.g., `ApiFixture`, `WireMockFixture`)
- **Stubs**: Suffix with `Stub` (e.g., `ExampleServiceStub`)
- **Clients**: Suffix with `Client` (e.g., `ApiClient`)
- **Configuration**: Singleton/static classes for configuration (e.g., `TestsConfiguration`)

### Dependency Injection & Fixtures
- Use **ClassDataSource** attribute for TUnit fixture injection:
  ```csharp
  [ClassDataSource<ApiFixture>(Shared = SharedType.PerTestSession)]
  public required ApiFixture App { get; init; }
  ```
- Implement **IAsyncInitializer** and **IAsyncDisposable** for infrastructure lifecycle management
- Fixtures orchestrate multiple containers (PostgreSQL, Kafka, WireMock, Aspire Dashboard)
- Use `SuppressInstrumentationScope` to avoid double-tracing during setup

### Testing Patterns
- **Arrange-Act-Assert**: Structure tests clearly with these sections
- **Per-Test Isolation**: TUnit opens a `test case` span per test, each in its own trace
- **Shared Infrastructure**: Leverage PerTestSession sharing to reuse containers across tests
- **Stubs & Mocks**: Use WireMock for external service stubbing; trace-scoped via `traceparent` header
- Use **TUnit assertions**: `await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)`

### OpenTelemetry & Observability
- Test-side spans (`TUnit`, `TUnit.Lifecycle`, HttpClient) are exported by `TUnit.OpenTelemetry` (via TUnit.Aspire) as "functional-tests"; `GlobalSetup.cs` only points it at the dashboard (`DOTNET_DASHBOARD_OTLP_ENDPOINT_URL`, fixed ports) - don't build a TracerProvider or create per-test activities by hand
- Add instrumentation for: AspNetCore, Http, Kafka, Npgsql
- The API takes OTLP endpoint/protocol/service name from `OTEL_*` env (`UseOtlpExporter()`); never hard-code them - Aspire and TUnit.Aspire set them per host
- TUnit sets test span status and records exceptions itself; hooks run inside their own spans, so `Activity.Current` in a hook is the hook span
- In-process API stays on `WebApplicationFactory<T>` + `TelemetryHandler` for trace propagation (TUnit 1.83's `TestWebApplicationFactory<T>` hung when parallel tests made their first call together)

### Module Organization
- **AppHost/**: Aspire AppHost - the single source of truth for the system
  - PostgreSQL + `ordersdb` (schema in `Database/orders.sql`), Kafka (+ `orders-topic` created on ResourceReady)
  - Third-party APIs as `AddExternalService(name, <url parameter>)` when the URL is configured, otherwise a local stand-in project (`SomeService/`) - tests always configure the URL
  - Binds resources to the API's config keys (`ConnectionStrings__Database`, `Kafka__ConnectionString`, `SomeService__BaseUrl`)
- **Api/**: Minimal .NET API with POST `/api/orders` and `/health`
- **Tests/**: Functional test suite, an overlay on the AppHost
  - `ApiFixture.cs`: `AspireFixture<AppHost>` - only what differs in a test: a stub for every external service, dashboard OTLP, API host
  - `Fixtures/ApiHosts.cs`: re-hosts the AppHost's `api` in-process or as a container by replaying its environment callbacks
  - `Fixtures/`: WireMock and dashboard containers, `LocalAppFixture` (WebApplicationFactory), `TestsConfiguration`
  - `Clients/`: Typed HTTP clients for the API under test
  - `Stubs/`: Domain-specific WireMock stubs, one `ExternalServiceStub` per third party
  - `GlobalSetup.cs`: TUnit hooks - telemetry endpoint before discovery, test span naming/trimming

## Key Testing Architecture

### Infrastructure Management
- **Aspire** (via the AppHost) runs PostgreSQL, Kafka and the API; change infrastructure in `AppHost.cs`, not in the tests
- **Testcontainers** run what the tests own: WireMock (fakes for third parties) and the reused Aspire Dashboard
- **API hosts** (`TESTS_API_HOST`): `Aspire` launched like `aspire run`; `InProcess` via WebApplicationFactory; `Container` as the published image on Aspire's network
- A new `AddExternalService` in the AppHost needs an `ExternalServiceStub` subclass (naming its URL parameter) registered in `ApiFixture.Stubs`, otherwise the test session fails fast

### Parallel Test Isolation
- Each test runs in its own trace (TUnit `test case` span)
- WireMock stubs are scoped by `traceparent` header to prevent cross-test interference
- Fixtures use `Shared = SharedType.PerTestSession` to reuse expensive infrastructure

### Configuration
- `TESTS_API_HOST` env var selects the API host (see `TestsConfiguration.cs`)
- The API's configuration always comes from the AppHost model; tests only pass parameters (`--Parameters:<external>-url` as AppHost args) and `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`

---

**Generated**: For automated agents operating in functional-tests repository

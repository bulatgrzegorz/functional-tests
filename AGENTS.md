# AGENTS.md - Developer Guidelines

This file provides agentic coding agents with essential information about building, testing, and coding standards for this functional tests repository.

## Build & Test Commands

### .NET / MSBuild Setup
- **.NET SDK Version**: net10.0 (see `global.json`)
- **Test Framework**: TUnit with TestContainers for real infrastructure (PostgreSQL, Kafka, WireMock)
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

# Run with OpenTelemetry tracing (when UseAspire=true, Aspire Dashboard accessible at logged port)
dotnet test Tests/Tests.csproj
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
- **Fixtures**: Suffix with `Fixture` (e.g., `ApiFixture`, `PostgresqlFixture`)
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
- **Per-Test Isolation**: Each test gets isolated traces via unique Activity per test
- **Shared Infrastructure**: Leverage PerTestSession sharing to reuse containers across tests
- **Stubs & Mocks**: Use WireMock for external service stubbing; trace-scoped via `traceparent` header
- Use **TUnit assertions**: `await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)`

### OpenTelemetry & Observability
- Configure **ActivitySource** per logical component (TestActivitySource, service name "functional-tests")
- Add instrumentation for: AspNetCore, Http, Kafka, Npgsql
- Export traces to OTLP endpoint (grpc protocol) when configured
- Always set **Activity status** in after-hooks based on test result
- Use **AddAsyncLocalValues()** to propagate context across async boundaries

### Module Organization
- **Api/**: Minimal .NET API with POST `/api/orders` endpoint
  - Orchestrates PostgreSQL, Kafka, external service calls
  - Configure via environment variables (ConnectionStrings__*, Kafka__*, SomeService__*)
- **Tests/**: Functional test suite with real infrastructure
  - `ApiFixture.cs`: Main orchestrator for all fixtures and HTTP client factory
  - `Fixtures/`: Individual container fixtures (Postgres, Kafka, WireMock, Aspire, Docker Network)
  - `Clients/`: Typed HTTP clients for the API under test
  - `Stubs/`: Domain-specific WireMock stub builders
  - `GlobalSetup.cs`: TUnit hooks for OpenTelemetry lifecycle management

## Key Testing Architecture

### Infrastructure Management
- **Testcontainers** manage real PostgreSQL, Kafka, WireMock, and Aspire Dashboard containers
- **Docker Network** fixture ensures all containers can communicate
- **LocalMode** vs **ContainerMode**: LocalMode uses WebApplicationFactory for faster iteration; ContainerMode runs API in Docker

### Parallel Test Isolation
- Each test creates isolated OpenTelemetry Activity with unique trace context
- WireMock stubs are scoped by `traceparent` header to prevent cross-test interference
- Fixtures use `Shared = SharedType.PerTestSession` to reuse expensive infrastructure

### Configuration
- Set feature flags in `TestsConfiguration.cs`: `UseAspire`, `LocalMode`
- Pass variables to app via environment: `Dictionary<string, string>` → app initialization
- OTLP endpoint, database connection, Kafka, external service URL all configurable

---

**Generated**: For automated agents operating in functional-tests repository

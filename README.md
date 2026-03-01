# Functional Tests Example

Companion repository for the blog post: [Complete Guide to Functional Tests](https://bulatgrzegorz.github.io/complete-guide-to-functional-tests/).

A working example of functional tests for a .NET API, demonstrating how to combine **TUnit**, **Testcontainers**, **OpenTelemetry**, and the **Aspire Dashboard** into a productive testing workflow.

## What's inside

- **Api** — a minimal .NET API with a POST `/api/orders` endpoint that talks to PostgreSQL, Kafka, and an external service (WireMock)
- **Tests** — functional tests using TUnit with real infrastructure spun up via Testcontainers

## Key concepts

- **Real infrastructure in tests** — PostgreSQL, Kafka, and WireMock running as Docker containers via Testcontainers
- **Parallel test isolation** — each test gets its own OpenTelemetry trace; WireMock stubs are scoped by `traceparent` header so parallel tests don't interfere with each other
- **Observability** — the Aspire Dashboard runs as a Testcontainer, collecting traces and logs from both the test process and the API under test
- **Two execution modes** — `LocalMode` runs the API in-process via `WebApplicationFactory`; container mode publishes and runs it as a Docker image on a shared network

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/)

## Running

```console
dotnet test Tests/Tests.csproj
```

When `TestsConfiguration.UseAspire` is `true`, the Aspire Dashboard will be available at `http://localhost:<mapped-port>` — check the test output for the exact port. The dashboard container is reused across runs (`WithReuse(true)`), so your traces accumulate.

## Project structure

```
├── Api/
│   ├── Program.cs              # Minimal API with /api/orders endpoint
│   └── Api.csproj
├── Tests/
│   ├── GlobalSetup.cs          # OpenTelemetry setup, per-test Activity lifecycle
│   ├── ApiFixture.cs           # Orchestrates all infrastructure and app startup
│   ├── OrderTests.cs           # Example test
│   ├── Clients/
│   │   └── ApiClient.cs        # Typed HTTP client for the API
│   ├── Fixtures/
│   │   ├── AspireDashboardFixture.cs   # Aspire Dashboard container
│   │   ├── PostgresqlFixture.cs        # PostgreSQL container
│   │   ├── KafkaFixture.cs             # Kafka container
│   │   ├── WireMockFixture.cs          # WireMock container + trace-scoped stubs
│   │   ├── DockerNetworkFixture.cs     # Shared Docker network
│   │   ├── ContainerAppFixture.cs      # Runs API as Docker container
│   │   ├── LocalAppFixture.cs          # Runs API in-process via WebApplicationFactory
│   │   └── TestsConfiguration.cs       # Feature flags (UseAspire, LocalMode)
│   └── Stubs/
│       └── ExampleServiceStub.cs       # Domain-specific WireMock stub
├── FunctionalTests.sln
└── global.json
```

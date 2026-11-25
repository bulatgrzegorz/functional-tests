# Functional Tests

**The whole system, started for real. Every test, one trace.**

A small .NET order API with PostgreSQL, Kafka and a third-party HTTP service, plus functional tests that start the *same* system you run locally. They fake only the third party and follow each test from the first request to the Kafka message as one distributed trace.

Companion repository for the blog post **[Complete Guide to Functional Tests](https://bulatgrzegorz.github.io/complete-guide-to-functional-tests/)**.

```mermaid
flowchart LR
  subgraph session["dotnet test (TUnit)"]
    a["test A"]
    b["test B"]
  end
  subgraph system["AppHost.cs (Aspire)"]
    api["Api"]
    pg[("PostgreSQL")]
    kafka[["Kafka<br/>orders-topic"]]
  end
  subgraph owned["owned by the tests (Testcontainers)"]
    wm["WireMock<br/>fake inventory"]
    dash["Aspire Dashboard<br/>localhost:18888"]
  end
  a & b -->|"POST /api/orders"| api
  api -->|"GET /inventory/{sku}"| wm
  api -->|"INSERT"| pg
  api -->|"OrderCreated"| kafka
  kafka -.->|"only my messages"| a
  a & b & api -.->|"traces"| dash
```

---

## Try it

You need the [.NET 11 SDK](https://dotnet.microsoft.com/download) and Docker or Podman. The [Aspire CLI](https://get.aspire.dev) is optional: without it, the first build downloads the parts Aspire needs.

```console
dotnet test Tests/Tests.csproj
```

Then open **http://localhost:18888** and click a test. That is the whole tour.

> [!TIP]
> The first run pulls images and builds the API, so give it a few minutes. Later runs reuse what is already there.

Want the app itself, without tests?

```console
dotnet run --project AppHost   # or: aspire run --apphost AppHost/AppHost.csproj
# then send Api/CreateOrder.http → POST http://localhost:5160/api/orders
```

Here the inventory service is a small stand-in (`SomeService/`). It reports 100 for `SKU-001`, 0 for `SKU-002` and 5 for anything else.

---

## Three tools, one job each

| | Job | In this repo |
| --- | --- | --- |
| **[Aspire](https://aspire.dev)** | Describes the system and runs it | `AppHost/AppHost.cs`: PostgreSQL, Kafka, the API, the third party |
| **[TUnit](https://tunit.dev)** | Runs the tests and gives each one its own trace | `ApiFixture` starts the AppHost once per session; every test is a span |
| **[Testcontainers](https://dotnet.testcontainers.org)** | Runs what only the tests need | a [WireMock](https://github.com/WireMock-Net/WireMock.Net) fake and the trace dashboard |

[OpenTelemetry](https://opentelemetry.io) connects them. You will see it everywhere below.

---

## Idea 1: describe the system once

`AppHost.cs` is the only place the system is described. `aspire run` starts it. `dotnet test` starts it too.

```mermaid
flowchart LR
  run(["aspire run"]) --> host
  test(["dotnet test"]) --> overlay["ApiFixture<br/><i>the test overlay</i>"] --> host
  host["<b>AppHost.cs</b><br/>postgres · kafka · api · some-service"]
```

```csharp
// AppHost/AppHost.cs (trimmed)
var ordersDb = builder.AddPostgres("postgres").WithInitFiles("./Database").AddDatabase("ordersdb");
var kafka    = builder.AddKafka("kafka");   // + creates orders-topic once ready

var api = builder.AddDotnetProject("api", "../Api/Api.csproj")
    .WithReference(ordersDb, "Database").WaitFor(ordersDb)
    .WithEnvironment("Kafka__ConnectionString", kafka).WaitFor(kafka);

// The third party is just a URL. If none is configured, a local stand-in runs instead.
api.WithEnvironment("SomeService__BaseUrl", builder.AddExternalService("some-service", builder.AddParameter("some-service-url")));
```

The tests don't rebuild any of this. `ApiFixture` only says **what is different in a test**:

```csharp
// Tests/ApiFixture.cs (trimmed)
public class ApiFixture : AspireFixture<AppHost> {
  // every third party → a WireMock stub this test session controls
  public ExampleServiceStub Stub => field ??= new(Wiremock);
  private IEnumerable<ExternalServiceStub> Stubs => [Stub];
  protected override string[] Args => [.. Stubs.Select(s => $"--Parameters:{s.UrlParameter}={s.UrlFor(Mode)}")];
}
```

So the schema, the topic, the connection strings and the startup order are the same ones you run every day. You change infrastructure in one place, and the tests follow.

| | `aspire run` | `dotnet test` |
| --- | --- | --- |
| PostgreSQL, Kafka, schema, topic | AppHost | AppHost (same) |
| API | AppHost | AppHost, or in-process, or a container ([see below](#where-the-api-runs)) |
| Inventory service | stand-in, or a real URL | **WireMock**, stubbed per test |
| Dashboard | Aspire's own | a reused test dashboard at `:18888` |

> [!IMPORTANT]
> If the AppHost declares an external service and `ApiFixture.Stubs` has no stub for it, the session stops before the first test.

---

## Idea 2: a test is a trace

TUnit starts a new trace for every test. The API is instrumented with OpenTelemetry, so it carries that trace on every call it makes: the HTTP call to the inventory service and the message it puts on Kafka.

That trace id works as the test's **name tag**:

```mermaid
sequenceDiagram
  autonumber
  participant T as Test (trace 4bf9)
  participant W as WireMock
  participant A as Api
  participant P as PostgreSQL
  participant K as Kafka
  T->>W: stub GET /inventory/SKU-1 = 100, only for trace 4bf9
  T->>A: POST /api/orders (traceparent 4bf9)
  A->>W: GET /inventory/SKU-1 (traceparent 4bf9)
  W-->>A: inventory 100
  A->>P: INSERT order
  A->>K: OrderCreated (header traceparent 4bf9)
  A-->>T: 200 OK
  T->>K: read orders-topic, keep trace 4bf9 only
  K-->>T: OrderCreated
```

The API has no test code in it. It passes the trace along because that is what instrumented services do. The tests use that to stay apart:

**WireMock stubs answer only their own test.** Every stub matches on the current trace id:

```csharp
// Tests/Fixtures/WireMockFixture.cs (trimmed)
new HeaderModel {
  Name = "traceparent",
  Matchers = [new MatcherModel { Name = "WildcardMatcher", Pattern = $"*{Activity.Current.TraceId}*" }]
}
```

**Kafka reads see only their own messages.** The consumer skips messages from other traces:

```csharp
// Tests/Clients/KafkaClient.cs (trimmed)
DistributedContextPropagator.Current.ExtractTraceIdAndState(message.Headers, ..., out var receivedTraceId, out _);
if (!receivedTraceId.Contains(Activity.Current.TraceId.ToString())) continue;  // another test's order
```

So tests run in parallel against **one** WireMock and **one** topic, with no cleanup and no locks. And they still read like plain tests:

```csharp
[Test]
public async Task Should_PublishKafkaMessage_When_OrderIsCreated()
{
  var client = App.ApiClient();
  using var kafka = App.KafkaClient("orders-topic");

  var sku = "SKU-KAFKA-001";
  await App.Stub.SetupInventory(sku, new InventoryResponse(100));

  var response = await client.PostOrder(sku, 25);
  await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

  var hasMessage = kafka.ConsumeForCurrentTrace(TimeSpan.FromSeconds(10), out var receivedMessage);

  await Assert.That(hasMessage).IsTrue();
  Assert.NotNull(receivedMessage);
  await Assert.That(receivedMessage.Sku).IsEqualTo(sku);
  await Assert.That(receivedMessage.Quantity).IsEqualTo(25);
}
```

---

## Idea 3: see it

Each trace is sent to a dashboard container that stays up between runs. Open **http://localhost:18888** and the trace list reads like your test list. Each one shows the whole story:

```
Should_PublishKafkaMessage_When_OrderIsCreated   ██████████████████████████████████  functional-tests
  POST /api/orders                                 ████████████████████████████████  functional-tests
    POST /api/orders                                ██████████████████████████████   api
      GET /inventory/SKU-KAFKA-001  → WireMock       ██████                          api
      INSERT INTO orders            → PostgreSQL           ███                       api
      publish OrderCreated          → Kafka                   █████                  api
```

<sub>Roughly what you'll see. Span names come from the OpenTelemetry instrumentation.</sub>

When a test fails, you don't guess. Open its trace and you see which step went wrong: the stub that never matched, the insert that threw, the message that never left.

- Traces from earlier runs stay, because the dashboard container is reused.
- TUnit's HTML report (`TestResults/*-report.html`) includes the API's spans too.
- OTLP endpoints: `18889` (gRPC), `18890` (HTTP).

---

## Where the API runs

One switch, same tests:

| `TESTS_API_HOST` | The API runs as | Use it when |
| --- | --- | --- |
| *(unset)* or `Aspire` | a project launched by the AppHost, like `aspire run` | every day |
| `InProcess` | part of the test process (`WebApplicationFactory`) | you want breakpoints in the API |
| `Container` | its published image, on Aspire's network | CI: test the image you ship |

```console
TESTS_API_HOST=InProcess dotnet test Tests/Tests.csproj
TESTS_API_HOST=Container dotnet test Tests/Tests.csproj
```

All three get their configuration from the same AppHost model. `Fixtures/ApiHosts.cs` reuses the AppHost's wiring for the in-process and container hosts.

---

## Adding a third-party service

Three small steps:

1. **AppHost:** declare it as a URL. `builder.AddExternalService("payments", builder.AddParameter("payments-url"))`, with a stand-in for `aspire run` if you want one.
2. **Stub:** `class PaymentsStub(WireMockFixture w) : ExternalServiceStub(w, "payments-url")`, with methods that read as intent: `Approve(...)` calls `DefineMock(...)`.
3. **ApiFixture:** expose it and register it. `public PaymentsStub Payments => field ??= new(Wiremock);` and add `Payments` to `Stubs`, so tests read `App.Payments.Approve(...)`.

If the AppHost declares an external service with no stub, the session stops before the first test and names it.

---

## Repo map

```
AppHost/
  AppHost.cs                  the system, used by aspire run and by the tests
  Database/orders.sql         schema, applied when postgres starts
Api/
  Program.cs                  POST /api/orders, /health
SomeService/
  Program.cs                  stand-in inventory service (aspire run only)
Tests/
  OrderTests.cs               the tests
  ApiFixture.cs               the overlay: fakes, telemetry, where the API runs
  GlobalSetup.cs              sends test spans to the dashboard and names them after the tests
  Clients/                    ApiClient (HTTP), KafkaClient (reads this test's messages)
  Stubs/                      ExternalServiceStub base; ExampleServiceStub: inventory stubs
  Fixtures/                   WireMock, dashboard, API hosts, WebApplicationFactory
```

---

<sub>Built with Aspire 17 · TUnit + TUnit.Aspire · Testcontainers · WireMock.Net · OpenTelemetry · Npgsql · Confluent.Kafka</sub>

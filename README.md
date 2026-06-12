# Event Ledger (.NET 8)

Cloud-native Event Ledger solution with two independent microservices:

- **Event Gateway API** (`src/EventGateway.Api`)
- **Account Service** (`src/AccountService.Api`)

## Architecture

Each service follows a clean architecture split:

- `Domain`
- `Application` (CQRS via MediatR + FluentValidation)
- `Infrastructure` (EF Core InMemory + outbound adapters)
- `Api`

Implemented capabilities:

- .NET 8 Web APIs
- Independent in-memory databases per service
- Synchronous REST communication (Gateway -> Account)
- Idempotency with `eventId`
- Out-of-order tolerance with timestamp sorting
- Health checks (`/health`)
- Serilog structured JSON logs
- OpenTelemetry tracing + metrics
- W3C trace context propagation (`traceparent`)
- Polly resiliency on Gateway outbound calls (retry with exponential backoff, circuit breaker, per-attempt timeout)
- Graceful degradation (Gateway POST returns `503` when Account service is unavailable; reads continue from local DB)

## Endpoints

### Event Gateway API

- `POST /events`
- `GET /events/{id}`
- `GET /events?account={accountId}`
- `GET /health`

### Account Service

- `POST /accounts/{accountId}/transactions`
- `GET /accounts/{accountId}/balance`
- `GET /accounts/{accountId}`
- `GET /health`

## Run locally

```bash
dotnet build EventLedger.slnx
dotnet run --project src/AccountService.Api/AccountService.Api.csproj
dotnet run --project src/EventGateway.Api/EventGateway.Api.csproj
```

Event Gateway uses `AccountService__BaseUrl` (default `http://localhost:8081`).

## Run tests

```bash
dotnet test EventLedger.slnx
```

Test coverage includes:

- idempotency
- out-of-order correctness
- balance correctness
- validation behavior
- resiliency behavior under Account Service failure
- trace propagation
- full Gateway -> Account integration flow

## Docker

```bash
docker compose up --build
```

- Event Gateway: `http://localhost:8080`
- Account Service: `http://localhost:8081`

## Resiliency choice

Gateway uses Polly (outer → inner policy order):

- **Retry** (3 attempts, exponential backoff: 200ms / 400ms / 800ms)
- **Circuit breaker** (opens after 5 consecutive failures, stays open for 30s)
- **Per-attempt timeout** (2s)

When downstream failures persist, Gateway returns `503` for `POST /events` and still serves read operations from its local event store.

# Event Ledger (.NET 8)

Cloud-native Event Ledger solution with two independent microservices:

- **Event Gateway API** (`src/EventGateway.Api`) — public-facing, accepts events, stores them locally, and coordinates Account Service
- **Account Service** (`src/AccountService.Api`) — internal, maintains account balances

## Architecture diagram

```mermaid
graph TD
    Client([Client])
    GW[Event Gateway API :8080]
    AS[Account Service :8081]
    GDB[(Gateway InMemory DB)]
    ADB[(Account InMemory DB)]

    Client -- POST /events --> GW
    Client -- GET /events --> GW
    GW -- POST /accounts/id/transactions --> AS
    GW --- GDB
    AS --- ADB
```

## Architecture overview

Each service is independently runnable and follows a **Clean Architecture** split:

| Layer | Responsibility |
|---|---|
| `Domain` | Entities and enums; no external dependencies |
| `Application` | CQRS commands/queries via MediatR, FluentValidation pipeline, idempotency |
| `Infrastructure` | EF Core InMemory persistence, outbound HTTP adapters (Gateway only) |
| `Api` | Controllers, Serilog setup, OpenTelemetry wiring |

### Service interactions

1. A client `POST /events` to the **Event Gateway**.
2. The Gateway validates and idempotency-checks the incoming event (duplicate `eventId` returns immediately).
3. The Gateway calls the **Account Service** `POST /accounts/{accountId}/transactions` synchronously, forwarding the W3C `traceparent` header.
4. On success, the Gateway persists the event locally and returns `201 Created`.
5. If the Account Service is unreachable the Gateway returns `503 Service Unavailable` — read endpoints (`GET /events`) continue to work from the local store.

### Implemented capabilities

- .NET 8 Web APIs
- Independent in-memory databases per service
- Synchronous REST communication (Gateway → Account)
- Per-EventId locking for atomic idempotency (concurrent requests with the same `eventId` are serialized)
- Out-of-order tolerance with timestamp sorting
- Health checks (`/health`)
- Serilog structured JSON logs
- OpenTelemetry tracing + metrics
- W3C trace context propagation (`traceparent`)
- Polly resiliency on Gateway outbound calls (timeout + retry with exponential backoff)
- Graceful degradation (Gateway `POST /events` returns `503` when Account service is unavailable; reads continue from local DB)

## Endpoints

### Event Gateway API

| Method | Path | Description |
|---|---|---|
| `POST` | `/events` | Submit a new event |
| `GET` | `/events/{id}` | Retrieve event by ID |
| `GET` | `/events?account={accountId}` | List events for an account (sorted by timestamp) |
| `GET` | `/health` | Health check |

### Account Service

| Method | Path | Description |
|---|---|---|
| `POST` | `/accounts/{accountId}/transactions` | Apply a transaction |
| `GET` | `/accounts/{accountId}/balance` | Get current balance |
| `GET` | `/accounts/{accountId}` | Get account details with transaction history |
| `GET` | `/health` | Health check |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Docker Compose run)

Restore NuGet packages before building:

```bash
dotnet restore EventLedger.slnx
```

## Run locally

Start Account Service first (Gateway depends on it):

```bash
dotnet run --project src/AccountService.Api/AccountService.Api.csproj
```

In a second terminal, start Event Gateway:

```bash
dotnet run --project src/EventGateway.Api/EventGateway.Api.csproj
```

The Gateway reads `AccountService__BaseUrl` from environment (default `http://localhost:8081`).  
Override it by setting the variable before running:

```bash
AccountService__BaseUrl=http://localhost:8081 dotnet run --project src/EventGateway.Api/EventGateway.Api.csproj
```

Build the full solution:

```bash
dotnet build EventLedger.slnx
```

## Run tests

```bash
dotnet test EventLedger.slnx
```

Test coverage includes:

- Idempotency (duplicate `eventId` is not re-applied)
- Out-of-order correctness (events applied regardless of arrival order)
- Balance correctness (`CREDIT - DEBIT`)
- Validation behavior (invalid payloads rejected with errors)
- Resiliency behavior under Account Service failure (Gateway returns `503`)
- Trace propagation (`traceparent` forwarded to Account Service)
- Full Gateway → Account integration flow

## Docker

```bash
docker compose up --build
```

- Event Gateway: `http://localhost:8080`
- Account Service: `http://localhost:8081`

Both services are connected via a shared `event-ledger-net` Docker network.

## Resiliency choice

The Gateway uses Polly **timeout + retry with exponential backoff** on all outbound Account Service calls:

| Concern | Configuration |
|---|---|
| Timeout per attempt | 2 seconds (Polly `TimeoutAsync`) |
| Retries | 3 attempts |
| Backoff | 200 ms → 400 ms → 800 ms |
| Transient errors handled | 5xx responses, network errors, timeouts |

`HttpClient.Timeout` is set to `Timeout.InfiniteTimeSpan` so it does not compete with the Polly timeout policy; all timeout control is handled exclusively by Polly.

When all retries are exhausted the Gateway surfaces `503 Service Unavailable` for `POST /events`. Read operations (`GET /events` and `GET /events/{id}`) are served entirely from the Gateway's local store and are unaffected by Account Service availability.

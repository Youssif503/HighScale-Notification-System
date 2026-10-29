# High Scale Notification System

A .NET 10 Web API and background worker for accepting email notifications, storing them in PostgreSQL or SQL Server, and delivering them asynchronously through RabbitMQ. Redis records notification processing state to suppress duplicate deliveries.

## Features

- `POST /api/notification` accepts email notification requests.
- Notification records are persisted before their IDs are published to RabbitMQ.
- A hosted worker consumes notification IDs and sends email through SMTP.
- Redis uses an atomic claim keyed by `NotificationId`, a processing lease, and a completed marker to handle duplicate queue deliveries.
- Unit and HTTP integration tests run with an in-memory database and a fake message publisher.

## Architecture

```text
HTTP client
    |
    v
Notification API -> Database -> RabbitMQ queue: notifications
                                      |
                                      v
                               Background worker -> Redis idempotency claim
                                      |
                                      v
                                  SMTP server
```

The API persists each notification with `pending` status. The worker marks it `send` after the email provider reports success. If sending fails, it releases its Redis processing claim and requeues the RabbitMQ message.

## Requirements

- .NET SDK 10
- PostgreSQL or SQL Server
- RabbitMQ
- Redis
- An SMTP server

All four services must be reachable by the API process. When running in Docker, use service DNS names or otherwise reachable host names instead of `localhost` for dependencies in other containers.

## Configuration

Configuration is read from `NotificationSystem/appsettings.json` and can be overridden with environment variables. Use double underscores for nested keys.

| Setting | Environment variable | Purpose |
| --- | --- | --- |
| `Database:Provider` | `Database__Provider` | `Postgres` (default) or `SqlServer` |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Database connection string |
| `RabbitMQ:Host` | `RabbitMQ__Host` | RabbitMQ host |
| `RabbitMQ:User` | `RabbitMQ__User` | RabbitMQ username |
| `RabbitMQ:Password` | `RabbitMQ__Password` | RabbitMQ password |
| `Redis:ConnectionString` | `Redis__ConnectionString` | Redis `host:port` with optional `,password=...` |
| `Redis:ProcessingLeaseSeconds` | `Redis__ProcessingLeaseSeconds` | Duration of an in-progress claim (default: 300 seconds) |
| `Redis:CompletedKeyRetentionDays` | `Redis__CompletedKeyRetentionDays` | Completed key retention (default: 30 days) |
| `Email:Smtp:Host` | `Email__Smtp__Host` | SMTP server host |
| `Email:Smtp:Port` | `Email__Smtp__Port` | SMTP server port |
| `Email:Smtp:UseSsl` | `Email__Smtp__UseSsl` | Enable SMTP TLS |
| `Email:Smtp:Username` | `Email__Smtp__Username` | Optional SMTP username |
| `Email:Smtp:Password` | `Email__Smtp__Password` | Optional SMTP password |
| `Email:FromAddress` | `Email__FromAddress` | Sender email address |
| `Email:FromName` | `Email__FromName` | Sender display name |
| `Email:Subject` | `Email__Subject` | Email subject |

Do not store production credentials in checked-in settings files. Supply them through environment variables or a secrets manager.

The Redis client currently supports a basic `host:port,password=...` connection string. Redis TLS and advanced connection options are not supported by this client.

## Database setup

The application uses EF Core with PostgreSQL by default. Set `Database:Provider` to `SqlServer` to use SQL Server instead. Ensure the database schema exists before starting the application. This repository does not currently include EF Core migrations or automatic schema creation.

The worker looks up the user referenced by `UserId` and uses that user's email address. Add the user record to the `Users` table before requesting a notification. There is not currently a user management endpoint.

## Run locally

1. Start PostgreSQL/SQL Server, RabbitMQ, Redis, and an SMTP server.
2. Update `NotificationSystem/appsettings.json` or provide environment variable overrides.
3. Ensure the database schema and recipient user exist.
4. Start the API and worker:

   ```bash
   dotnet run --project NotificationSystem/NotificationSystem.csproj
   ```

In Development, the OpenAPI document is exposed at `/openapi/v1.json`.

## API

### Submit a notification

`POST /api/notification`

Request body:

```json
{
  "userId": "user-123",
  "body": "Your order has shipped.",
  "channel": "email"
}
```

Successful requests return `202 Accepted`. `Body`, `UserId`, and `Channel` are required. Currently, `email` is the only supported channel. Invalid requests return `400 Bad Request`.

## Run tests

```bash
dotnet test NotificationSystem.sln
```

The HTTP integration tests host the API on a local ephemeral port and replace RabbitMQ publishing with a test double. They do not require live database, Redis, RabbitMQ, or SMTP services. SMTP delivery and Redis server behavior are not exercised by the current automated tests.

## Build a Docker image

Run from the repository root, using the application directory as the build context:

```bash
docker build -t notification-system ./NotificationSystem
```

Configure the container with the same environment variables described above before running it. The container runs both the HTTP API and the RabbitMQ background worker in one process.

## Delivery semantics

Redis idempotency prevents concurrent workers from processing the same `NotificationId` and allows completed messages to be acknowledged without sending again. Email delivery cannot be guaranteed exactly once across a process failure: the SMTP server could accept an email immediately before the worker crashes and records completion. Completed Redis keys expire after the configured retention period.

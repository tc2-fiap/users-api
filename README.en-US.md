**English** · [Português](README.pt-BR.md)

# FIAP Games — Users API

Registration, login, Google sign-in, JWT issuance, and role management (`Player`/`Admin`) — the only service any other service trusts to issue tokens. Owns the `users` Postgres schema.

## Run standalone

```bash
cp .env.example .env
docker compose up --build
```

Brings up this service plus its own Postgres (and RabbitMQ, since it publishes `UserCreatedEvent`). API on `localhost:8081`, Swagger at `/swagger`.

## Run as part of the system

Deployed by the [`orchestration`](https://github.com/tc2-fiap/orchestration) Helm chart alongside the other four backend services and the frontend — see [`../orchestration/README.en-US.md`](../orchestration/README.en-US.md). Reached through the shared Ingress at `/api/users/*`.

## What's here

- `Domain/User.cs` — `PasswordHash` is nullable (null for Google-only accounts); `GoogleSubjectId`; `Role`.
- `Domain/UserEvent.cs` — a system-wide audit log of every `UserCreatedEvent` published (raw payload, not a summary), mirroring [orders-api](https://github.com/tc2-fiap/orders-api)'s `OrderEvent` (`../documentation/spec/notes.md` 43).
- Publishes `UserCreatedEvent` on registration (and once, idempotently, for the seeded admin — see `../documentation/spec/notes.md` 32).
- `GET /api/users/config` — anonymous; tells the frontend whether Google sign-in is configured, so it never renders a button guaranteed to fail.
- `GET /api/users/admin/events` — admin-only, paginated, filterable by `eventType`/`from`/`to`; the system-wide (not per-order) event listing behind the frontend's `/admin/events` page (`../documentation/spec/notes.md` 43).
- `PUT /api/users/{id}/role` — admin-only; promotes another user (the first admin is seeded from config at startup, since promotion needs an existing admin). Rejects `id == callerId` — an admin can't change their own role — and publishes `RoleChangedEvent`, persisted into the same `UserEvent` audit log.
- `DELETE /api/users/{id}` — admin-only; also rejects `id == callerId`, so an admin can't delete their own account.
- `POST /api/users/login` locks an account for 15 minutes after 5 consecutive failed attempts (`User.FailedLoginAttempts`/`LockedUntilUtc`), reset on a successful login.
- `POST /api/users/logout` — publishes `TokenRevokedEvent`, consumed by every one of the six services to reject that token immediately instead of waiting out its natural expiry.
- Registration's `Password` rule: 12+ characters, all four character classes (upper/lower/digit/special), and a small common-weak-password denylist.
- One `Admin` account is seeded at startup from `Admin:Email`/`Admin:Password` config, idempotently.

## Test

```bash
cd tests/FiapGames.Users.Tests && dotnet test
```

## Documentation

Full architecture, event contracts, and the project-wide decision record live in the `documentation` repo — [`github.com/tc2-fiap/documentation`](https://github.com/tc2-fiap/documentation) (or `../documentation/` if you have it cloned as a sibling) — see [`ARCHITECTURE.en-US.md`](https://github.com/tc2-fiap/documentation/blob/main/architecture/ARCHITECTURE.en-US.md) and [`instructions.md`](https://github.com/tc2-fiap/documentation/blob/main/spec/instructions.md) §4.1.

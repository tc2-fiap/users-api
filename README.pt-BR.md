[English](README.en-US.md) · **Português**

# FIAP Games — Users API

Cadastro, login, login com Google, emissão de JWT e gerenciamento de papéis (`Player`/`Admin`) — o único serviço em que qualquer outro confia para emitir tokens. Dono do schema `users` no Postgres.

## Rodar de forma independente

```bash
cp .env.example .env
docker compose up --build
```

Sobe este serviço mais seu próprio Postgres (e RabbitMQ, já que ele publica `UserCreatedEvent`). API em `localhost:8081`, Swagger em `/swagger`.

## Rodar como parte do sistema

Implantado pelo chart Helm [`orchestration`](https://github.com/tc2-fiap/orchestration) junto com os outros quatro serviços de backend e o frontend — ver [`../orchestration/README.pt-BR.md`](../orchestration/README.pt-BR.md). Acessado pelo Ingress compartilhado em `/api/users/*`.

## O que tem aqui

- `Domain/User.cs` — `PasswordHash` é anulável (nulo para contas somente-Google); `GoogleSubjectId`; `Role`.
- `Domain/UserEvent.cs` — um log de auditoria de todo o sistema com cada `UserCreatedEvent` publicado (payload bruto, não um resumo), espelhando o `OrderEvent` do [orders-api](https://github.com/tc2-fiap/orders-api) (`../documentation/spec/notes.md` 43).
- Publica `UserCreatedEvent` no cadastro (e uma vez, de forma idempotente, para o admin semeado — ver `../documentation/spec/notes.md` 32).
- `GET /api/users/config` — anônimo; informa ao frontend se o login com Google está configurado, para que ele nunca renderize um botão fadado a falhar.
- `GET /api/users/admin/events` — somente admin, paginado, filtrável por `eventType`/`from`/`to`; a listagem de eventos de todo o sistema (não por pedido) por trás da página `/admin/events` do frontend (`../documentation/spec/notes.md` 43).
- `PUT /api/users/{id}/role` — somente admin; promove outro usuário (o primeiro admin é semeado a partir de configuração na inicialização, já que a promoção exige um admin existente).
- Uma conta `Admin` é semeada na inicialização a partir das configurações `Admin:Email`/`Admin:Password`, de forma idempotente.

## Testar

```bash
cd tests/FiapGames.Users.Tests && dotnet test
```

## Documentação

A arquitetura completa, os contratos de eventos e o registro de decisões do projeto vivem em [`../documentation/`](../documentation/) (também publicado em [github.com/tc2-fiap/documentation](https://github.com/tc2-fiap/documentation)) — ver [`DOCUMENTATION.pt-BR.md`](../documentation/narrative/DOCUMENTATION.pt-BR.md) e [`instructions.md`](../documentation/spec/instructions.md) §4.1 (em inglês).

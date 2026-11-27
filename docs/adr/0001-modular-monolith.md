# 0001. Modular monolith over microservices

Status: accepted

## Context

The system covers one bounded area, disputes of debit orders and card transactions, with three closely related parts: transactions and mandates, disputes, and notifications. It must run on Kubernetes and scale horizontally. It is built and reviewed by one person.

## Decision

Build one deployable ASP.NET Core API, split internally into three modules: `Transactions`, `Disputes` and `Notifications`. Each module is its own project with its own database schema and migrations. A module exposes a small public `Contracts` namespace and keeps everything else `internal`. Architecture tests fail the build if a module reaches past another module's contracts.

## Alternatives considered

- **Microservices.** Each part could scale and deploy on its own. For a domain this small that buys nothing and costs network calls between services, distributed transactions and far more infrastructure.
- **A layered monolith without module boundaries.** Simpler at first, but the parts grow into each other and cannot be separated later without a rewrite.

## Consequences

- Scaling out is by adding replicas. This works because the API is stateless: every request carries a token, and background workers claim rows with `FOR UPDATE SKIP LOCKED` so replicas never process the same row.
- All modules scale together. If one part later needs far more capacity than the others, it can be extracted into a service, because its boundary is already enforced.
- One database is a single point of failure and a shared resource. Schema-per-module keeps ownership clear and makes a later split mechanical.

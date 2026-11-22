# 0012. Plain handlers instead of MediatR and AutoMapper

Status: accepted

## Context

MediatR, AutoMapper and FluentAssertions are common defaults in .NET projects. All three have moved to commercial licences.

## Decision

Use cases are plain classes resolved from the container and called directly by the endpoint. Mapping between domain objects and response models is written by hand. Test assertions use Shouldly.

## Consequences

- No licence exposure and three fewer dependencies.
- Call paths are explicit: an endpoint calls a named handler, which is easy to follow and to debug.
- There is no pipeline for cross-cutting behaviour. Validation runs as an endpoint filter, and logging, authorisation and rate limiting run as ASP.NET Core middleware, which is where they belong.

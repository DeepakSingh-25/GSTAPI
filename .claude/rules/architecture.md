---
alwaysApply: true
description: >
  Dependency direction, layer placement and endpoint organization for Gst API
  (Clean Architecture with MediatR pipeline + generic repository + Unit of Work + MVC controllers).
---

# Architecture Rules (Gst API)

> Adapted for this repo. The upstream dotnet-claude-kit version of this file recommends Minimal API
> endpoint modules and no repository pattern; this project deliberately uses controllers + MediatR +
> repositories + Unit of Work. Follow the existing shape. Propose a migration only when the user asks.

## Layers and dependency direction

```
src/Core/Gst.Domain                  entities (BaseEntity), ApiResponse, DataGrid, constants, config POCOs   (references nothing)
src/Core/Gst.Application             Features/<Name>/ (record + validator + handler), DTOs, interfaces, behaviors -> Domain
src/Infrastructure/Gst.Persistence   ApplicationDbContext (= IUnitOfWork), configurations, repositories, services -> Application
src/Presentation/Gst.Api             controllers, middleware, extensions, Program.cs                          -> Persistence
```

- **Never reverse an arrow.** Domain has no packages. Application must not reference EF Core,
  SqlClient or ASP.NET (only MediatR, FluentValidation, Extensions abstractions).
- **EF Core / Dapper only in Persistence.** Handlers talk to interfaces in
  `Gst.Application/Interfaces/{IRepository,IService,IContext}`.

## Data access

- Generic `IRepository<T>` / `Repository<T>` gives CRUD + `SoftDelete`. Per-aggregate
  `I<Name>Repository : IRepository<Name>` adds only extra queries.
- Repositories never call `SaveChanges`; the handler calls `IUnitOfWork.SaveChangesAsync` once.
- `AsNoTracking()` on reads; pass the `CancellationToken` through.
- One `IEntityTypeConfiguration<T>` per entity in `Gst.Persistence/Configurations/`.
- Register repositories `AddScoped` in `Gst.Persistence/PersistenceServiceRegistration.cs`.

## Use cases

- One folder per feature: `Gst.Application/Features/<Name>/`. Each file holds the
  `record ... : IRequest<ApiResponse<TDto>>`, its `AbstractValidator`, and its handler.
- Pipeline order: Logging → Validation → Caching (`ApplicationServicesRegistration.cs`).
- Business failures return `ApiResponse<T>.Fail(msg, code)`; unexpected errors throw.

## Endpoints

- MVC controllers in `Gst.Api/Controllers` inheriting `BaseController`
  (`[ApiController]`, `[Authorize]`, `[Route("api/[controller]")]`). An action does one
  `Mediator.Send(..., cancellationToken)` and returns `ReturnResult(...)` — no business logic, no `DbContext`.
- Public actions say `[AllowAnonymous]`; admin actions `[Authorize(Policy = Policies.AdminOnly)]`.
- JSON is ASP.NET default camelCase (Angular `../Web` expects it). Never return an entity.

## Shared code

- DTOs live beside their feature (`Features/<Name>/<Name>Dtos.cs`) with a static `From(entity)` mapper.
- Config POCOs in `Gst.Domain/Configurations`, bound in `PersistenceServiceRegistration.cs`.
- Business rules belong in handlers or entities, not in DTOs, controllers or static helpers.

using System.Security.Claims;
using FiapGames.Shared.Infrastructure.Extensions;
using FiapGames.Shared.Kernel.Pagination;
using FiapGames.Users.Api.Application.Abstractions;
using FiapGames.Users.Api.Application.Dtos;
using FluentValidation;
using Microsoft.Extensions.Configuration;

namespace FiapGames.Users.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users").WithTags("Users");

        // Public and non-sensitive — a Google OAuth client ID is meant to
        // be embedded in client-side pages. Lets the frontend decide
        // whether to render the "Sign in with Google" button at all,
        // rather than showing one that always fails when unconfigured.
        group.MapGet("/config", (IConfiguration configuration) =>
        {
            var clientId = configuration["Google:ClientId"];
            return Results.Ok(new
            {
                googleSignInEnabled = !string.IsNullOrWhiteSpace(clientId),
                googleClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId
            });
        }).AllowAnonymous();

        group.MapPost("/register", async (
            RegisterUserRequest request,
            IValidator<RegisterUserRequest> validator,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var result = await service.RegisterAsync(request, cancellationToken);
            return result.ToHttpResult(user => Results.Created($"/api/users/{user.Id}", user));
        }).AllowAnonymous();

        group.MapPost("/login", async (
            LoginRequest request,
            IValidator<LoginRequest> validator,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var result = await service.LoginAsync(request, cancellationToken);
            return result.ToHttpResult();
        }).AllowAnonymous();

        group.MapPost("/login/google", async (
            GoogleLoginRequest request,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.LoginWithGoogleAsync(request, cancellationToken);
            return result.ToHttpResult();
        }).AllowAnonymous();

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            return Results.Ok(new { id, email = user.FindFirstValue(ClaimTypes.Email) });
        }).RequireAuthorization();

        group.MapGet("/{id:guid}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(id, cancellationToken);
            return result.ToHttpResult();
        }).RequireAuthorization();

        group.MapGet("/", async ([AsParameters] PagedRequest request, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetPagedAsync(request, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization();

        group.MapGet("/admin/events", async (
            [AsParameters] PagedRequest request,
            string? eventType,
            DateTime? from,
            DateTime? to,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAllUserEventsAdminAsync(request, eventType, from, to, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

        group.MapGet("/admin/search", async (
            [AsParameters] PagedRequest request,
            string? name,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SearchUsersAdminAsync(request, name, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateUserRequest request,
            IValidator<UpdateUserRequest> validator,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var result = await service.UpdateAsync(id, request, cancellationToken);
            return result.ToHttpResult();
        }).RequireAuthorization();

        group.MapPut("/{id:guid}/role", async (
            Guid id,
            UpdateRoleRequest request,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateRoleAsync(id, request.Role, cancellationToken);
            return result.ToHttpResult();
        }).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

        group.MapDelete("/{id:guid}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return result.ToHttpResult();
        }).RequireAuthorization();

        return endpoints;
    }
}

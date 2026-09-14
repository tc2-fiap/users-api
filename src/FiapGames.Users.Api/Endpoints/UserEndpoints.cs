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
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints, Func<IResult> getVersion)
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
            IValidator<GoogleLoginRequest> validator,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var result = await service.LoginWithGoogleAsync(request, cancellationToken);
            return result.ToHttpResult();
        }).AllowAnonymous();

        // Best-effort revocation: publishes a TokenRevokedEvent for the
        // caller's own jti so every service stops honoring this token even
        // before it naturally expires. If the token is somehow missing
        // jti/exp (shouldn't happen — JwtTokenService always sets both),
        // this still returns 204 rather than erroring — there's nothing
        // more the client can do differently.
        group.MapPost("/logout", async (ClaimsPrincipal caller, IUserService service, CancellationToken cancellationToken) =>
        {
            var jti = caller.FindFirstValue("jti");
            var expClaim = caller.FindFirstValue("exp");
            if (jti is not null && expClaim is not null && long.TryParse(expClaim, out var expUnix))
            {
                var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
                await service.LogoutAsync(jti, expiresAtUtc, cancellationToken);
            }

            return Results.NoContent();
        }).RequireAuthorization();

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            return Results.Ok(new { id, email = user.FindFirstValue(ClaimTypes.Email) });
        }).RequireAuthorization();

        // Admin-only: this is a lookup by arbitrary id, not the caller's own
        // profile (that's GET /me) — without a role check here, any
        // authenticated Player could read any other user's record by
        // guessing/incrementing a GUID.
        group.MapGet("/{id:guid}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(id, cancellationToken);
            return result.ToHttpResult();
        }).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

        // Admin-only: lists every user in the system, same as every other
        // "list everything" endpoint in this codebase (orders-api's
        // /admin, payments-api's /admin, this file's own /admin/search).
        group.MapGet("/", async ([AsParameters] PagedRequest request, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetPagedAsync(request, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

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

        // Admin-dashboard-facing twin of the bare /version (see Program.cs):
        // same handler, reached via the Ingress like any other route in
        // this group instead of only via kubectl port-forward, gated to Admin.
        group.MapGet("/version", getVersion).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

        // Admin-only: UpdateAsync has no ownership check, so without a role
        // check here any authenticated Player could rewrite any other
        // user's name/email by id.
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
        }).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

        group.MapPut("/{id:guid}/role", async (
            Guid id,
            UpdateRoleRequest request,
            IValidator<UpdateRoleRequest> validator,
            ClaimsPrincipal caller,
            IUserService service,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub")!);
            var result = await service.UpdateRoleAsync(id, request.Role, callerId, cancellationToken);
            return result.ToHttpResult();
        }).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

        // Admin-only: DeleteAsync has no ownership check, so without a role
        // check here any authenticated Player could delete any other
        // user's account by id.
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal caller, IUserService service, CancellationToken cancellationToken) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub")!);
            var result = await service.DeleteAsync(id, callerId, cancellationToken);
            return result.ToHttpResult();
        }).RequireAuthorization(p => p.RequireRole(nameof(Domain.UserRole.Admin)));

        return endpoints;
    }
}

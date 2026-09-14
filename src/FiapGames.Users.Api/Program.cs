using System.Text.Json;
using FiapGames.Contracts;
using FiapGames.Shared.Infrastructure.Auth;
using FiapGames.Shared.Infrastructure.Extensions;
using FiapGames.Users.Api.Application.Abstractions;
using FiapGames.Users.Api.Application.Services;
using FiapGames.Users.Api.Application.Validators;
using FiapGames.Users.Api.Consumers;
using FiapGames.Users.Api.Domain;
using FiapGames.Users.Api.Endpoints;
using FiapGames.Users.Api.Infrastructure.Auth;
using FiapGames.Users.Api.Infrastructure.Persistence;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

// Built from parts, not a single ConnectionStrings entry, so only the
// password needs to come from a Kubernetes Secret — host/port/database/
// username/schema are non-secret ConfigMap values.
var postgresConnectionString =
    $"Host={builder.Configuration["Postgres:Host"] ?? "localhost"};" +
    $"Port={builder.Configuration["Postgres:Port"] ?? "5432"};" +
    $"Database={builder.Configuration["Postgres:Database"] ?? "fiap_games"};" +
    $"Username={builder.Configuration["Postgres:Username"] ?? "users_role"};" +
    $"Password={builder.Configuration["Postgres:Password"]};" +
    $"Search Path={builder.Configuration["Postgres:SearchPath"] ?? "users"}";

builder.Services.AddDbContext<UsersDbContext>(options =>
    options.UseNpgsql(postgresConnectionString));

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddGlobalExceptionHandling();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterUserRequestValidator>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<TokenRevokedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(
            builder.Configuration["RabbitMq:Host"] ?? "localhost",
            builder.Configuration["RabbitMq:VirtualHost"] ?? "/",
            h =>
            {
                h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
                h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
            });

        // Explicit, service-scoped endpoint name — see orders-api's
        // Program.cs for why relying on MassTransit's default naming
        // (which ignores the namespace) is unsafe once two services
        // declare a same-named consumer class for the same event. Every
        // other service consuming TokenRevokedEvent uses this exact same
        // per-service queue name shape.
        cfg.ReceiveEndpoint("users-api-token-revoked", e =>
        {
            e.ConfigureConsumer<TokenRevokedConsumer>(context);
        });
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "FIAP Games — Users API", Version = "v1" });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a valid JWT token."
    };
    options.AddSecurityDefinition("Bearer", securityScheme);
    options.AddSecurityRequirement(document =>
    {
        var requirement = new OpenApiSecurityRequirement();
        requirement.Add(new OpenApiSecuritySchemeReference("Bearer", document, null), []);
        return requirement;
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
    db.Database.Migrate();

    // Bootstrap: promotion requires an existing admin, so seed exactly one
    // from config the first time this email is seen — and a matching
    // Player account, so a demo/grading walkthrough always has a
    // non-admin login ready without registering one by hand. Both
    // idempotent — a restart never re-creates or resets either.
    await SeedUserIfConfiguredAsync(scope, "Admin", "Admin:Email", "Admin:Password", UserRole.Admin);
    await SeedUserIfConfiguredAsync(scope, "Player", "Player:Email", "Player:Password", UserRole.Player);
}

async Task SeedUserIfConfiguredAsync(IServiceScope scope, string displayName, string emailConfigKey, string passwordConfigKey, UserRole role)
{
    var email = builder.Configuration[emailConfigKey];
    var password = builder.Configuration[passwordConfigKey];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return;

    var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    var existing = await repository.GetByEmailAsync(email);
    if (existing is not null)
        return;

    var user = new User(displayName, email, passwordHasher.Hash(password), role);
    await repository.AddAsync(user);

    var userCreatedEvent = new UserCreatedEvent(user.Id, user.Name, user.Email);
    await repository.AddEventAsync(new UserEvent(user.Id, "UserCreatedEvent", JsonSerializer.Serialize(userCreatedEvent)));
    await repository.SaveChangesAsync();

    // Same announcement a normal registration makes — otherwise
    // notifications-api never learns this account exists and can't
    // address a purchase notification to it (see notes.md 30's
    // UserProjection).
    var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
    await publishEndpoint.Publish(userCreatedEvent);

    Log.Information("Seeded {Role} account for {Email}", role, email);
}

app.UseExceptionHandler();

app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

// Shared by two routes below: the bare /version (unauthenticated, reached
// only via kubectl port-forward — not under /api/* so the Ingress can't
// route to it) and /api/users/version (Admin-gated, reached through the
// Ingress like any other route — the admin dashboard's source for this).
// Same handler, not duplicated logic, registered at two paths because
// nothing else makes the unauthenticated one reachable from a browser.
static IResult GetVersion() => Results.Ok(new
{
    sha = Environment.GetEnvironmentVariable("BUILD_SHA") ?? "unknown",
    buildTime = Environment.GetEnvironmentVariable("BUILD_TIME") ?? "unknown"
});

app.MapHealthChecks("/health");
app.MapGet("/version", GetVersion);

app.MapUserEndpoints(GetVersion);

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;

using System.Text.Json;
using FiapGames.Contracts;
using FiapGames.Shared.Infrastructure.Auth;
using FiapGames.Shared.Infrastructure.Extensions;
using FiapGames.Users.Api.Application.Abstractions;
using FiapGames.Users.Api.Application.Services;
using FiapGames.Users.Api.Application.Validators;
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

        cfg.ConfigureEndpoints(context);
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
    // from config the first time this email is seen. Idempotent — a
    // restart never re-creates or resets it.
    var adminEmail = builder.Configuration["Admin:Email"];
    var adminPassword = builder.Configuration["Admin:Password"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var existingAdmin = await repository.GetByEmailAsync(adminEmail);
        if (existingAdmin is null)
        {
            var admin = new User("Admin", adminEmail, passwordHasher.Hash(adminPassword), UserRole.Admin);
            await repository.AddAsync(admin);

            var userCreatedEvent = new UserCreatedEvent(admin.Id, admin.Name, admin.Email);
            await repository.AddEventAsync(new UserEvent(admin.Id, "UserCreatedEvent", JsonSerializer.Serialize(userCreatedEvent)));
            await repository.SaveChangesAsync();

            // Same announcement a normal registration makes — otherwise
            // notifications-api never learns this account exists and can't
            // address a purchase notification to it (see notes.md 30's
            // UserProjection).
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            await publishEndpoint.Publish(userCreatedEvent);

            Log.Information("Seeded admin account for {Email}", adminEmail);
        }
    }
}

app.UseExceptionHandler();

app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapUserEndpoints();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;

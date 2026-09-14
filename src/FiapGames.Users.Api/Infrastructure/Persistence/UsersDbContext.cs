using FiapGames.Users.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FiapGames.Users.Api.Infrastructure.Persistence;

public sealed class UsersDbContext : DbContext
{
    public const string Schema = "users";

    public DbSet<User> Users => Set<User>();

    public DbSet<UserEvent> UserEvents => Set<UserEvent>();

    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("users");
            builder.HasKey(u => u.Id);
            builder.Property(u => u.Name).IsRequired().HasMaxLength(200);
            builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
            builder.HasIndex(u => u.Email).IsUnique();
            builder.Property(u => u.PasswordHash).IsRequired(false);
            builder.Property(u => u.GoogleSubjectId).HasMaxLength(64);
            builder.HasIndex(u => u.GoogleSubjectId).IsUnique().HasFilter("\"GoogleSubjectId\" IS NOT NULL");
            builder.Property(u => u.FailedLoginAttempts).IsRequired().HasDefaultValue(0);
            builder.Property(u => u.LockedUntilUtc).IsRequired(false);
        });

        modelBuilder.Entity<UserEvent>(builder =>
        {
            builder.ToTable("user_events");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.EventType).IsRequired().HasMaxLength(50);
            builder.Property(e => e.Payload).IsRequired();
            builder.HasIndex(e => e.UserId);
        });
    }
}

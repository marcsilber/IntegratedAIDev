using AIDev.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DevRequest> DevRequests => Set<DevRequest>();
    public DbSet<RequestComment> RequestComments => Set<RequestComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DevRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.SubmittedBy).IsRequired().HasMaxLength(200);
            entity.Property(e => e.SubmittedByEmail).IsRequired().HasMaxLength(200);
            entity.Property(e => e.GitHubIssueUrl).HasMaxLength(500);
            entity.Property(e => e.RequestType).HasConversion<string>();
            entity.Property(e => e.Priority).HasConversion<string>();
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasMany(e => e.Comments)
                .WithOne(c => c.DevRequest)
                .HasForeignKey(c => c.DevRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequestComment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Author).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Content).IsRequired();
        });
    }
}

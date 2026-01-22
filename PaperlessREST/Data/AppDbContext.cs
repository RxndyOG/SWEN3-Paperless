using Microsoft.EntityFrameworkCore;
using Paperless.REST.Models;
using PaperlessREST.Models;

namespace PaperlessREST.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Document>()
            .HasMany(d => d.Versions)
            .WithOne(v => v.Document)
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Document>()
            .HasOne(d => d.CurrentVersion)
            .WithMany()
            .HasForeignKey(d => d.CurrentVersionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentVersion>()
            .HasOne(v => v.DiffBaseVersion)
            .WithMany()
            .HasForeignKey(v => v.DiffBaseVersionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AccessStatistics>(entity =>
        {
            entity.HasKey(a => a.id);
            entity.HasIndex(a => new { a.documentId, a.accessDate })
            .IsUnique();
            entity.Property(a => a.accessDate)
            .HasColumnType("date");
        });

        modelBuilder.Entity<AccessStatistics>()
            .HasOne<Document>()
            .WithMany()
            .HasForeignKey(a => a.documentId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}
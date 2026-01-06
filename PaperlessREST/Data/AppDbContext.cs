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

        // optional FK on Document -> DocumentVersion
        modelBuilder.Entity<Document>()
            .HasOne(d => d.CurrentVersion)
            .WithMany() // no inverse navigation on DocumentVersion
            .HasForeignKey(d => d.CurrentVersionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<DocumentVersion>()
            .HasOne(v => v.DiffBaseVersion)
            .WithMany()
            .HasForeignKey(v => v.DiffBaseVersionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
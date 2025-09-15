using Microsoft.EntityFrameworkCore;
using PaperlessREST.Models;

namespace PaperlessREST.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Document> Documents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Document>()
                .Property(d => d.CreatedAt)
                .HasDefaultValueSql("NOW()");

            modelBuilder.Entity<Document>()
                .Property(d => d.UpdatedAt)
                .HasDefaultValueSql("NOW()");
        }
    }
}
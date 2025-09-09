using Microsoft.EntityFrameworkCore;
using PaperlessREST.Models;

namespace PaperlessREST.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
}
using Microsoft.EntityFrameworkCore;


namespace FaxMonitor.Data;

public class FaxDbContext : DbContext
{
    public DbSet<Job> Job { get; set; }
    public DbSet<JobEvent> JobEvent { get; set; }

    public FaxDbContext(DbContextOptions<FaxDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>().HasIndex(j => j.ServerJobId);
    }
}

using Microsoft.EntityFrameworkCore;

namespace backend;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ReceivedData>().HasData(
            new DataToDatabase() {Id = "0", Date = DateTime.Now, DeviceId = "0",LinkToPicture = "Http://LinkThatGoesNowhere.com"}
        );

        base.OnModelCreating(builder);
    }

    public DbSet<DataToDatabase> DatabaseData { get; set; }
}
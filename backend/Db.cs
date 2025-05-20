using Microsoft.EntityFrameworkCore;

namespace backend;

public class Db : DbContext
{
    public Db(DbContextOptions<Db> options) : base(options)
    {
        
    }
    
    public DbSet<DataToDatabase> DataToDatabase { get; set; } = null!;
}
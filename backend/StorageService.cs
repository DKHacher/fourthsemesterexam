using backend.Context;
using backend.Entities;
using Microsoft.EntityFrameworkCore;

public class StorageService
{
    private readonly IDbContextFactory<MyDbContext> _dbContextFactory;

    public StorageService(IDbContextFactory<MyDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task StoreMetadataAsync(string deviceId, string publicId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entry = new Storeddatum
        {
            Deviceid = deviceId,
            Date = DateTime.UtcNow,
            Linktopicture = publicId // save publicId for later signed URL generation
        };

        db.Storeddata.Add(entry);
        await db.SaveChangesAsync();
    }
}
using System.Text.Json;
using backend;
using backend.Context;
using backend.Entities;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddDbContextFactory<MyDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PgDbConnection")));
builder.Services.AddControllers();

builder.Services.AddScoped<StorageService>();
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("Cloudinary"));
builder.Services.AddScoped<CloudinaryService>();
builder.Services.AddLogging();
var app = builder.Build();
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant();
    string[] blockedPaths = {
        "/info.php", "/.env", "/.git/config", "/config.json", "/telescope/requests", "/wp/v2/users/"
    };

    if (blockedPaths.Any(p => path.Contains(p)))
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Forbidden");
        return;
    }

    await next();
});
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();


// Wait for the database to be ready
var retries = 6;
var delay = TimeSpan.FromSeconds(5);

for (int i = 0; i < retries; i++)
{
    try
    {
        using (var scope = app.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MyDbContext>>();
            using var db = await dbFactory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
        }
        break; // success
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DB not ready yet ({i + 1}/{retries}): {ex.Message}");
        if (i == retries - 1) throw;
        await Task.Delay(delay);
    }
}

// Setup Client options and instantiate
var options = new HiveMQClientOptionsBuilder().
    WithBroker("mqtt").
    //WithBroker("host.docker.internal"). //this is to test the mqtt functions locally
    WithPort(1883). 
    WithUseTls(false).
    Build();


HiveMQClient client = null;
int maxRetries = 20;
TimeSpan delayBetweenRetries = TimeSpan.FromSeconds(9);
bool connected = false;

for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    try
    {
        client = new HiveMQClient(options);
        var connectResult = await client.ConnectAsync().ConfigureAwait(false);
        connected = true;
        Console.WriteLine($"MQTT connected on attempt {attempt}");
        break;
    }
    catch (HiveMQtt.Client.Exceptions.HiveMQttClientException ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] MQTT connection attempt {attempt} failed: {ex.Message}");

        if (attempt == maxRetries)
        {
            Console.WriteLine("Failed to connect to MQTT after multiple attempts. Exiting.");
            connected = false;
        }
        await Task.Delay(delayBetweenRetries);
    }
}
Dictionary<string, ChunkMeta> chunkMetadataMap = new();
Dictionary<string, SortedDictionary<int, byte[]>> orderedChunkStorage = new();
object chunkLock = new();

if (connected)
{
    client.OnMessageReceived += async (sender, args) =>
    {
        using var scope = app.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();
        var cloudinaryService = scope.ServiceProvider.GetRequiredService<CloudinaryService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        string topic = args.PublishMessage.Topic;
        byte[] payload = args.PublishMessage.Payload;

        if (topic == "topic1/meta")
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(payload);
                string metaDeviceId = json.GetProperty("deviceId").GetString();
                string imageId = json.GetProperty("imageId").GetString();
                int totalChunks = json.GetProperty("totalChunks").GetInt32();

                lock (chunkLock)
                {
                    chunkMetadataMap[metaDeviceId] = new ChunkMeta(imageId, totalChunks);
                }

                logger.LogInformation($"Received metadata: deviceId={metaDeviceId}, imageId={imageId}, totalChunks={totalChunks}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse meta message.");
            }
        }
        else if (topic.StartsWith("topic1/chunk/"))
        {
            var parts = topic.Split('/');
            if (parts.Length >= 5)
            {
                string deviceIdFromTopic = parts[2];
                string imageIdFromTopic = parts[3];
                if (!int.TryParse(parts[4], out int chunkIndex))
                {
                    logger.LogWarning($"Invalid chunk index in topic: {topic}");
                    return;
                }

                lock (chunkLock)
                {
                    if (!orderedChunkStorage.ContainsKey(deviceIdFromTopic))
                        orderedChunkStorage[deviceIdFromTopic] = new SortedDictionary<int, byte[]>();

                    try
                    {
                        string base64 = System.Text.Encoding.UTF8.GetString(payload);
                        byte[] decoded = Convert.FromBase64String(base64);
                        orderedChunkStorage[deviceIdFromTopic][chunkIndex] = decoded;

                        logger.LogInformation($"Received chunk {chunkIndex} for {deviceIdFromTopic} (stored chunks: {orderedChunkStorage[deviceIdFromTopic].Count})");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to decode base64 chunk.");
                    }
                }
            }
            else
            {
                logger.LogWarning($"Unexpected topic format for chunk: {topic}");
            }
        }
        else if (topic.StartsWith("topic1/done/"))
        {
            var parts = topic.Split('/');
            if (parts.Length >= 4)
            {
                string deviceIdFromTopic = parts[2];
                string imageIdFromTopic = parts[3];

                byte[] fullImage = null;

                lock (chunkLock)
                {
                    if (!chunkMetadataMap.TryGetValue(deviceIdFromTopic, out var meta))
                    {
                        logger.LogWarning($"Done received but no metadata found for device {deviceIdFromTopic}");
                        return;
                    }

                    if (!orderedChunkStorage.TryGetValue(deviceIdFromTopic, out var chunkMap))
                    {
                        logger.LogWarning($"Done received but no chunks found for device {deviceIdFromTopic}");
                        return;
                    }

                    if (chunkMap.Count != meta.TotalChunks)
                    {
                        logger.LogWarning($"Chunks incomplete for device {deviceIdFromTopic}: have {chunkMap.Count}, expected {meta.TotalChunks}");
                        return;
                    }

                    // Combine chunks in order
                    fullImage = chunkMap.Values.SelectMany(c => c).ToArray();

                    // Cleanup
                    orderedChunkStorage.Remove(deviceIdFromTopic);
                    chunkMetadataMap.Remove(deviceIdFromTopic);
                }

                if (fullImage != null)
                {
                    try
                    {
                        var filename = $"{deviceIdFromTopic}_{imageIdFromTopic}.jpg";
                        var tempFilePath = Path.Combine(Path.GetTempPath(), filename);

                        await File.WriteAllBytesAsync(tempFilePath, fullImage);

                        var publicId = await cloudinaryService.UploadPrivateImageAsync(
                            new FileStream(tempFilePath, FileMode.Open, FileAccess.Read),
                            filename);

                        File.Delete(tempFilePath);

                        await storageService.StoreMetadataAsync(deviceIdFromTopic, publicId);

                        logger.LogInformation($"Image uploaded and metadata stored: {filename}");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to process complete image.");
                    }
                }
            }
            else
            {
                logger.LogWarning($"Invalid done topic format: {topic}");
            }
        }
        else
        {
            logger.LogWarning($"Unknown topic received: {topic}");
        }
    };
}



    // Configure the subscriptions
    var subscribeOptionsBuilder = new SubscribeOptionsBuilder()
        .WithSubscription(new TopicFilter("topic1/meta", QualityOfService.ExactlyOnceDelivery))
        .WithSubscription(new TopicFilter("topic1/chunk/#", QualityOfService.ExactlyOnceDelivery))
        .WithSubscription(new TopicFilter("topic1/done/#", QualityOfService.ExactlyOnceDelivery));


        
    var subscribeOptions = subscribeOptionsBuilder.Build();
    var subscribeResult = await client.SubscribeAsync(subscribeOptions);
    //commented out method for the backend to test the mqtt connection and the services included in saving images, before i started to chop images up into many pieces when sending them
    /*var imagePath = Path.Combine(AppContext.BaseDirectory, "ImageToTest.jpg");
    Console.WriteLine($"Trying to load image at: {imagePath}");
    Console.WriteLine($"File exists? {File.Exists(imagePath)}");

    if (!File.Exists(imagePath))
    {
        Console.WriteLine($"Image not found at {imagePath}");
    }
    else
    {
        byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
        string base64Image = Convert.ToBase64String(imageBytes);

        var testData = new ReceivedData()
        {
            Id = "0",
            DeviceId = "test-device-001",
            Data = base64Image
        };

        var publishResult = await client.PublishAsync("topic1", JsonSerializer.Serialize(testData));
        Console.WriteLine($"Published test image to MQTT topic. Result: {publishResult.ReasonCode}");
    }*/

app.Run();
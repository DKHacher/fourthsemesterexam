using System.Text.Json;
using backend;
using backend.Context;
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
if (connected)
{
    // Nested dictionary: deviceId -> imageId -> list of chunks
    Dictionary<string, Dictionary<string, List<byte[]>>> chunkStorage = new();
    object chunkLock = new();  // for thread safety

    client.OnMessageReceived += async (sender, args) =>
    {
        using var scope = app.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();
        var cloudinaryService = scope.ServiceProvider.GetRequiredService<CloudinaryService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        string topic = args.PublishMessage.Topic;
        byte[] payload = args.PublishMessage.Payload;

        string deviceId = "esp32-cam-001";

        // Store metadata per device
        Dictionary<string, string> imageIdMap = new();
        Dictionary<string, List<byte[]>> chunkStorage = new();
        object chunkLock = new();

        if (topic == "topic1/meta")
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(payload);
                string metaDeviceId = json.GetProperty("deviceId").GetString();
                string imageId = json.GetProperty("imageId").GetString();

                lock (chunkLock)
                {
                    imageIdMap[metaDeviceId] = imageId;
                }

                logger.LogInformation($"Received metadata: deviceId={metaDeviceId}, imageId={imageId}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse meta message.");
            }
        }
        else if (topic.StartsWith("topic1/chunk/"))
        {
            lock (chunkLock)
            {
                if (!chunkStorage.ContainsKey(deviceId))
                {
                    chunkStorage[deviceId] = new List<byte[]>();
                }
                try
                {
                    string base64 = System.Text.Encoding.UTF8.GetString(payload);
                    byte[] decoded = Convert.FromBase64String(base64);
                    chunkStorage[deviceId].Add(decoded);
                    logger.LogInformation($"Received and decoded chunk for {deviceId}, total: {chunkStorage[deviceId].Count}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to decode base64 chunk.");
                }

                logger.LogInformation($"Received chunk for {deviceId}, total: {chunkStorage[deviceId].Count}");
            }
        }
        else if (topic == "topic1/done")
        {
            byte[] fullImage = null;
            string imageId = null;

            lock (chunkLock)
            {
                if (chunkStorage.TryGetValue(deviceId, out var chunks))
                {
                    fullImage = chunks.SelectMany(c => c).ToArray();
                    chunkStorage.Remove(deviceId);
                }
                else
                {
                    logger.LogWarning($"Received DONE but no chunks for {deviceId}.");
                    return;
                }

                imageIdMap.TryGetValue(deviceId, out imageId);
                imageIdMap.Remove(deviceId);
            }

            if (fullImage != null && imageId != null)
            {
                try
                {
                    var filename = $"{deviceId}_{imageId}.jpg";
                    var tempFilePath = Path.Combine(Path.GetTempPath(), filename);

                    await File.WriteAllBytesAsync(tempFilePath, fullImage);

                    var publicId = await cloudinaryService.UploadPrivateImageAsync(
                        new FileStream(tempFilePath, FileMode.Open, FileAccess.Read),
                        filename);

                    File.Delete(tempFilePath);

                    await storageService.StoreMetadataAsync(deviceId, publicId);

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
            logger.LogWarning($"Unknown topic received: {topic}");
        }
    };
}


    // Configure the subscriptions
    var subscribeOptionsBuilder = new SubscribeOptionsBuilder()
        .WithSubscription(new TopicFilter("topic1/meta", QualityOfService.ExactlyOnceDelivery))
        .WithSubscription(new TopicFilter("topic1/chunk/+/+/+", QualityOfService.ExactlyOnceDelivery))
        .WithSubscription(new TopicFilter("topic1/done/+/+", QualityOfService.ExactlyOnceDelivery));


        
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
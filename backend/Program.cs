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
    //WithBroker("host.docker.internal").
    WithPort(1883). 
    WithUseTls(false).//tls is turned off for testing locally, but i would have to change it to be on and figure out another way to work with it, once i send it to cloud probably
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
    Dictionary<string, List<byte[]>> chunkStorage = new();
    object chunkLock = new();  // for thread safety
    // Setup an application message handlers BEFORE subscribing to a topic
    client.OnMessageReceived += async (sender, args) =>
    {
        using var scope = app.Services.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();
        var cloudinaryService = scope.ServiceProvider.GetRequiredService<CloudinaryService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        string topic = args.PublishMessage.Topic;
        byte[] payload = args.PublishMessage.Payload;
        string deviceId = "esp32-cam-001";  // Update if your ESP sends an ID

        bool isDoneMessage = false;
        byte[] fullImage = null;

        lock (chunkLock)
        {
            if (topic.StartsWith("topic1/chunk/"))
            {
                if (!chunkStorage.ContainsKey(deviceId))
                {
                    chunkStorage[deviceId] = new List<byte[]>();
                }

                chunkStorage[deviceId].Add(payload);
                logger.LogInformation($"Received chunk for {deviceId}, total chunks so far: {chunkStorage[deviceId].Count}");
            }
            else if (topic == "topic1/done")
            {
                if (chunkStorage.TryGetValue(deviceId, out var chunks))
                {
                    fullImage = chunks.SelectMany(c => c).ToArray();
                    chunkStorage.Remove(deviceId);
                    isDoneMessage = true;
                }
                else
                {
                    logger.LogWarning($"Received DONE message but no chunks found for device {deviceId}.");
                }
            }
        }

        // This runs outside the lock
        if (isDoneMessage && fullImage != null)
        {
            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var tempFilePath = Path.Combine(Path.GetTempPath(), $"{deviceId}_{timestamp}.jpg");

                await File.WriteAllBytesAsync(tempFilePath, fullImage);

                var publicId = await cloudinaryService.UploadPrivateImageAsync(
                    new FileStream(tempFilePath, FileMode.Open, FileAccess.Read),
                    $"{deviceId}_{timestamp}.jpg");

                File.Delete(tempFilePath);

                await storageService.StoreMetadataAsync(deviceId, publicId);

                logger.LogInformation($"Image uploaded and metadata stored for {deviceId}.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process complete image.");
            }
        }
    };
}


    // Configure the subscriptions we want and subscribe
    var subscribeOptionsBuilder = new SubscribeOptionsBuilder()
        .WithSubscription(new TopicFilter("topic1/chunk/+", QualityOfService.ExactlyOnceDelivery))
        .WithSubscription(new TopicFilter("topic1/done", QualityOfService.ExactlyOnceDelivery));

        
    var subscribeOptions = subscribeOptionsBuilder.Build();
    var subscribeResult = await client.SubscribeAsync(subscribeOptions);

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
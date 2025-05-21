using System.Text.Json;
using backend;
using backend.Context;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;
using Microsoft.EntityFrameworkCore;



var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("PgDbConnection");
Console.WriteLine($"[DEBUG] Connection string: {connectionString}");
builder.Services.AddDbContextFactory<MyDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PgDbConnection")));
builder.Services.AddControllers();
Console.WriteLine("Connection string: " + builder.Configuration.GetConnectionString("PgDbConnection"));

//var cloudinaryService = new CloudinaryService("", "", "");//replace blanks with github secrets
builder.Services.AddScoped<StorageService>();
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("Cloudinary"));
builder.Services.AddScoped<CloudinaryService>();
builder.Services.AddLogging();
var app = builder.Build();
app.MapControllers();


// Setup Client options and instantiate
var options = new HiveMQClientOptionsBuilder().
    WithBroker("host.docker.internal").
    WithPort(1883). 
    WithUseTls(false).//tls is turned off for testing locally, but i would have to change it to be on and figure out another way to work with it, once i send it to cloud probably
    Build();
var client = new HiveMQClient(options);

var retries = 3;
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

// Setup an application message handlers BEFORE subscribing to a topic
client.OnMessageReceived += async (sender, args) =>
{
    using var scope = app.Services.CreateScope();
    var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();
    var cloudinaryService = scope.ServiceProvider.GetRequiredService<CloudinaryService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var rawPayload = args.PublishMessage.PayloadAsString;
        var data = JsonSerializer.Deserialize<ReceivedData>(rawPayload);

        // Decode base64 image
        byte[] imageBytes = Convert.FromBase64String(data.Data);
        using var imageStream = new MemoryStream(imageBytes);

        // Upload to Cloudinary
        var publicId = await cloudinaryService.UploadPrivateImageAsync(
            imageStream,
            $"{data.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.jpg");

        // Store metadata in DB
        await storageService.StoreMetadataAsync(data.DeviceId, publicId);

        logger.LogInformation($"Image uploaded and metadata stored for device {data.DeviceId}.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to handle incoming MQTT message.");
    }
};


// Connect to the MQTT broker
var connectResult = await client.ConnectAsync().ConfigureAwait(false);

// Configure the subscriptions we want and subscribe
var subscribeOptionsBuilder = new SubscribeOptionsBuilder().WithSubscription(new TopicFilter("topic1", QualityOfService.ExactlyOnceDelivery));
    
var subscribeOptions = subscribeOptionsBuilder.Build();
var subscribeResult = await client.SubscribeAsync(subscribeOptions);

var imagePath = Path.Combine(AppContext.BaseDirectory, "ImageToTest.jpg");
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
}
app.Run();
//maybe inclue an App.Run here once the above parts are not commented out dont know why i need to yet
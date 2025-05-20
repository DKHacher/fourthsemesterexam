using System.Text.Json;
using backend;
using backend.Context;
using backend.Entities;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;
using Microsoft.EntityFrameworkCore;


//most of this code is taken from the HiveMQTT example and used as provided, but they set it up to connect directly to a cloud based broker from their own services,
//this has been changed to a local version for now


//This commented out part is where i add a db context based on the database i think
var builder = WebApplication.CreateBuilder(args);  


builder.Services.AddEntityFrameworkNpgsql().AddDbContext<MyDbContext>(opt =>
{
    opt.UseNpgsql(builder.Configuration.GetConnectionString("PgDbConnection"));
});


var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<string>>();


// Setup Client options and instantiate
var options = new HiveMQClientOptionsBuilder().
    WithBroker("localhost").//seems to work fine for now, but if i want to work on the fullstack aspects of this later, then i might have to convert to a websocket based connection
    WithPort(1883). 
    WithUseTls(false).//tls is turned off for testing locally, but i would have to change it to be on and figure out another way to work with it, once 
    Build();
var client = new HiveMQClient(options);

// Setup an application message handlers BEFORE subscribing to a topic, maybe is should move the process from the subscribe options builder up here
client.OnMessageReceived += (sender, args) =>
{
    Console.WriteLine("Message Received: {}", args.PublishMessage.PayloadAsString);
};

// Connect to the MQTT broker
var connectResult = await client.ConnectAsync().ConfigureAwait(false);

// Configure the subscriptions we want and subscribe
var subscribeOptionsBuilder =
    new SubscribeOptionsBuilder().WithSubscription(new TopicFilter("topic1", QualityOfService.ExactlyOnceDelivery),
        (obj, e) => 
        {
            //will need to tweak this part, as i have two seperate buisness entities for the data to go through, one as a received version, and the version that needs to be sent out
            
            //things to look into, receive data as BE 1 which includes a picture, convert as much to BE2, add timestamp,
            //run process to add picture to filesystem that is to be used, maybe use Google Pictures on my own account
            //then look into adding link to newly saved picture to BE2 before sending it to database and process is done
            
            logger.LogInformation(JsonSerializer.Serialize(e.PublishMessage.PayloadAsString)); //potential logger
            var data = JsonSerializer.Deserialize<ReceivedData>(e.PublishMessage.PayloadAsString); 
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                var transformedData = new Storeddatum(){Id = data.Id, Deviceid = data.DeviceId, Date = DateTime.Now, Linktopicture = "Testlink"};//replace testlink with actual link, and feature to create link
                db.Storeddata.Add(transformedData);
                db.SaveChanges();
                logger.LogInformation("Now the database has the follwing data in the StoredData table: ");
                foreach (var d in db.Storeddata)
                {
                    logger.LogInformation(JsonSerializer.Serialize(d));
                }
            }
        }
    );
    
var subscribeOptions = subscribeOptionsBuilder.Build();
var subscribeResult = await client.SubscribeAsync(subscribeOptions);

// Publish a message
var publishResult = await client.PublishAsync("topic1/example", "Hello Payload");


//maybe inclue an App.Run here once the above parts are not commented out
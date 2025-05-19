using System.Text.Json;
using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;

//most of this code is taken from the HiveMQTT example and used as provided, but they set it up to connect directly to a cloud based broker from their own services,
//this has been changed to a local version for now

// Setup Client options and instantiate
var options = new HiveMQClientOptionsBuilder().
    WithBroker("localhost").//seems to work fine for now, but if i want to work on the fullstack aspects of this later, then i might have to convert to a websocket based connection
    WithPort(1883). 
    WithUseTls(false).//tls is turned off for testing locally, but i would have to change it to be on and figure out another way to work with it, once 
    Build();
var client = new HiveMQClient(options);

// Setup an application message handlers BEFORE subscribing to a topic
client.OnMessageReceived += (sender, args) =>
{
    Console.WriteLine("Message Received: {}", args.PublishMessage.PayloadAsString);
};

// Connect to the MQTT broker
var connectResult = await client.ConnectAsync().ConfigureAwait(false);

// Configure the subscriptions we want and subscribe
var subscribeOptionsBuilder =
    new SubscribeOptionsBuilder().WithSubscription(new TopicFilter("topic1", QualityOfService.ExactlyOnceDelivery),
        (obj, e) => //Currently commented out as i am missing a dbcontext for the database and subsequent functions, Replace TimeseriesData with XXXXXXData
        {
            //logger.LogInformation(JsonSerializer.Serialize(e.PublishMessage.PayloadAsString)); //potential logger
            //var data = JsonSerializer.Deserialize<XXXXXXXData>(e.PublishMessage.PayloadAsString); //Replace XXXXXXXData with a set of rules like Id[key], FileLink/Serialized image, Device Id and Timestamp, Possibly More
            //using (var scope = app.Services.CreateScope())
            //{
            //    var db = scope.ServiceProvider.GetRequiredService<Db>();
            //    db .TimeseriesData.Add(data);
            //    db.SaveChanges();
            //    logger.LogInformation("Now the database has the follwing data in the timeseries table: ");
            //    foreach (var d in db.TimeseriesData)
            //    {
            //        logger.LogInformation(JsonSerializer.Serialize(d));
            //    }
            //}
        }
    );
    
var subscribeOptions = subscribeOptionsBuilder.Build();
var subscribeResult = await client.SubscribeAsync(subscribeOptions);

// Publish a message
var publishResult = await client.PublishAsync("topic1/example", "Hello Payload");
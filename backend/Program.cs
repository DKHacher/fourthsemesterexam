using HiveMQtt.Client;
using HiveMQtt.MQTT5.Types;

// command to setup local docker broker: docker run --name hivemq-ce -d -p 1883:1883 hivemq/hivemq-ce

//most of this code is taken from the HiveMQTT example and used as provided, but they set it up to connect directly to a cloud based broker from their own services

// Setup Client options and instantiate
var options = new HiveMQClientOptionsBuilder().
    WithBroker("localhost").//set to localhost, i might just decide to use one of the free online brokers for this part as i cant seem to get it to work correctly
    //another option is to change this out for a websocket server
    WithPort(1883). //port 8883 default i think for connecting to the free online broker, use port 1883 if using local docker as that is default
    WithUseTls(true).// it seems like this is what is holding me back from continuing further, but i cant remember how to add credentials so it passes
    Build();
var client = new HiveMQClient(options);

//have not touched anything past here, ast i need the above part to work to be able to test these parts

// Setup an application message handlers BEFORE subscribing to a topic
client.OnMessageReceived += (sender, args) =>
{
    Console.WriteLine("Message Received: {}", args.PublishMessage.PayloadAsString);
};

// Connect to the MQTT broker
var connectResult = await client.ConnectAsync().ConfigureAwait(false);

// Configure the subscriptions we want and subscribe
var builder = new SubscribeOptionsBuilder();
builder.WithSubscription("topic1", QualityOfService.AtLeastOnceDelivery)
    .WithSubscription("topic2", QualityOfService.ExactlyOnceDelivery);
var subscribeOptions = builder.Build();
var subscribeResult = await client.SubscribeAsync(subscribeOptions);

// Publish a message
var publishResult = await client.PublishAsync("topic1/example", "Hello Payload");
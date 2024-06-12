using NLog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using MQTTnet;
using MQTTnet.Client;
using System.Text;

namespace GPIOControl;

internal class Mqtt
{
    private MqttClient _client;
    private const string user = "raspi-ar";
    private const string passwd = "LoTTPlaolGiM";
    private const string mqttIp = "192.168.178.26";

    private Dictionary<string, Action<string>> _callbackDir = new();

    private static Logger Log = LogManager.GetCurrentClassLogger();

    public Mqtt()
    {
        _callbackDir.Clear();
        var mqttFactory = new MqttFactory();
        _client = (MqttClient)mqttFactory.CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += HandleMqttApplicationMessageReceived;

        Log.Info($"Mqtt connect client created");
    }

    public async Task Connect_Client_Timeout(string id)
    {
        // This sample creates a simple MQTT client and connects to an invalid broker using a timeout.
        var mqttClientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(mqttIp)
            .WithCredentials(user, passwd)
            .WithClientId(id)
            .Build();
        try
        {
            using (var timeoutToken = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                await _client.ConnectAsync(mqttClientOptions, timeoutToken.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Info("Mqtt client timeout while connecting.");
        }

        Log.Info($"Mqtt client {id} connected: {_client.IsConnected}");
    }

    public async void subscribe(string topic, Action<string> callback)
    {
        _callbackDir.Add(topic, callback);
        var mqttSubscribeOptions = new MqttFactory().CreateSubscribeOptionsBuilder()
            .WithTopicFilter(
                f =>
                {
                    f.WithTopic(topic);
                })
            .Build();

        var respoce = await _client?.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);

        Log.Info($"Subscribe to topic {topic}, result: {respoce}");
    }
    private Task HandleMqttApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
        Log.Debug($"Received MQTT topic {e.ApplicationMessage.Topic} with {payload}");


        if (_callbackDir.TryGetValue(e.ApplicationMessage.Topic, out Action<string> action))
        {
            action.Invoke(payload);
        }
        else
        {
            Log.Error($"No action registerd for MQTT topic {e.ApplicationMessage.Topic}");
        }
        return Task.CompletedTask;
    }

    public async Task publishZ(string value)
    {
        var applicationMessage = new MqttApplicationMessageBuilder()
           .WithTopic("HomeTemp/Z-PumpOn")
           .WithPayload(value)
           .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
           .Build();
        var r = await _client?.PublishAsync(applicationMessage, CancellationToken.None);

        Log.Debug($"Publish Z returns {r.IsSuccess}");
    }

    public async Task publishWw(string value)
    {
        var applicationMessage = new MqttApplicationMessageBuilder()
            .WithTopic("HomeTemp/Siphon")
            .WithPayload(value)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        var r = await _client?.PublishAsync(applicationMessage, CancellationToken.None);
        Log.Debug($"Publish Ww returns {r.IsSuccess}");
    }

    public async Task publishHotFlansch(string value)
    {
        var applicationMessage = new MqttApplicationMessageBuilder()
            .WithTopic("HomeTemp/Flansch")
            .WithPayload(value)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        var r = await _client?.PublishAsync(applicationMessage, CancellationToken.None);
        Log.Debug($"Publish Hot Flansch returns {r.IsSuccess}");
    }

    public async Task publishHeaterPower(string value)
    {
        var applicationMessage = new MqttApplicationMessageBuilder()
            .WithTopic("HomeTemp/HeaterPower")
            .WithPayload(value)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        var r = await _client?.PublishAsync(applicationMessage, CancellationToken.None);
        Log.Debug($"Publish Heater Power {r.IsSuccess}");

    }
}


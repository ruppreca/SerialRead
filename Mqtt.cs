using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


namespace GPIOControl
{
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
            _client = new MqttClient(mqttIp);
            byte r = _client.Connect(Guid.NewGuid().ToString(), user, passwd);
            Log.Info($"Mqtt connect returns {(r == 0 ? "Ok" : "Fail")}, Is connected: {_client.IsConnected}");
        }

        private void ClientMqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            Log.Debug($"MQTT message received: {Encoding.ASCII.GetString(e.Message)}, topic: {e.Topic}");
            _callback(Encoding.ASCII.GetString(e.Message));
        }

        //private void ClientMqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        //{
        //    Log.Debug($"subscribed MQTT message received: {Encoding.ASCII.GetString(e.Message)}, topic: {e.Topic}");

        //    if (_callbackDir.TryGetValue(e.Topic, out Action<string> action))
        //    {
        //        action(Encoding.ASCII.GetString(e.Message));
        //    }
        //    else
        //    {
        //        Log.Error($"No action registerd for MQTT topic {e.Topic}");
        //    }
        //}

        private void ClientMqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            Log.Info($"Subscribe received, Msg.Id:{e.MessageId}, Qos:{e.GrantedQoSLevels}");
        }

        Action<string> _callback;
        public void subscribe(string topic, Action<string> callback)
        {
            _callback = callback;
            if (_client.IsConnected)
            {
                _client.MqttMsgPublishReceived += ClientMqttMsgPublishReceived;
                _client.MqttMsgSubscribed += ClientMqttMsgSubscribed;
                //_client.MqttMsgPublished += ClientMqttMsgPublished;
                //_client.ConnectionClosed += ClientMqttConnectionClosed;

                _client.Subscribe([topic], [MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE]);
            }
        }

        //public void subscribe(string[] topics, Action<string>[] callbacks)
        //{
        //    Log.Info($"Mqtt subscripe start {topics}");
        //    int i = 0;
        //    foreach (string topic in topics)
        //    {
        //        if (callbacks[i] != null)
        //        {
        //            _callbackDir.Add(topic, callbacks[i++]);
        //        }
        //    }

        //    foreach (var item in _callbackDir)
        //    {
        //        Log.Info($"Topic: {item.Key} is calling {item.Value.Method}");
        //    }

        //    if (_client.IsConnected)
        //    {
        //        _client.MqttMsgPublishReceived += ClientMqttMsgPublishReceived;
        //        _client.MqttMsgSubscribed += ClientMqttMsgSubscribed;
        //        //_client.MqttMsgPublished += ClientMqttMsgPublished;
        //        //_client.ConnectionClosed += ClientMqttConnectionClosed;

        //        _client.Subscribe(topics, [MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE]);
        //        Log.Info($"Mqtt subscripe is done");
        //    }
        //}

        public void publishZ(string value)
        {
            var r = _client?.Publish("HomeTemp/Z-PumpOn", Encoding.UTF8.GetBytes(value), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            //Log.Info($"Publish Z returns {r}");
        }

        public void publishWw(string value)
        {
            var r = _client?.Publish("HomeTemp/Siphon", Encoding.UTF8.GetBytes(value), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            //Log.Info($"Publish Ww returns {r}");
        }

        public void publishHotFlansch(string value)
        {
            var r = _client?.Publish("HomeTemp/Flansch", Encoding.UTF8.GetBytes(value), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            //Log.Info($"Publish Ww returns {r}");
        }

        public void publishHeaterPower(string value)
        {
            var r = _client?.Publish("HomeTemp/HeaterPower", Encoding.UTF8.GetBytes(value), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            //Log.Info($"Publish Ww returns {r}");
        }
    }
}

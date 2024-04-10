using NLog;
using System;
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

        private static Logger Log = LogManager.GetCurrentClassLogger();

        public Mqtt()
        {
            _client = new MqttClient(mqttIp);
            byte r = _client.Connect(Guid.NewGuid().ToString(), user, passwd);
            Log.Info($"Mqtt connect returns {(r == 0 ? "Ok" : "Fail")}, Is connected: {_client.IsConnected}");
        }

        private void ClientMqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            Log.Debug($"MQTT message received: {Encoding.ASCII.GetString(e.Message)}, topic: {e.Topic}");
            _callback(Encoding.ASCII.GetString(e.Message));
        }

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

        public void publishZ(string value)
        {
            var r = _client?.Publish("HomeTemp/Z-PumpOn", Encoding.UTF8.GetBytes(value), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            //Log.Info($"Publish Z returns {r}");
        }

        public void publishWw(string value)
        {
            var r = _client?.Publish("HomeTemp/WWtemp", Encoding.UTF8.GetBytes(value), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            //Log.Info($"Publish Ww returns {r}");
        }

        public void publishHotFlansch(string value)
        {
            var r = _client?.Publish("HomeTemp/Flansch", Encoding.UTF8.GetBytes(value), MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            //Log.Info($"Publish Ww returns {r}");
        }
    }
}

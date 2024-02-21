using NLog;
using System;
using System.Net;
using System.Reflection.Metadata;
using System.Text;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


namespace Z_PumpControl_Raspi
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
            var r = _client.Connect(Guid.NewGuid().ToString(), user, passwd);
            Log.Info($"Connect returns {r}");
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
    }
}

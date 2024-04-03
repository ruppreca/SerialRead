using System;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Z_PumpControl_Raspi;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace GPIO_Control.pwmKemo;
internal class KebaBackgroundService
{
    private readonly PeriodicTimer timer;
    private readonly CancellationTokenSource cts = new();
    private Task timerTask;
    private static readonly PwmKemo _pwmKemo = new();
    private static Logger Log = LogManager.GetCurrentClassLogger();

    private static Zpump Pump;
    private static Mqtt Mqtt;

    public KebaBackgroundService(TimeSpan timerInterval)
    {
        timer = new(timerInterval);
    }

    public async Task Start()
    {
        try
        {
            Log.Info("KemoBackgroundService do start up");
            await _pwmKemo.init();
            Pump = new();
            Log.Info("New Pump");
            
            Mqtt = new();
            Mqtt.subscribe("strom/zaehler/SENSOR", ReceivedFromSubcribe);
            Log.Info("New Mqtt and subscribe");

            timerTask = DoWorkAsync();
            Log.Info("KemoBackgroundService started success");
        }
        catch (Exception)
        {
            Log.Error("KemoBackgroundService failed to init");
            throw;
        }
    }

    private async Task DoWorkAsync()
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                double flanschTemp = ReadTemp.Read1WireTemp("28-000000a851b8");  //Flansch am Heizstab
                Log.Info($"Heater flansch temp: {flanschTemp}°C at {DateTime.Now}");
                Mqtt.publishHotFlansch((flanschTemp).ToString());


                //Log.Info(string.Concat("Keba run loop: ", DateTime.Now.ToString("O")));
                //await _pwmKemo.loop();
            }
        }
        catch (OperationCanceledException)
        {

        }
    }

    public async Task StopAsync()
    {
        if (timerTask is null)
        {
            return;
        }

        cts.Cancel();
        await timerTask;
        cts.Dispose();
        Console.WriteLine("WeatherForecastBackgroundService just stopped");
    }

    public void ReceivedFromSubcribe(string message)
    {
        var now = DateTime.Now;
        (DateTime time, int? power) = ParseJsonSML(message);
        if (power.HasValue && power < -3 && time - now < TimeSpan.FromSeconds(10))
        {
            Log.Info($"Time OK, power {power}");
            return;
        }
    }

    private static (DateTime, int?) ParseJsonSML(string message)
    {
        //Parse a Json like: {"Time":"2024-04-03T19:48:01","SML":{"server_id":"0a01484c5902000d762c","total_kwh":4086.4367,"curr_w":210}}
        MqttMessage result = null;
        try
        {
            result = JsonSerializer.Deserialize<MqttMessage>(message);
        }
        catch (Exception e)
        {
            Log.Error($"JsonSerializer.Deserialize throw: {e.Message}");
        }
        if (result == null)
        {
            Log.Error($"JsonSerializer.Deserialize failed");
            return(DateTime.MinValue, null);
        }
        DateTime msgDate = DateTime.MinValue;
        try
        {
            msgDate = DateTime.ParseExact(result.Time, "yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception e)
        {
            Log.Error($"ParseExact throw: {e.Message} from {result.Time}");
            return (DateTime.MinValue, null);
        }
        Log.Info($"Stromzähler Date: {msgDate}, Power(W): {result.SML.curr_w}");
        return (msgDate, result.SML.curr_w);
    }
}

internal class MqttMessage
{
    public string Time { get; set; }
    public SML SML { get; set; }
}

internal class SML
{
    public int curr_w { get; set; }
}

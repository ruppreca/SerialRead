using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GPIO_Control.Zpump;
using NLog;

namespace GPIOControl.PwmKemo;

internal class KebaBackgroundService
{
    private readonly PeriodicTimer timer;
    private readonly CancellationTokenSource cts = new();
    private Task? timerTask;
    private static Logger Log = LogManager.GetCurrentClassLogger();

    private PwmKemo _pwmKemo;
    private Zpump Pump;
    private Mqtt Mqtt;
    private ZpumpBackgroundService _zpumpService;

    private GlobalProps _globalProps;

    public KebaBackgroundService(TimeSpan timerInterval)
    {
        timer = new(timerInterval);
    }

    public async Task Start(GlobalProps globalProps, ZpumpBackgroundService zpumpService)
    {
        _zpumpService = zpumpService;
        _globalProps = new GlobalProps();
        try
        {
            Log.Debug("KemoBackgroundService do start up");
            _pwmKemo = new();
            await _pwmKemo.init(_globalProps);
            Log.Debug("New pwmKemo, init done");

            Mqtt = new();
            Mqtt.subscribe("strom/zaehler/SENSOR", ReceivedFromSubcribe);
            Log.Debug("New Mqtt and subscribe");

            timerTask = DoWorkAsync();
            Log.Info("KemoBackgroundService started success");
        }
        catch (Exception)
        {
            Log.Error("KemoBackgroundService failed to init");
            throw;
        }
    }

    int _maxFlanshTemp = 65;
    int _alarmFlanshTemp = 90;
    
   
    private async Task DoWorkAsync()
    {
        Log.Info("Kemo run DoWorkAsync");
        try
        {
            while (!cts.Token.IsCancellationRequested && await timer.WaitForNextTickAsync(cts.Token))
            {
               
                double flanschTemp = ReadTemp.Read1WireTemp("28-000000a851b8");  //Flansch am Heizstab
                Log.Info($"Heater flansch temp: {flanschTemp}°C at {DateTime.Now}");
                Mqtt.publishHotFlansch((flanschTemp).ToString());

                if (flanschTemp > _maxFlanshTemp)
                {
                    Log.Info($"Heiz Flansch > {_maxFlanshTemp}, run pump for 4 sec");
                    _zpumpService.PumpTask(4);
                }
                if (flanschTemp > _alarmFlanshTemp)
                {
                    Log.Error($"Heiz Flansch ALARRM Temp: {flanschTemp}°C, heater OFF");
                    _globalProps.FlanschHotAlarm = true;
                    _pwmKemo.alarmOff();
                    await Pump.RunForSec(300);
                }

                await _pwmKemo.loop();
                Log.Debug("Keba run loop done");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exception in KemoBackgroundService DoWorkAsync");
            Log.Error($"Ex. message: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        _pwmKemo.HeaterPower = 0;
        if (timerTask is null)
        {
            return;
        }

        cts.Cancel();
        await timerTask;
        cts.Dispose();
        Log.Info("KemoBackgroundService just stopped");
    }

    public void ReceivedFromSubcribe(string message)
    {
        var now = DateTime.Now;
        (DateTime time, int? power) = ParseJsonSML(message);
        if (power.HasValue && time - now < TimeSpan.FromSeconds(10))
        {
            Log.Info($"Time OK, power {power}");
            //if (power > -1000 && power < 16000)
            //{
            //    _pwmKemo.controlPower((int)power);
            //}
            //else
            //{
            //    _pwmKemo.controlPower(0);
            //}
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
        Log.Debug($"Stromzähler Date: {msgDate}, Power(W): {result.SML.curr_w}");
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
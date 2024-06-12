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
    private GpioPins Gpio;
    private Mqtt Mqtt;

    //private const string MqttTopicPower = "strom/zaehler/SENSOR";
    private const string MqttTopicPower = "Iammeter/curr_w";
    private const string MqttTopicBattVolt = "ahoy/schnee/ch1/U_DC";

    private GlobalProps _globalProps;

    public KebaBackgroundService(TimeSpan timerInterval)
    {
        timer = new(timerInterval);
    }

    public async Task Start(GlobalProps globalProps)
    {
        _globalProps = new GlobalProps();
        try
        {
            Mqtt = new();
            await Mqtt.Connect_Client_Timeout("Keba");
            Mqtt.subscribe(MqttTopicPower, ReceivedFromSubcribeIammeter);  // change Topic and method !!
            Mqtt.subscribe(MqttTopicBattVolt, ReceivedFromSubcribeBattVolt);
            Log.Info("Mqtt Keba subscripe done");

            Gpio = new();
            Log.Debug("New Gpio");

            Log.Debug("KemoBackgroundService do start up");
            _pwmKemo = new(Gpio);
            await _pwmKemo.init(_globalProps);
            Log.Debug("New pwmKemo, init done");

            timerTask = DoWorkAsync();
            Log.Info("KemoBackgroundService started success");
        }
        catch (Exception e)
        {
            Log.Error($"KemoBackgroundService failed to init: {e.Message}");
            throw;
        }
    }

    int _maxFlanshTemp = 85;
    int _alarmFlanshTemp = 90;
    
    private async Task DoWorkAsync()
    {
        Log.Debug("Kemo run DoWorkAsync");
        try
        {
            while (!cts.Token.IsCancellationRequested && await timer.WaitForNextTickAsync(cts.Token))
            {
               
                double flanschTemp = ReadTemp.Read1WireTemp("28-000000a851b8");  //Flansch am Heizstab
                Log.Debug($"Heater flansch temp: {flanschTemp}°C at {DateTime.Now}");
                await Mqtt.publishHotFlansch((flanschTemp).ToString());

                if (flanschTemp > _maxFlanshTemp)
                {
                    Log.Info($"Heiz Flansch > {_maxFlanshTemp}, run pump for 6 sec");
                    await Gpio.RunForSec(6);
                }
                if (flanschTemp > _alarmFlanshTemp)
                {
                    Log.Error($"Heiz Flansch ALARRM Temp: {flanschTemp}°C, heater OFF");
                    _globalProps.FlanschHotAlarm = true;
                    _pwmKemo.alarmOff();
                    await Gpio.RunForSec(300);
                }
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

    private int _lastPower = 0;
    public async void ReceivedFromSubcribeSML(string message)
    {
        var now = DateTime.Now;
        int heaterpower;

        Log.Info($"Message from Iammeter: {message}");


        (DateTime time, int? power) = ParseJsonSML(message);
        if (power.HasValue && time - now < TimeSpan.FromSeconds(10))
        {
            Log.Debug($"Time OK, power {power}");
            if (power > -1000 && power < 16000)  // only if power changed
            {
                if(_lastPower != power)
                {
                    heaterpower = _pwmKemo.controlPower((int)power);
                    _lastPower = (int)power;
                    await Mqtt.publishHeaterPower(heaterpower.ToString());
                }        
            }
            else
            {
                Log.Error($"Inplausable power from Zähler: {power}");
                heaterpower = _pwmKemo.controlPower(2000); // power 2000 Watt will stop Heater
                await Mqtt.publishHeaterPower(heaterpower.ToString());
            }
        }
        
    }

    public async void ReceivedFromSubcribeIammeter(string message)
    {
        int heaterpower;
        int Hm600power;   // max power setting for Hoymiles HM-600 inverter

        Log.Debug($"Message from Iammeter: {message}");
        int? power = int.Parse(message);
        if (power.HasValue)
        {
            Log.Debug($"Iammeter power {power}");
            if (power > -1000 && power < 16000)  // only if power changed
            {
                if (_lastPower != power)
                {
                    // AR 240609 disabled heater power
                    //heaterpower = _pwmKemo.controlPower((int)power);
                    //Mqtt.publishHeaterPower(heaterpower.ToString());

                    //ar240609 add "nulleinspeistung" für hm-600
                    Hm600power = await _pwmKemo.limitHm600Power((int)power);

                    _lastPower = (int)power;
                }
            }
            else
            {
                Log.Error($"Inplausable power from Zähler: {power}");
                heaterpower = _pwmKemo.controlPower(2000); // power 2000 Watt will stop Heater
                await Mqtt.publishHeaterPower(heaterpower.ToString());
            }
        }
    }

    public void ReceivedFromSubcribeBattVolt(string message)
    {
        double? u_Dc = double.Parse(message);
        if (u_Dc.HasValue)
        {
            Log.Info($"Message from Batt Volt: {message}");
            if(u_Dc < 48.0)
                _globalProps.BatterieVoltageLow = true;
            if(u_Dc > 48.5)
                _globalProps.BatterieVoltageLow = false;
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
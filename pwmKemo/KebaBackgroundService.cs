using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Z_PumpControl_Raspi;

namespace GPIO_Control.pwmKemo;
internal class KebaBackgroundService
{
    private readonly PeriodicTimer timer;
    private readonly CancellationTokenSource cts = new();
    private Task? timerTask;
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
            Mqtt.subscribe("strom/zaehler/SENSOR");
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
}

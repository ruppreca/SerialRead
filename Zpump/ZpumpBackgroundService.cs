using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GPIOControl;
using NLog;

namespace GPIO_Control.Zpump;

internal class ZpumpBackgroundService
{
    private readonly PeriodicTimer timer;
    private readonly CancellationTokenSource cts = new();
    private Task? timerTask;
    private static Logger Log = LogManager.GetCurrentClassLogger();

    private GpioPins Pump;
    private Mqtt Mqtt;

    private const int PumpTime = 240; //Z-Pump on time in s

    public ZpumpBackgroundService(TimeSpan timerInterval)
    {
        timer = new(timerInterval);
    }

    public async Task Start()
    {
        try
        {
            Log.Debug("ZpumpBackgroundService do start up");
            Mqtt = new();
            await Mqtt.Connect_Client_Timeout("Zpump");
            Pump = new(Mqtt);
            Log.Debug("New Pump and mqtt created");
           
            Log.Debug("New Mqtt Zpump client");
            timerTask = DoWorkAsync();
            Log.Info("ZpumpBackgroundService started success");
        }
        catch (Exception)
        {
            Log.Error("ZpumpBackgroundService failed to init");
            throw;
        }



        //// Test extra Pump runtime
        //var Start = DateTime.Now;
        //Log.Info($"Test 1 Start Pump for {20}sec at {DateTime.Now}");
        //Pump.RunForSec(20);
        //await Task.Delay(10000); // 10ces
        //Log.Info($"Test 2 Start Pump for {20}sec at {DateTime.Now}");
        //await Pump.RunForSec(20);
        //Log.Info($"Total Pump Time is { (DateTime.Now - Start).TotalSeconds }");
    }

    private const double mindiff = 1;
    private const int lowerTlimit = 35;

    private async Task DoWorkAsync()
    {
        bool isBelow35 = false;
        bool isPause = false;
        double lastTemp1 = 0;
        double lastTemp2 = 0;
        Log.Info("Zpump run DoWorkAsync");
        try
        {
            while (!cts.Token.IsCancellationRequested && await timer.WaitForNextTickAsync(cts.Token))
            {
                var startime = DateTime.Now;
                var pauseStart = DateTime.Parse("00:05:00");

                if (startime > pauseStart && startime < pauseStart + TimeSpan.FromHours(6))
                {
                    if (!isPause)
                    {
                        Log.Info($"Pausing at {startime}");
                        isPause = true;
                    }
                    continue;
                }
                else if (isPause)
                {
                    Log.Info($"Pause ends at {startime}");
                    isPause = false;
                }

                double ww = ReadTemp.Read1WireTemp("28-000000a84439");
                Log.Debug($"WW temp: {ww:0.00} degC, took) {(DateTime.Now - startime).TotalMilliseconds}ms");
                await Mqtt.publishWw(ww.ToString());
                if (cts.Token.IsCancellationRequested) break;

                if (lastTemp1 > 0 && lastTemp2 > 0 && lastTemp2 < lowerTlimit)  // only check if water is below limit
                {
                    Log.Debug($"Temp {ww:0.00}, lastTemp {lastTemp1:0.00}, diff: {ww - lastTemp1:0.00}");
                    if (ww > lastTemp1 + mindiff && lastTemp1 > lastTemp2 + mindiff)
                    {
                        Log.Info($"WW temp: {ww:0.00} degC, diff above {mindiff} to last {ww - lastTemp1:0.00} and prelast {lastTemp1 - lastTemp2:0.00}");

                        Log.Info($"Start Pump for {PumpTime}sec");
                        await Pump.RunForSec(PumpTime);
                        if (cts.Token.IsCancellationRequested) break;
                        Log.Info($"Stopped Pump \n");

                        // reset temp values after run time
                        ww = (double)ReadTemp.ReadWWtemp() / 1000;
                        lastTemp1 = ww;
                    }
                }

                if (!isBelow35 && ww < lowerTlimit)
                {
                    Log.Info($"Now below {lowerTlimit}: {ww:0.00}");
                    isBelow35 = true;
                }
                else if (isBelow35 && lastTemp2 > 35)
                {
                    Log.Info($"Now above {lowerTlimit}: {ww:0.00}");
                    isBelow35 = false;
                }
                lastTemp2 = lastTemp1;
                lastTemp1 = ww;
                Log.Debug("Zpump run loop done");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exception in ZpumpBackgroundService DoWorkAsync");
            Log.Error($"Ex. message: {ex.Message}");
            throw;
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
        Log.Info("ZpumpBackgroundService just stopped");
    }
}
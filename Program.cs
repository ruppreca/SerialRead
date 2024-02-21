using NLog;
using System;
using System.Threading.Tasks;

namespace Z_PumpControl_Raspi;

class Program
{

    const int Sleeptime = 5000; // wait 5sek für next loop
    const int PumpTime = 30; //Z-Pump on time in s
    private static Logger Log = LogManager.GetCurrentClassLogger();
    private static Zpump Pump = new();
    private static Mqtt Mqtt = new();

    private const double mindiff = 0.9;   // soll 0.6 ?

    static async Task Main(string[] args)
    {
        Log.Info("Startup Z-PumpControl");
        await Pump.Off();

        bool quit = false;
        Console.CancelKeyPress += (s, e) =>
        {
            Log.Info("Ending Z-PumpControl by CancelKey");
            quit = true;
        };

        bool isBelow35 = false;
        bool isPause = false;
        double lastTemp1 = 0;
        double lastTemp2 = 0;
        try
        {
            while (true)
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
                    await Task.Delay(5000);
                    continue;
                }
                else if (isPause)
                {
                    Log.Info($"Pause ends at {startime}");
                    isPause = false;
                }

                double ww = (double)Temp.ReadWWtemp() / 1000;
                //Log.Info($"WW temp: {ww:0.00} degC, took) {(DateTime.Now - startime).TotalMilliseconds}ms");
                Mqtt.publishWw((ww).ToString());
                if (quit) break;

                if (lastTemp1 > 0 && lastTemp2 > 0 && lastTemp2 < 35)  // only check if water is below limit
                {
                    //Log.Info($"lastTemp {lastTemp:0.00}, diff: {ww - lastTemp:0.00}");
                    if (ww > lastTemp1 + mindiff && lastTemp1 > lastTemp2 + mindiff)
                    {
                        Log.Info($"WW temp: {ww:0.00} degC, diff above {mindiff} to last {ww - lastTemp1:0.00} and prelast {lastTemp1 - lastTemp2:0.00}");

                        Log.Info($"Start Pump for {PumpTime}sec");
                        var pump = Task.Run(() => Pump.RunForSec(PumpTime));
                        while (!pump.IsCompleted)
                        {
                            if (quit) break;
                            await Task.Delay(1000);
                        }
                        Log.Info($"Stopped Pump \n");

                        // reset temp values after run time
                        ww = (double)Temp.ReadWWtemp() / 1000;
                        lastTemp1 = ww;
                    }
                }

                if (!isBelow35 && ww < 35)
                {
                    Log.Info($"Now below 35 {ww:0.00}");
                    isBelow35 = true;
                }
                else if (isBelow35 && lastTemp2 > 35)
                {
                    Log.Info($"Now above 35 {ww:0.00}");
                    isBelow35 = false;
                }
                lastTemp2 = lastTemp1;
                lastTemp1 = ww;

                //Log.Info("Start sleeping");
                var sleep = Task.Run(() => Task.Delay(Sleeptime));
                while (!sleep.IsCompleted)
                {
                    if (quit) break;
                    await Task.Delay(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Info("Exeption in Z-Pump while loop");
            Log.Error($"{ex.Message}");
        }
        finally
        {
            Log.Info("Ending Z-PumpControl");
        }
    }


}

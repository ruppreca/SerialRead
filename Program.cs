using GPIO_Control.pwmKemo;
using NLog;
using System;
using System.Threading.Tasks;



namespace GPIO_Control;

class Program
{
    private static Logger Log = LogManager.GetCurrentClassLogger();


    static async Task Main(string[] args)
    {
        Log.Info($"Look up 2.0: {PowerToPwmLookup.LookUp(2)}");
        Log.Info($"Look up 40.0: {PowerToPwmLookup.LookUp(40)}");
        Log.Info($"Look up 216.0: {PowerToPwmLookup.LookUp(216)}");
        Log.Info($"Look up 315.0: {PowerToPwmLookup.LookUp(315)}");

        Log.Info($"Look up 216.5: {PowerToPwmLookup.LookUp(216)}");

        KebaBackgroundService kemoBackgroundService = new(TimeSpan.FromSeconds(10));
        await kemoBackgroundService.Start() ;
    }

}

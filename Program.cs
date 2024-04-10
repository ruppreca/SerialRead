using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;
using GPIOControl.PwmKemo;

namespace GPIOControl;

class Program
{
    private static Logger Log = LogManager.GetCurrentClassLogger();
    private static KebaBackgroundService kebaBackgroundService;
    private static readonly CancellationTokenSource cts = new();
    public  static GlobalProps _globalProps = new();

    static async Task Main(string[] args)
    {
        Log.Info("Startup Gpio-Control main");
        kebaBackgroundService = new(TimeSpan.FromSeconds(5));
        await kebaBackgroundService.Start(_globalProps);

        System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += ctx =>
        {
            Log.Info("Main Unloading was called");
            Task stopTask = kebaBackgroundService.StopAsync();
            stopTask.Wait();
            cts.Cancel();
        };

        while (!cts.Token.IsCancellationRequested)
        {
            Log.Debug("Main still running");
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
        }
        cts.Dispose();  
    }
}


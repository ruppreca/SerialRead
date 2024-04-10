using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GPIOControl;

class Program
{
    private static Logger Log = LogManager.GetCurrentClassLogger();
    private static KebaBackgroundService kebaBackgroundService;
    private static readonly CancellationTokenSource cts = new();

    static async Task Main(string[] args)
    {
        Log.Info("Startup Gpio-Control main");
        kebaBackgroundService = new(TimeSpan.FromSeconds(5));
        await kebaBackgroundService.Start();

        System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += ctx =>
        {
            Log.Info("Main Unloading was called");
            Task stopTask = kebaBackgroundService.StopAsync();
            stopTask.Wait();
            cts.Cancel();
        };

        while (!cts.Token.IsCancellationRequested)
        {
            Log.Info("Main still running");
            await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
        }
        cts.Dispose();  
    }


    internal class KebaBackgroundService
    {
        private readonly PeriodicTimer timer;
        private readonly CancellationTokenSource cts = new();
        private Task? timerTask;
        //private static readonly PwmKemo _pwmKemo = new();

        public KebaBackgroundService(TimeSpan timerInterval)
        {
            timer = new(timerInterval);
        }

        public async Task Start()
        {
            //await _pwmKemo.init();
            timerTask = DoWorkAsync();
            Log.Info("KemoBackgroundService started success");
        }

        private async Task DoWorkAsync()
        {
            try
            {
                while (!cts.Token.IsCancellationRequested && await timer.WaitForNextTickAsync(cts.Token))
                {
                    Log.Info($"Kemo run loop at: {DateTime.Now.ToString("O")}");
                    //await _pwmKemo.loop();
                    await Task.Delay(500);
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
            Log.Info("KemoBackgroundService just stopped");
        }
    }
}


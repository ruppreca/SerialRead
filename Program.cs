using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;
using Z_PumpControl_Raspi;


namespace GPIO_Control;

class Program
{
    private static Logger Log = LogManager.GetCurrentClassLogger();




    static async Task Main(string[] args)
    {
        KebaBackgroundService kebaBackgroundService = new(TimeSpan.FromSeconds(10));
        await kebaBackgroundService.Start() ;
    }


    internal class KebaBackgroundService
    {
        private readonly PeriodicTimer timer;
        private readonly CancellationTokenSource cts = new();
        private Task? timerTask;
        private static readonly PwmKeba _pwmKeba = new();

        public KebaBackgroundService(TimeSpan timerInterval)
        {
            timer = new(timerInterval);
        }

        public async Task Start()
        {
            Log.Info("KebaBackgroundService do start up");
            await _pwmKeba.init();
            timerTask = DoWorkAsync();
            Log.Info("KebaBackgroundService started success");
        }

        private async Task DoWorkAsync()
        {
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    Log.Info(string.Concat("Keba run loop: ", DateTime.Now.ToString("O")));
                    await _pwmKeba.loop();
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
}

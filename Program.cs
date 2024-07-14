using NLog;
using SerialRead.Serial.VE.Direct;
using System;
using System.Threading;
using System.Threading.Tasks;


namespace SerialRead;

class Program
{
    private static Logger Log = LogManager.GetCurrentClassLogger();
    private static SerialService serialService;
    private static readonly CancellationTokenSource cts = new();
    public  static GlobalProps _globalProps = new();

    static async Task Main(string[] args)
    {
        Log.Info("\n\nStartup SerialRead main");

        try
        {
            serialService = new(TimeSpan.FromSeconds(10));
            Task serial = serialService.Startup(_globalProps, cts);
            await serial;

            Log.Info($"Main ends. cts was canceled? : serialService task status: {serial.Status}");
            cts.Dispose();
        }
        catch (Exception e)
        {
            Log.Error($"Main ends with exception: {e.Message}");
            throw;
        }

        System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += ctx =>
        {
            Log.Info("Main Unloading was called");
            Task stopTask = serialService.StopAsync();
            stopTask.Wait();
            Thread.Sleep(300);
            cts.Cancel();
        };
    }
}


using NLog;
using System;
using System.IO;


namespace Z_PumpControl_Raspi;

internal static class Temp
{
    private const string OneWireIdT3 = @"/sys/bus/w1/devices/28-000000a85f2b/temperature";
    private static Logger Log = LogManager.GetCurrentClassLogger();

    public static int ReadWWtemp()
    {
        try
        {
            var data = File.ReadAllText(OneWireIdT3);
            if (int.TryParse(data, out var value))
            {
                return value;
            }
            Log.Error($"Failed to parse to int: {data}");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Info($"Failed to open file {OneWireIdT3}");
            Log.Error($"{ex.Message}");
        }
        return 0;
    }
}

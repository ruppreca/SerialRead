using NLog;
using System;
using System.IO;

namespace GPIOControl;

internal static class ReadTemp
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
            return -155;
        }
        catch (Exception ex)
        {
            Log.Info($"Failed to open file {OneWireIdT3}");
            Log.Error($"{ex.Message}");
        }
        return -155;
    }

    public static double Read1WireTemp(string id)
    {
        var path = @$"/sys/bus/w1/devices/{id}/temperature";
        try
        {
            var data = File.ReadAllText(path);
            if (int.TryParse(data, out var value))
            {
                return value/1000;
            }
            Log.Error($"Failed to parse to int: {data}");
            return -155.0;
        }
        catch (Exception ex)
        {
            Log.Info($"Failed to open file {path}");
            Log.Error($"{ex.Message}");
        }
        return -155.0;
    }
}

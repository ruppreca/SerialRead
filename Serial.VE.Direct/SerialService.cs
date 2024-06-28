using GPIOControl.PwmKemo;
using GPIOControl;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using System.Runtime.ConstrainedExecution;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System.Diagnostics;
using System.Timers;
using System.Reactive;

namespace GPIO_Control.Serial.VE.Direct;

internal class SerialService
{
    private readonly PeriodicTimer _timer;
    private Task timerTask;
    private static readonly CancellationTokenSource cts = new();
    private static Logger Log = LogManager.GetCurrentClassLogger();
    private static CancellationTokenSource s_cts;

    private const string mppt_1 = "/dev/ttyUSB3";
    private const string mppt_2 = "/dev/ttyUSB2";
    private const string Serial_1 = "HQ222362TRN";  // West
    private const string Serial_2 = "HQ22236MWWE";  // Ost

    private static readonly string Token = "LbpWnklKJjhgRecvsJiMzImm016Ycze98_55R2aUcjiuN-L4waCeHKr2fUAGXo9dTIgqd0h1kGaaUrd9vBsUdw==";
    private static readonly string Org = "ArHome";
    private static readonly string Bucket = "Batterie";

    //FileStream _stream_1;
    //StreamReader _dev_1;
    //FileStream _stream_2;
    //StreamReader _dev_2;
    MpptData _west;
    MpptData _ost;
    InfluxDBClient _client;

    public SerialService(TimeSpan timerInterval)
    {
        _west = new() { Ser = Serial_1, Name = "West"};
        _ost = new() { Ser = Serial_2, Name = "Ost" };

        _timer = new(timerInterval);
    }
    public async Task Startup(GlobalProps globalProps)
    {
        try
        {
            string output = "stty -F /dev/ttyUSB2 19200 cs8 -cstopb -parenb".Bash();
            Log.Info($"Bash: {output}");
            output = "stty -F /dev/ttyUSB3 19200 cs8 -cstopb -parenb".Bash();
            Log.Info($"Bash: {output}");

            _client = new InfluxDBClient("http://192.168.178.26:8086", Token);
            timerTask = DoWorkAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"SerialService failed to open devices: {ex.Message}");
            throw;
        }
        Log.Info("SerialService started success");
    }

    private async Task DoWorkAsync()
    {
        //Log.Info("SerialService run DoWorkAsync");
        string lineOfText;
        try
        {
            while (!cts.Token.IsCancellationRequested && await _timer.WaitForNextTickAsync(cts.Token))
            {
                try
                {
                    //TODO read both devices in parallel
                    s_cts = new CancellationTokenSource();
                    bool writeToDb = true;
                    s_cts.CancelAfter(3000);
                    try
                    {
                        
                        //FileStream _stream_2 = new FileStream(mppt_2, FileMode.Open, FileAccess.Read);
                        //_dev_2 = new StreamReader(_stream_2, Encoding.UTF8, true, 128);
                        using (var stream_1 = new FileStream(mppt_1, FileMode.Open, FileAccess.Read))
                        {
                            using (var dev_1 = new StreamReader(stream_1, Encoding.UTF8, true, 128))
                            {
                                lineOfText = await CollectDataLines(dev_1, s_cts);
                                if (!CheckMpttReadout(lineOfText, _west))
                                {
                                    Log.Error($"SerialService failed interpert data dev {_west.Name}: {lineOfText}");
                                    writeToDb = false;
                                }
                                Log.Info($"Mptt {_west.Name}: Vbatt {_west.Vbatt_V}, Ibatt {_west.Ibatt_A}A, Power {_west.PowerPV_W}W, State: {_west.State}, Load {_west.LoadOn}");  }
                        }
                        using (var stream_1 = new FileStream(mppt_2, FileMode.Open, FileAccess.Read))
                        {
                            using (var dev_1 = new StreamReader(stream_1, Encoding.UTF8, true, 128))
                            {
                                if (cts.IsCancellationRequested) return;
                                lineOfText = await CollectDataLines(dev_1, s_cts);
                                if (!CheckMpttReadout(lineOfText, _ost))
                                {
                                    Log.Error($"SerialService failed interpert data dev {_ost.Name}: {lineOfText}");
                                    writeToDb = false;
                                }
                                Log.Info($"Mptt {_ost.Name}: Vbatt {_ost.Vbatt_V}, Ibatt {_ost.Ibatt_A}A, Power {_ost.PowerPV_W}W, State: {_ost.State}, Load {_ost.LoadOn}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Error($"\nSerialService Tasks cancelled: timed out.\n");
                        return;
                    }
                    finally
                    {
                        s_cts.Dispose();
                    }

                    if (cts.IsCancellationRequested) return;
                    if (writeToDb)
                    {
                        try //  write to Influx DB
                        {
                            using (var writeApi = _client.GetWriteApi())
                            {
                                // Write by Point
                                var point = PointData.Measurement("batterie")
                                    //.Tag("location", "west")
                                    .Field("Vbatt_V", _ost.Vbatt_V)
                                    .Field("Ibatt_A", (_ost.Ibatt_A + _west.Ibatt_A))
                                    .Field("WestPV_W", _west.PowerPV_W)
                                    .Field("OstPV_W", _ost.PowerPV_W)
                                    .Timestamp(DateTime.UtcNow, WritePrecision.S);

                                writeApi.WritePoint(point, Bucket, Org);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"SerialService Influx fail: {ex.Message}");
                        }
                        //Log.Info("SerialService done Write DB");
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"SerialService failed and ends: {e.Message}");
                    Log.Error(e);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exception in SerialService DoWorkAsync");
            Log.Error($"Ex. message: {ex.Message}");
            throw;
        }
        Log.Info("SerialService Timertask ends");
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

    private static async Task<string> CollectDataLines(StreamReader dev, CancellationTokenSource cts)
    {
        // nach eineger Zeit läuft der ReadLineAsync für immer, braucht timeout und dann neustart des service
        string result;
        string line;
        do
        {
            line = await dev.ReadLineAsync(cts.Token);
        }
        while (!line.StartsWith("PID"));
        result = line + ';';
        do
        {
            line = await dev.ReadLineAsync(cts.Token);
            if (line.Length < 2) continue;
            result += line + ';';
        }
        while (!line.StartsWith("Checksum"));
        return result;
    }

    private static bool CheckMpttReadout(string data, MpptData mppt)
    {
        //byte[] array = Encoding.ASCII.GetBytes(data);
        byte check = 0;
        //for (int i = 0; i < data.Length; i++)
        //{
        //    check += array[i];
        //}
        //Log.Info($"Byte array size: {array.Length}, string: {data.Length}, Checksum: {check}");

        string[] parts = data.Split(';');
        foreach (var item in parts)
        {
            if (item.Length < 2) continue;
            string[] pair = item.Split('\t');
            if (pair.Length != 2) continue;
            switch (pair[0])
            {
                case "Checksum":
                    if (pair[1].Length != 1)
                    {
                        Log.Error($"SerialService Checksum unexpected dev {mppt.Name}: {pair[1]}");
                        return false;
                    }
                    else
                    {
                        byte[] checksum = Encoding.ASCII.GetBytes(pair[1]);
                        if (checksum.Length > 0 && checksum[0] == check)
                        {
                            Log.Info($"Checksum is OK");
                        }
                    }
                    break;
                case "SER#":
                    if (pair[1] != mppt.Ser)
                    {
                        Log.Error($"SerialService Ser# unexpected dev {mppt.Name}: {pair[1]}");
                        return false;
                    }
                    break;
                case "V":
                    if (int.TryParse(pair[1], out int result))
                    {
                        if(result > 10000)
                        {
                            mppt.Vbatt_V = result / 1000.0;
                            break;
                        }
                    }
                    return false;
                case "I":
                    if (int.TryParse(pair[1], out result))
                    {
                        mppt.Ibatt_A = result / 1000.0;
                    }
                    else return false;
                    break;
                case "PPV":
                    if (int.TryParse(pair[1], out result))
                    {
                        mppt.PowerPV_W = result;
                    }
                    else return false;
                    break;
                case "CS":
                    if (int.TryParse(pair[1], out result))
                    {
                        switch (result)
                        {
                            case 0:
                                mppt.State = "Off";
                                break;
                            case 2:
                                mppt.State = "Fault";
                                break;
                            case 3:
                                mppt.State = "Bulk";
                                break;
                            case 4:
                                mppt.State = "Absorption";
                                break;
                            case 5:
                                mppt.State = "Float";
                                break;
                            default:
                                mppt.State = "Unknown";
                                break;
                        }
                    }
                    else return false;
                    break;
                case "ERR":
                    if (!mppt.HasError && pair[1] != "0")
                    {
                        mppt.HasError = true;
                        mppt.Err = pair[1];
                        Log.Error($"SerialService New error for mppt {mppt.Name}, Ser#: {mppt.Ser}, Err: {pair[1]}");
                    }
                    if (mppt.HasError && pair[1] == "0")
                    {
                        mppt.HasError = false;
                        mppt.Err = pair[1];
                        Log.Error($"SerialService Removed error for mppt {mppt.Name}, Ser#: {mppt.Ser}");
                    }
                    break;
                case "LOAD":
                    if (pair[1].Trim() == "ON")
                    {
                        mppt.LoadOn = true;
                    }
                    else if (pair[1].Trim() == "OFF")
                    {
                        mppt.LoadOn = false;
                    }
                    else
                    {
                        Log.Error($"SerialService Unexpected string for LOAD at device {mppt.Name}: {pair[1]}");
                        return false;
                    }
                    break;
                default:
                    break;
            }
        }
        return true;
    }
}


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

namespace GPIO_Control.Serial.VE.Direct;

internal class SerialService
{
    private readonly PeriodicTimer timer;
    private readonly CancellationTokenSource cts = new();
    private Task? timerTask;
    private static Logger Log = LogManager.GetCurrentClassLogger();

    private const string mppt_1 = "/dev/ttyUSB3";
    private const string mppt_2 = "/dev/ttyUSB2";
    private const string Serial_1 = "HQ222362TRN";  // West
    private const string Serial_2 = "HQ22236MWWE";  // Ost

    private static readonly string Token = "LbpWnklKJjhgRecvsJiMzImm016Ycze98_55R2aUcjiuN-L4waCeHKr2fUAGXo9dTIgqd0h1kGaaUrd9vBsUdw==";
    private static readonly string Org = "ArHome";
    private static readonly string Bucket = "Batterie";

    FileStream _stream_1;
    StreamReader _dev_1;
    FileStream _stream_2;
    StreamReader _dev_2;
    MpptData _west;
    MpptData _ost;
    InfluxDBClient _client;

    public SerialService()
    {
        _west = new() { Ser = Serial_1, Name = "West"};
        _ost = new() { Ser = Serial_2, Name = "Ost" };

        string output = "stty -F /dev/ttyUSB2 19200 cs8 -cstopb -parenb".Bash();
        Log.Info($"Bash: {output}");
        output = "stty -F /dev/ttyUSB3 19200 cs8 -cstopb -parenb".Bash();
        Log.Info($"Bash: {output}");

        try
        {
            FileStream _stream_1 = new FileStream(mppt_1, FileMode.Open, FileAccess.Read);
            _dev_1 = new StreamReader(_stream_1, Encoding.UTF8, true, 128);
            FileStream _stream_2 = new FileStream(mppt_2, FileMode.Open, FileAccess.Read);
            _dev_2 = new StreamReader(_stream_2, Encoding.UTF8, true, 128);

            _client = new InfluxDBClient("http://192.168.178.26:8086", Token);
        }
        catch (Exception ex)
        {
            Log.Error($"SerialService failed to open devices: {ex.Message}");
            throw;
        }
    }
    public async Task Startup(CancellationTokenSource cts, GlobalProps globalProps)
    {
        Log.Info("SerialService started");
        try
        {
            string lineOfText;
            while (!cts.IsCancellationRequested)
            {
                //TODO read both devices in parallel and use timeout

                bool writeToDb = true;
                lineOfText = await CollectDataLines(_dev_1, cts);
                if (!CheckMpttReadout(lineOfText, _west))
                {
                    Log.Error($"SerialService failed interpert data dev {_west.Name}: {lineOfText}");
                    writeToDb = false;
                }
                Log.Info($"Mptt {_west.Name}: Vbatt {_west.Vbatt_V}, Ibatt {_west.Ibatt_A}A, Power {_west.PowerPV_W}W, Load {_west.LoadOn}");

                if (cts.IsCancellationRequested) break;
                lineOfText = await CollectDataLines(_dev_2, cts);
                if (!CheckMpttReadout(lineOfText, _ost))
                {
                    Log.Error($"SerialService failed interpert data dev {_ost.Name}: {lineOfText}");
                    writeToDb = false;
                }
                Log.Info($"Mptt {_ost.Name}: Vbatt {_ost.Vbatt_V}, Ibatt {_ost.Ibatt_A}A, Power {_ost.PowerPV_W}W, Load {_ost.LoadOn}");

                if(!writeToDb)
                {
                    continue;
                }
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
                Log.Info("SerialService done Write DB");

                await Task.Delay(9500); // next data not expected before 1sec, skip readout for about 10sec
                _dev_1.DiscardBufferedData(); // cleanup for next data
                _dev_2.DiscardBufferedData();
            }
            Log.Info("SerialService ends");
        }
        catch (Exception e)
        {
            Log.Error($"SerialService failed and ends: {e.Message}");
            Log.Error(e);
            throw;
        }
        finally
        {
            if (_dev_1 != null)
            {
                _dev_1.Close();
            }
            if (_dev_2 != null)
            {
                _dev_2.Close();
            }
            if (_stream_1 != null)
            {
                _stream_1.Close();  
            }
            if (_stream_2 != null)
            {
                _stream_2.Close();
            }
        }
    }

    private static async Task<string> CollectDataLines(StreamReader dev, CancellationTokenSource cts)
    {
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

    private static bool CheckMpttReadout(string data, MpptData mptt)
    {
        byte[] array = Encoding.ASCII.GetBytes(data);
        byte check = 0;
        for (int i = 0; i < data.Length; i++)
        {
            check += array[i];
        }
        Log.Info($"Byte array size: {array.Length}, string: {data.Length}, Checksum: {check}");

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
                        Log.Error($"SerialService Checksum unexpected dev {mptt.Name}: {pair[1]}");
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
                    if (pair[1] != mptt.Ser)
                    {
                        Log.Error($"SerialService Ser# unexpected dev {mptt.Name}: {pair[1]}");
                        return false;
                    }
                    break;
                case "V":
                    if (int.TryParse(pair[1], out int result))
                    {
                        if(result > 10000)
                        {
                            mptt.Vbatt_V = result / 1000.0;
                            break;
                        }
                    }
                    return false;
                case "I":
                    if (int.TryParse(pair[1], out result))
                    {
                        mptt.Ibatt_A = result / 1000.0;
                    }
                    else return false;
                    break;
                case "PPV":
                    if (int.TryParse(pair[1], out result))
                    {
                        mptt.PowerPV_W = result;
                    }
                    else return false;
                    break;
                case "ERR":
                    if (!mptt.HasError && pair[1] != "0")
                    {
                        mptt.HasError = true;
                        mptt.Err = pair[1];
                        Log.Error($"SerialService New error for mppt {mptt.Name}, Ser#: {mptt.Ser}, Err: {pair[1]}");
                    }
                    if (mptt.HasError && pair[1] == "0")
                    {
                        mptt.HasError = false;
                        mptt.Err = pair[1];
                        Log.Error($"SerialService Removed error for mppt {mptt.Name}, Ser#: {mptt.Ser}");
                    }
                    break;
                case "LOAD":
                    if (pair[1].Trim() == "ON")
                    {
                        mptt.LoadOn = true;
                    }
                    else if (pair[1].Trim() == "OFF")
                    {
                        mptt.LoadOn = false;
                    }
                    else
                    {
                        Log.Error($"SerialService Unexpected string for LOAD at device {mptt.Name}: {pair[1]}");
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


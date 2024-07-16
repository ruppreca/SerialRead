﻿using NLog;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using NodaTime;

namespace SerialRead.Serial.VE.Direct;

internal class SerialService
{
    private readonly PeriodicTimer _timer;
    private Task _timerTask;
    private static readonly CancellationTokenSource _cts = new();
    private static Logger Log = LogManager.GetCurrentClassLogger();

    private const string mppt_1 = "/dev/ttyUSB3";
    private const string mppt_2 = "/dev/ttyUSB2";
    private const string shunt = "/dev/ttyUSB1";
    private const string Serial_1 = "HQ222362TRN";  // OstWest
    private const string Serial_2 = "HQ22236MWWE";  // Süd
    private const string Serial_shunt = "";  // Smart Shunt

    private static readonly string Token = "LbpWnklKJjhgRecvsJiMzImm016Ycze98_55R2aUcjiuN-L4waCeHKr2fUAGXo9dTIgqd0h1kGaaUrd9vBsUdw==";
    private static readonly string Org = "ArHome";
    private static readonly string Bucket = "Batterie";

    MpptData _ostWest;
    MpptData _süd;
    ShuntData _shunt;
    InfluxDBClient _client;

    public SerialService(TimeSpan timerInterval)
    {
        _ostWest = new() { Ser = Serial_1, Name = "OstWest" };
        _süd = new() { Ser = Serial_2, Name = "Süd" };
        _shunt = new() {Name = "SmartShunt" };

        _timer = new(timerInterval);
    }
    public async Task Startup(GlobalProps globalProps, CancellationTokenSource cts)
    {
        try
        {
            string output = "stty -F /dev/ttyUSB2 19200 cs8 -cstopb -parenb".Bash();
            Log.Info($"Bash: {output}");
            output = "stty -F /dev/ttyUSB3 19200 cs8 -cstopb -parenb".Bash();
            Log.Info($"Bash: {output}");
            output = "stty -F /dev/ttyUSB1 19200 cs8 -cstopb -parenb".Bash();
            Log.Info($"Bash: {output}");

            _client = new InfluxDBClient("http://192.168.178.26:8086", Token);
            _timerTask = DoWorkAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"SerialService failed to init devices: {ex.Message}");
            throw;
        }
        Log.Info("SerialService started success");
        while (!cts.Token.IsCancellationRequested)
        {
            await Task.Delay(20000, cts.Token);
        }
        Log.Info("SerialService startup ends");
    }

    private async Task DoWorkAsync()
    {
        //TODO: nicht Alles bei nacht auslesen ??
        //Log.Info("SerialService run DoWorkAsync");
        try
        {
            while (!_cts.Token.IsCancellationRequested && await _timer.WaitForNextTickAsync(_cts.Token))
            {
                Log.Info("DoWorkAsync beginns");
                try
                {
                    bool writeToDb = true;
                    try
                    {
                        var task1 = CollectDataLines(mppt_1);
                        var task2 = CollectDataLines(mppt_2);
                        var task3 = CollectDataLines(shunt);
                        string[] result = await Task.WhenAll(task1, task2, task3);
                        if (task1.IsCompletedSuccessfully && result[0] != null)
                        {
                            if (!CheckMpttReadout(result[0], _ostWest))
                            {
                                Log.Error($"SerialService failed interpert data dev {_ostWest.Name}: {result[0]}");
                                writeToDb = false;
                            }
                            Log.Info($"Mptt {_ostWest.Name}: Vbatt {_ostWest.Vbatt_V:0.00}, Ibatt {_ostWest.Ibatt_A:0.00}A, Power {_ostWest.PowerPV_W}W, State: {_ostWest.State}, Load {_ostWest.LoadOn}");
                        }
                        else
                        {
                            Log.Error($"Task mppt_1 timeout: {task1.Status}, result is {result[0]}");
                            writeToDb = false;
                        }
                        if (task2.IsCompletedSuccessfully && result[1] != null)
                        {
                            if (!CheckMpttReadout(result[1], _süd))
                            {
                                Log.Error($"SerialService failed interpert data dev {_süd.Name}: {result[1]}");
                                writeToDb = false;
                            }
                            Log.Info($"Mptt {_süd.Name}: Vbatt {_süd.Vbatt_V:0.00}V, Ibatt {_süd.Ibatt_A:0.00}A, Power {_süd.PowerPV_W}W, State: {_süd.State}, Load {_süd.LoadOn}");
                        }
                        else
                        {
                            Log.Error($"Task mppt_2 timeout: {task2.Status}, result is {result[1]}");
                            writeToDb = false;
                        }
                        if (task3.IsCompletedSuccessfully && result[2] != null)
                        {
                            if (!CheckShuntReadout(result[2], _shunt))
                            {
                                Log.Error($"SerialService failed interpert data dev {_shunt.Name}: {result[2]}");
                                writeToDb = false;
                            }
                            Log.Info($"Shunt: SOC {_shunt.SOC:0.0}%, Vbatt {_shunt.Vbatt_V:0.00}V, Ibatt {_shunt.Ibatt_A:0.00}A, Power {_shunt.Power_W}W, TimeToGo {_shunt.TimeToGo_min}min, Consumed_Ah {_shunt.Consumed_Ah:0.00}Ah, DM {_shunt.DM:0.0}%");
                        }
                        else
                        {
                            Log.Error($"Task shunt timeout: {task3.Status}, result is {result[2]}");
                            writeToDb = false;
                        }
                    }
                    catch (AggregateException ex)
                    {
                        Log.Error($"\nAggregateException from serial read {ex.Message}\n");
                        Log.Error(ex);
                    }

                    if (_cts.IsCancellationRequested) return;
                    if (writeToDb)
                    {
                        if(_shunt.Vbatt_V < 20)
                        {
                            Log.Error($"SerialService shunt VBatt below 20V, unexpected! id {_süd.Vbatt_V}");
                        }
                        else
                        {
                            try //  write to Influx DB
                            {
                                using (var writeApi = _client.GetWriteApi())
                                {
                                    // Write by Point
                                    var point = PointData.Measurement("batterie")
                                        //.Tag("location", "west")
                                        .Field("Vbatt_V", _shunt.Vbatt_V)
                                        .Field("Ibatt_A", _shunt.Ibatt_A)
                                        .Field("SOC_%", _shunt.SOC)
                                        .Field("OstWestPV_W", _ostWest.PowerPV_W)
                                        .Field("SüdPV_W", _süd.PowerPV_W)
                                        .Field("IPV_A", _ostWest.Ibatt_A + _süd.Ibatt_A)
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
                }
                catch (Exception e)
                {
                    Log.Error($"Exeption in SerialService while loop: {e.Message}");
                    Log.Error(e);
                }
            }
        }
        catch (AggregateException ex)
        {
            Log.Error($"\nAggregateException in SerialService DoWorkAsync: {ex.Message}\n");
            Log.Error(ex);
        }
        catch (Exception ex)
        {
            Log.Error("Exception in SerialService DoWorkAsync: {ex.Message}");
            Log.Error(ex);
            //throw;
        }
    }

    public async Task StopAsync()
    {
        if (_timerTask is null)
        {
            return;
        }
        _cts.Cancel();
        await _timerTask;
        _cts.Dispose();
        Log.Info("SerialService just stopped");
        await Task.Delay(200); 
    }

    private static async Task<string> CollectDataLines(string mppt)
    {
        var s_cts = new CancellationTokenSource();
        s_cts.CancelAfter(2000);
        byte[] buffer = new byte[1000];
        int offset = 0;
        try
        {
            using (var stream = new FileStream(mppt, FileMode.Open, FileAccess.Read))
            {
          
                int Poffset = -1;  // if one byte is retured from read, then offset is 0, points to buffer[0]
                bool foundPI = false;
                while (!foundPI) // find the bytes accoding to "PI" 
                {
                    if (s_cts.IsCancellationRequested)
                    {
                        Log.Error($"Cancel happed, {mppt} while loop for foundPI timeout");
                        Log.Error($"Readout until cancel: {Encoding.UTF8.GetString(buffer, 0, offset)}");
                        return null;
                    }
                    int lastOffset = offset <= 2 ? 2 : offset;
                    while (offset < lastOffset + 25) // read min 25 bytes //TODO why is the 50 critical ????
                    {
                        offset += await stream.ReadAsync(buffer, offset, buffer.Length - offset, s_cts.Token);
                    }
                    // offset zeigt auf das nächste leer byte im Buffer
                    for (int i = lastOffset; i < offset; i++) // search bytes for PID in reverse order
                    {
                        if (buffer[i] == 'D' && lastOffset > 1) // make shure buffer[i] to [i+2] is vaild
                        {
                            if (buffer[i - 1] == 'I' && buffer[i - 2] == 'P')
                            {
                                foundPI = true;
                                Poffset = i-2;
                                //Log.Info($"{mppt} foundPI {foundPI} at {Poffset}");
                                break;
                            }
                        }
                    }
                }

                s_cts = new CancellationTokenSource();
                s_cts.CancelAfter(800);  // rest of string must be fast << 1sec
                int Choffset = 0;
                bool foundCh = false;
                while (!foundCh) // find the byte according to "Ch" 
                {
                    if (s_cts.IsCancellationRequested)
                    {
                        Log.Error($"Cancel happed, {mppt} while loop for foundCh");
                        Log.Error($"Readout until cancel: {Encoding.UTF8.GetString(buffer, Poffset, offset - 2)}");
                        return null;
                    }
                    int startindex = offset;
                    while (offset < startindex + 8) // to get Checksun add min 8 bytes
                    {
                        offset += await stream.ReadAsync(buffer, offset, buffer.Length - offset, s_cts.Token);
                    }
                    for (int i = startindex; i < offset; i++) // search bytes for Che in reverse order
                    {
                        if (buffer[i] == 'e')  // Poffset + startindex - 2 is always >= 0
                        {
                            //Log.Info($"{mppt} found e at {i}");
                            if (buffer[i - 1] == 'h' && buffer[i - 2] == 'C')
                            {
                                foundCh = true;
                                Choffset = i-2;
                                //Log.Info($"{mppt} foundCh {foundCh} at {Choffset}");
                                break;
                            }
                        }
                    }
                }

                byte sum = 23; // add a 0d 0a before the PID found  (https://www.victronenergy.com/live/vedirect_protocol:faq#q8how_do_i_calculate_the_text_checksum)
                int end = Choffset - Poffset + 10; 
                //byte[] checkbytes = new byte[500];

                //Log.Info($"data\n{BitConverter.ToString(buffer, Poffset, end)}");  // end must be lenght

                //int j = 0;
                for (int i = Poffset; i < Poffset + end; i++)
                {
                    if (i < Poffset + end -1 && buffer[i] == '\n' && buffer[i + 1] == '\n')
                    {
                        sum += (byte)'\r';
                    }
                    else
                    { 
                        sum += buffer[i]; 
                    }
                    //checkbytes[j++] = buffer[i];
                }
                if (sum != 0)
                {
                    Log.Info($"{mppt} Collected from {Poffset} length {end} bytes, sum = {sum}");
                    //Log.Info($"checksum used\n{BitConverter.ToString(checkbytes, 0, end)}");
                }
                string result = Encoding.UTF8.GetString(buffer, Poffset, end);
                //Log.Info($"reads:\n{result}");
                return result;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Exception while reading serial {mppt}, offset {offset}: {ex.Message}");
            Log.Error(ex);
            Log.Info($"data\n{BitConverter.ToString(buffer, 0, offset)}");
            return null;
        }
        finally
        {
            s_cts.Dispose();
        }
    }

    private static bool CheckShuntReadout(string data, ShuntData shunt)
    {
        string[] parts = data.Split("\n\n");
        foreach (var item in parts)
        {
            string[] pair = item.Split('\t');
            if (pair.Length != 2) continue;
            switch (pair[0])
            {
                case "PID":
                    if (pair[1] != "0xA389") //SmartShunt 500A/50mV
                    {
                        Log.Error($"Read Shunt, PID unexpected dev {shunt.Name}: {pair[1]}");
                        return false;
                    }
                    break;
                case "V":
                    if (int.TryParse(pair[1], out int result))
                    {
                        if (result < 100000 && result > 40000)
                        {
                            shunt.Vbatt_V = result / 1000.0;
                            break;
                        }
                        else
                        {
                            Log.Error($"Read Shunt, V unexpected dev {shunt.Name}: {pair[1]}");
                            return false;
                        }
                    }
                    return PrintUnexpected(shunt.Name, pair[0], pair[1]);
                case "I":
                    if (int.TryParse(pair[1], out result))
                    {
                        if (result < 100000 && result > -100000)
                        {
                            shunt.Ibatt_A = result / 1000.0;
                            break;
                        }
                        else
                        {
                            Log.Error($"Read Shunt, I unexpected dev {shunt.Name}: {pair[1]}");
                            return false;
                        }
                    }
                    return PrintUnexpected(shunt.Name, pair[0], pair[1]);
                case "SOC":
                    if (int.TryParse(pair[1], out result))
                    {
                        shunt.SOC = result / 10.0;
                        if (result > 1000 || result < 0)
                        {
                            Log.Error($"Read Shunt, StateOfCharge unexpected, dev {shunt.Name}: {pair[1]}");
                        }
                        break;
                    }
                    return PrintUnexpected(shunt.Name, pair[0], pair[1]);
                case "DM":
                    if (int.TryParse(pair[1], out result))
                    {
                        shunt.DM = result / 10.0;
                        if (result > 10 || result < -10)
                        {
                            Log.Error($"Read Shunt, Midpoint deviation is high, dev {shunt.Name}: {pair[1]}");
                        }
                        break;
                    }
                    return PrintUnexpected(shunt.Name, pair[0], pair[1]);
                case "CE":
                    if (int.TryParse(pair[1], out result))
                    {
                        shunt.Consumed_Ah = result / 1000.0;
                        break;
                    }
                    return PrintUnexpected(shunt.Name, pair[0], pair[1]);
                case "P":
                    if (int.TryParse(pair[1], out result))
                    {
                        shunt.Power_W = result;
                        if (result > 3000 || result < -1000)
                        {
                            Log.Error($"Read Shunt, Power unexpected dev, dev {shunt.Name}: {pair[1]}");
                        }
                        break;
                    }
                    return PrintUnexpected(shunt.Name, pair[0], pair[1]);
                case "ERR":
                    if (!shunt.HasError && pair[1] != "0")
                    {
                        shunt.HasError = true;
                        shunt.Err = pair[1];
                        Log.Error($"Read Shunt, New error for Shunt {shunt.Name}, Err: {pair[1]}");
                    }
                    if (shunt.HasError && pair[1] == "0")
                    {
                        shunt.HasError = false;
                        shunt.Err = pair[1];
                        Log.Error($"Read Shunt, Removed error for Shunt {shunt.Name}");
                    }
                    break;
                case "TTG":
                    if (int.TryParse(pair[1], out result))
                    {
                        shunt.TimeToGo_min= result;
                        if (result > 144000 || result < -1)
                        {
                            Log.Error($"Read Shunt, unexpected TimeToGo_min, dev {shunt.Name}: {pair[1]}");
                        }
                        break;
                    }
                    return PrintUnexpected(shunt.Name, pair[0], pair[1]);
                default:
                    break;
            }
        }
        return true;
    }

    private static bool PrintUnexpected(string name, string lable, string value)
    {
        Log.Error($"Read Shunt, unexpected data  from {name}: {lable}->{value}");
        return false;
    }

    private static bool CheckMpttReadout(string data, MpptData mppt)
    {
        string[] parts = data.Split("\n\n");
        foreach (var item in parts)
        {
            string[] pair = item.Split('\t');
            if (pair.Length != 2) continue;
            switch (pair[0])
            {
                case "PID":
                    if (pair[1] != "0xA060")  // PID for SmartSolar MPPT 100|20 48V
                    {
                        Log.Error($"Read Mppt, PID unexpected dev {mppt.Name}: {pair[1]}");
                        return false;
                    }
                    break;
                case "SER#":
                    if (pair[1] != mppt.Ser)
                    {
                        Log.Error($"Read Mppt, Ser# unexpected dev {mppt.Name}: {pair[1]}");
                        return false;
                    }
                    break;
                case "V":
                    if (int.TryParse(pair[1], out int result))
                    {
                        if (result > 10000 || result < 40)
                        {
                            mppt.Vbatt_V = result / 1000.0;
                            break;
                        }
                        else
                        {
                            Log.Error($"Read Mppt, V unexpected dev {mppt.Name}: {pair[1]}");
                            return false;
                        }
                    }
                    return false;
                case "I":
                    if (int.TryParse(pair[1], out result))
                    {
                        mppt.Ibatt_A = result / 1000.0;
                    }
                    else
                    {
                        Log.Error($"Read Mppt, I unexpected dev {mppt.Name}: {pair[1]}");
                        return false;
                    }
                    break;
                case "PPV":
                    if (int.TryParse(pair[1], out result))
                    {
                        mppt.PowerPV_W = result;
                    }
                    else
                    {
                        Log.Error($"Read Mppt, PPV unexpected dev {mppt.Name}: {pair[1]}");
                        return false;
                    }
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
                    else
                    {
                        Log.Error($"Read Mppt, CS unexpected dev {mppt.Name}: {pair[1]}");
                        return false;
                    }
                    break;
                case "ERR":
                    if (!mppt.HasError && pair[1] != "0")
                    {
                        mppt.HasError = true;
                        mppt.Err = pair[1];
                        Log.Error($"Read Mppt, New error for mppt {mppt.Name}, Ser#: {mppt.Ser}, Err: {pair[1]}");
                    }
                    if (mppt.HasError && pair[1] == "0")
                    {
                        mppt.HasError = false;
                        mppt.Err = pair[1];
                        Log.Error($"Read Mppt, Removed error for mppt {mppt.Name}, Ser#: {mppt.Ser}");
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
                        Log.Error($"SRead Mppt, Unexpected string for LOAD at device {mppt.Name}: {pair[1]}");
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



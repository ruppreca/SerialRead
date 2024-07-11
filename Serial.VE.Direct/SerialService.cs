using NLog;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace SerialRead.Serial.VE.Direct;

internal class SerialService
{
    private readonly PeriodicTimer _timer;
    private Task _timerTask;
    private static readonly CancellationTokenSource _cts = new();
    private static Logger Log = LogManager.GetCurrentClassLogger();

    private const string mppt_1 = "/dev/ttyUSB3";
    private const string mppt_2 = "/dev/ttyUSB2";
    private const string Serial_1 = "HQ222362TRN";  // OstWest
    private const string Serial_2 = "HQ22236MWWE";  // Süd

    private static readonly string Token = "LbpWnklKJjhgRecvsJiMzImm016Ycze98_55R2aUcjiuN-L4waCeHKr2fUAGXo9dTIgqd0h1kGaaUrd9vBsUdw==";
    private static readonly string Org = "ArHome";
    private static readonly string Bucket = "Batterie";

    MpptData _ostWest;
    MpptData _süd;
    InfluxDBClient _client;

    public SerialService(TimeSpan timerInterval)
    {
        _ostWest = new() { Ser = Serial_1, Name = "OstWest" };
        _süd = new() { Ser = Serial_2, Name = "Süd" };

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
            _timerTask = DoWorkAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"SerialService failed to init devices: {ex.Message}");
            throw;
        }
        Log.Info("SerialService started success");
    }

    private async Task DoWorkAsync()
    {
        //TODO: nicht bei nacht auslesen ?
        //Log.Info("SerialService run DoWorkAsync");
        try
        {
            while (!_cts.Token.IsCancellationRequested && await _timer.WaitForNextTickAsync(_cts.Token))
            {
                //Log.Info("DoWorkAsync beginns");
                try
                {
                    bool writeToDb = true;
                    try
                    {
                        var task1 = CollectDataLines(mppt_1);
                        var task2 = CollectDataLines(mppt_2);
                        string[] result = await Task.WhenAll(task1, task2);
                        if (task1.IsCompletedSuccessfully && result[0] != null)
                        {
                            if (!CheckMpttReadout(result[0], _ostWest))
                            {
                                Log.Error($"SerialService failed interpert data dev {_ostWest.Name}: {result[0]}");
                                writeToDb = false;
                            }
                            Log.Info($"Mptt {_ostWest.Name}: Vbatt {_ostWest.Vbatt_V}, Ibatt {_ostWest.Ibatt_A}A, Power {_ostWest.PowerPV_W}W, State: {_ostWest.State}, Load {_ostWest.LoadOn}");
                        }
                        else
                        {
                            Log.Error($"Task mppt_1 failed: {task1.Status}, result is {result}");
                            writeToDb = false;
                        }
                        if (task2.IsCompletedSuccessfully && result[1] != null)
                        {
                            if (!CheckMpttReadout(result[1], _süd))
                            {
                                Log.Error($"SerialService failed interpert data dev {_süd.Name}: {result[1]}");
                                writeToDb = false;
                            }
                            Log.Info($"Mptt {_süd.Name}: Vbatt {_süd.Vbatt_V}, Ibatt {_süd.Ibatt_A}A, Power {_süd.PowerPV_W}W, State: {_süd.State}, Load {_süd.LoadOn}");
                        }
                        else
                        {
                            Log.Error($"Task mppt_2 failed: {task2.Status}, result is {result}");
                            writeToDb = false;
                        }
                    }
                    catch (AggregateException ex)
                    {
                        Log.Error($"\nAggregateException from serial read {ex.Message}\n");
                        Log.Error(ex);
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Error($"\nCollectDataLines Tasks timed out.\n");
                    }

                    if (_cts.IsCancellationRequested) return;
                    if (writeToDb)
                    {
                        if(_süd.Vbatt_V < 20)
                        {
                            Log.Error($"SerialService VBatt below 20V, unexpected! id {_süd.Vbatt_V}");
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
                                        .Field("Vbatt_V", _süd.Vbatt_V)
                                        .Field("Ibatt_A", (_süd.Ibatt_A + _ostWest.Ibatt_A))
                                        .Field("OstWestPV_W", _ostWest.PowerPV_W)
                                        .Field("SüdPV_W", _süd.PowerPV_W)
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
        s_cts.CancelAfter(3000);
        byte[] buffer = new byte[1000];
        try
        {
            using (var stream = new FileStream(mppt, FileMode.Open, FileAccess.Read))
            {
                int offset = 0;
                int Poffset = 0;
                bool foundPI = false;
                while (!foundPI) // find the bytes accoding to "PI" 
                {
                    if (s_cts.IsCancellationRequested) return null;
                    while (offset < 50) // read 100 bytes //TODO why is the 50 critical ????
                    {
                        offset += await stream.ReadAsync(buffer, offset, buffer.Length - offset, s_cts.Token);
                    }
                    for (int i = 0; i < offset; i++)
                    {
                        if (buffer[i] == 'P')
                        {
                            Poffset = i;
                            if (i < offset && buffer[i + 1] == 'I')
                            {
                                foundPI = true;
                                Log.Info($"{mppt} foundPI {foundPI} at {Poffset}");
                                break;
                            }
                        }
                    }
                }

                int Choffset = 0;
                bool foundCh = false;
                while (!foundCh) // find the byte according to "Ch" 
                {
                    if (s_cts.IsCancellationRequested) return null;
                    int startindex = offset;
                    int readcount = 100;
                    while (offset < startindex + 50 && readcount-- > 0) // read 50 bytes
                    {
                        offset += await stream.ReadAsync(buffer, offset, buffer.Length - offset, s_cts.Token);
                    }
                    if(readcount <= 0)
                    {
                        Log.Info($"{mppt} readcount exceeded 100, read fail");
                        return null;
                    }
                    for (int i = Poffset; i < offset; i++)
                    {
                        if (buffer[i] == 'C')
                        {
                            Choffset = i;
                            if (i < offset && buffer[i + 1] == 'h')
                            {
                                foundCh = true;
                                Log.Info($"{mppt} foundCh {foundCh} at {Choffset}");
                                break;
                            }
                        }
                    }
                }
                while (offset < Choffset + 10) // check all bytes up to checksum bye are read
                {
                    int bytes = await stream.ReadAsync(buffer, offset, Choffset + 10 - offset, s_cts.Token);
                    offset += bytes;
                }

                //byte sum = 0;
                //int end = Choffset - Poffset + 10;
                //byte[] checkbytes = new byte[300];
                //int j = 0;
                //for (int i = Poffset; i < end; i++)
                //{
                //    if(i < end && buffer[i] == '\n' && buffer[i+1] == '\n')
                //    {

                //    }
                //    sum += buffer[i];
                //    checkbytes[j++] = buffer[i];
                //}
                //Log.Info($"{mppt} Collected {end} bytes, sum = {sum}");
                //Log.Info($"checksum used\n{BitConverter.ToString(checkbytes, 0, end)}");
                
                //Log.Info($"data\n{BitConverter.ToString(buffer, Poffset, Choffset - Poffset + 10)}");
                string result = Encoding.UTF8.GetString(buffer, Poffset, Choffset - Poffset + 10);
                //Log.Info($"reads:\n{result}");
                return result;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Exception while reading serial: {ex.Message}");
            return null;
        }
        finally
        {
            s_cts.Dispose();
        }
    }

    

    private static bool CheckMpttReadout(string data, MpptData mppt)
    {
        string[] parts = data.Split("\n\n");
        foreach (var item in parts)
        {
            //if (item.Length < 2) continue;
            string[] pair = item.Split('\t');
            if (pair.Length != 2) continue;
            switch (pair[0])
            {
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
                        if (result > 10000)
                        {
                            mppt.Vbatt_V = result / 1000.0;
                            break;
                        }
                        else
                        {
                            Log.Error($"SerialService V unexpected dev {mppt.Name}: {pair[1]}");
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
                        Log.Error($"SerialService I unexpected dev {mppt.Name}: {pair[1]}");
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
                        Log.Error($"SerialService PPV unexpected dev {mppt.Name}: {pair[1]}");
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
                        Log.Error($"SerialService CS unexpected dev {mppt.Name}: {pair[1]}");
                        return false;
                    }
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



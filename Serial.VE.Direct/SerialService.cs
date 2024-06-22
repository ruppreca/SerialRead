﻿using GPIOControl.PwmKemo;
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

    FileStream _stream_1;
    StreamReader _dev_1;
    FileStream _stream_2;
    StreamReader _dev_2;
    MpptData _west;
    MpptData _ost;

    public SerialService()
    {
        _west = new() { Ser = Serial_1, Name = "West"};
        _ost = new() { Ser = Serial_2, Name = "Ost" };

        try
        {
            FileStream _stream_1 = new FileStream(mppt_1, FileMode.Open, FileAccess.Read);
            _dev_1 = new StreamReader(_stream_1, Encoding.UTF8, true, 128);
            FileStream _stream_2 = new FileStream(mppt_2, FileMode.Open, FileAccess.Read);
            _dev_2 = new StreamReader(_stream_2, Encoding.UTF8, true, 128);
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
                lineOfText = await CollectDataLines(_dev_1);
                if (!CheckMpttReadout(lineOfText, _west))
                {
                    Log.Error($"SerialService failed interpert data dev {_west.Name} {lineOfText}");
                }
                Log.Info($"Mptt {_west.Name}: Vbatt {_west.Vbatt_V}, Ibatt {_west.Ibatt_A}A, Power {_west.PowerPV_W}W, Load {_west.LoadOn}");

                if (cts.IsCancellationRequested) break;
                lineOfText = await CollectDataLines(_dev_2);
                if (!CheckMpttReadout(lineOfText, _ost))
                {
                    Log.Error($"SerialService failed interpert data dev {_ost.Name} {lineOfText}");
                }
                Log.Info($"Mptt {_ost.Name}: Vbatt {_ost.Vbatt_V}, Ibatt {_ost.Ibatt_A}A, Power {_ost.PowerPV_W}W, Load {_ost.LoadOn}");
                await Task.Delay(1500); // next data not expected before 1sec, skip one readout
            }
            Log.Info("SerialService ends");
        }
        catch (Exception e)
        {
            Log.Error($"SerialService failed to init: {e.Message}");
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

    private static async Task<string> CollectDataLines(StreamReader dev)
    {
        string result;
        string line;
        do
        {
            line = await dev.ReadLineAsync();
        }
        while (!line.StartsWith("Checksum"));
        result = line + ';';
        do
        {
            line = await dev.ReadLineAsync();
            if (line.Length < 2) continue;
            result += line + ';';
        }
        while (!line.StartsWith("HSDS"));

        //Log.Info($"{result}");
        return result;
    }

    private static bool CheckMpttReadout(string data, MpptData mptt)
    {
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
                        mptt.Vbatt_V = result / 1000.0;
                    }
                    else return false;
                    break;
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


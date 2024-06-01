﻿using GPIO_Control.Zpump;
using NLog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GPIOControl.PwmKemo;

internal class PwmKemo
{
    private static Logger Log = LogManager.GetCurrentClassLogger();

    
    private GpioPins Gpio;
    const int PcmPin = 18; //GPIO18 is pin 12 on RPi, is used as PCM0 output pin / /sys/class/pwm/pwmchip0

    const string PwmChip = @"/sys/class/pwm/pwmchip0";
    const string Pwm0 = @"/sys/class/pwm/pwmchip0/pwm0";

    private GlobalProps _globalProps;
    private DateTime _lastHeaterTime = DateTime.Now;

    public string Period { get; set; } = "1000000"; //period 1ms
    public int DutyCycle { get; set; } = 0;
    public int HeaterPower { get; set; } = 0;

    private bool _heaterOff = false;
    public PwmKemo(GpioPins gpio)
    {
        Gpio = gpio;
    }

    public async Task init(GlobalProps globalProps)
    {
        _globalProps = globalProps;
        try
        {
            //Log.Info($"List Dir: {PwmChip}");
            //string[] allfiles = Directory.GetFileSystemEntries(PwmChip, "*", SearchOption.TopDirectoryOnly);
            //foreach (var file in allfiles)
            //{
            //    FileInfo info = new FileInfo(file);
            //    Log.Info($"       found: {info.Name}");
            //}

            if (Gpio!= null)
            {
                //Gpio.KemoOn();
                Gpio.KemoOn();     //  240601 Off while not Power value available (Zähler lesen kaputt)

            }

            if (!Directory.Exists(Pwm0))
            {
                Log.Info("No pwm0 device -> Write a 0 to export");
                File.WriteAllText(Path.Combine(PwmChip, "export"), "0");
                await Task.Delay(500);
            }

            if (Directory.Exists(Pwm0))
            {
                Log.Info($"Device {Pwm0} found");
                File.WriteAllText(Path.Combine(Pwm0, "period"), Period);
                File.WriteAllText(Path.Combine(Pwm0, "duty_cycle"), DutyCycle.ToString());
                File.WriteAllText(Path.Combine(Pwm0, "enable"), "1");
                Log.Info($"Device {Pwm0} setup period {Period}, duty cycle {DutyCycle}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exeption: faild setup pwm0");
            Log.Error($"{ex.Message}");
            throw;
        }
    }

    private const int wantedInfeedpower = 10;
    double k = 0.6; // control factor
    public int controlPower(int powerConsumed) // is the power readout from Stromzähler
    {
        try
        {
            if (_globalProps.FlanschHotAlarm)
            {
                DutyCycle = 0;
            }
            else if (powerConsumed > 0) // reduce heater power
            {
                if (powerConsumed > HeaterPower)
                {
                    HeaterPower = 0;
                }
                else
                {
                    HeaterPower -= (int)(powerConsumed * k);
                }
                if(HeaterPower <= 0)
                {
                    if(_heaterOff = false)
                    {
                        Log.Info($"Heater power OFF, consumed {powerConsumed}");
                    }
                    _heaterOff = true;
                }
                else
                {
                    _heaterOff = false;
                    Log.Info($"Heater power reduced to {HeaterPower}, consumed {powerConsumed}");
                }
            }
            else if (powerConsumed <= -wantedInfeedpower) // increase heater power
            {
                HeaterPower -= (int)((powerConsumed + wantedInfeedpower) * k);
                HeaterPower = HeaterPower > 318 ? 318 : HeaterPower;
                Log.Info($"Heater power increased to {HeaterPower}, consumed {powerConsumed}");
            }

            HeaterPower = HeaterPower <= 0 ? 0 : HeaterPower;
            if (Directory.Exists(Pwm0))
            {
                DutyCycle = PowerToPwmLookup.LookUp(HeaterPower);

                File.WriteAllText(Path.Combine(Pwm0, "duty_cycle"), DutyCycle.ToString());
                Log.Debug($"Device {Pwm0} changed duty cycle {DutyCycle}");
            }
            else
            {
                Log.Error("Faild update Pwm0 duty cycle, NO directory");
            }

            if(HeaterPower > 1)
            {
                Gpio.KemoOn();
                _lastHeaterTime = DateTime.Now;
            }
            else
            {
                if (DateTime.Now - _lastHeaterTime > TimeSpan.FromMinutes(2))
                {
                    Gpio.KemoOff(); // Switch of after 2min of not heating
                }
            }
            return HeaterPower;
        }
        catch (Exception ex)
        {
            Log.Error("Exeption faild update Pwm0 duty cycle");
            Log.Error($"{ex.Message}");
            throw;
        }
    }

    public void alarmOff()
    {
        _globalProps.FlanschHotAlarm = true;
        Gpio.KemoOff();
        if (Directory.Exists(Pwm0))
        {
            File.WriteAllText(Path.Combine(Pwm0, "duty_cycle"), "0");
            File.WriteAllText(Path.Combine(Pwm0, "enable"), "0");
            Log.Info($"Alarm Off for Device {Pwm0} changed duty cycle {DutyCycle}, enable to 0");
        }
        else
        {
            Log.Error(">>>>>>>>>>>>>>>>>>>>   set alarm off failed !!!!!!!");
            Log.Error("Faild update Pwm0 duty cycle, NO directory");
        }
    }

    // was used for testing
    //public async Task loop()
    //{
    //    try
    //    {
    //        if (Directory.Exists(Pwm0))
    //        {
    //            if (DutyCycle > 900000 || HeaterPower > 300)
    //            {
    //                HeaterPower = 0;
    //                DutyCycle = 0;
    //            }
    //            else
    //            {
    //                HeaterPower += 20;
    //                DutyCycle = PowerToPwmLookup.LookUp(HeaterPower);
    //            }
    //            File.WriteAllText(Path.Combine(Pwm0, "duty_cycle"), DutyCycle.ToString());
    //            Log.Info($"Device {Pwm0} changed duty cycle {DutyCycle}, HeaterPower: {HeaterPower}");
    //            await Task.Delay(100);
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Log.Error("Exeption faild to swich Z-Pump off");
    //        Log.Error($"{ex.Message}");
    //        throw;
    //    }
    //}
}

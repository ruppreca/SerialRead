using GPIO_Control.Zpump;
using NLog;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
    private HttpClient _client;

    const string AhoyUrl = "http://192.168.178.64/api/ctrl";

    public string Period { get; set; } = "1000000"; //period 1ms
    public int DutyCycle { get; set; } = 0;
    public int HeaterPower { get; set; } = 0;
    public int Hm600Power { get; set; } = 0;

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
                Gpio.KemoOn();
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

            _client = new();
            if(_client != null)
            {
                Log.Info("Hppt client init OK, set HM power to 0");
                await SetHM600Power(0);
            }
            

            Gpio.KemoOff();     //  240601 Off while not Power value available (Zähler lesen kaputt)
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

    private const int MaxHm600Power = 600;  // in Watt
    double kHm = 0.7; // control factor for Hm-600 "Nulleinspeisung"
    int _oldPower = 0;
    internal async Task<int> limitHm600Power(int powerConsumed)
    {
        if (powerConsumed > 0 && Hm600Power <= MaxHm600Power) // increase HM power setting
        {
            Hm600Power += (int)((powerConsumed + wantedInfeedpower) * kHm);
            if (Hm600Power > MaxHm600Power)
            {
                Hm600Power = MaxHm600Power;
            }
            Log.Info($"HM-600 power increased to {Hm600Power}, consumed {powerConsumed}");
        }
        else if (powerConsumed <= -wantedInfeedpower)
        {
            Hm600Power += (int)(powerConsumed * kHm);  //a decrease is done because powerConsumed is negative
            if (Hm600Power < 0)
            {
                Hm600Power = 0;
            }
            Log.Info($"HM-600 power reduced to {Hm600Power}, consumed {powerConsumed}");
        }
        if(Hm600Power != _oldPower)
        {
            _oldPower = Hm600Power;
            await SetHM600Power(Hm600Power);
        }
        return Hm600Power;
    }

    private async Task SetHM600Power(int power)
    {
        power = power > 600 ? 600 : power;
        power = power < 0 ? 0 : power;
        string myJson = $"{{\"id\": 0, \"cmd\": \"limit_nonpersistent_absolute\", \"val\": {power}}}";

        var response = await _client.PostAsync(AhoyUrl, new StringContent(myJson, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode) 
        {
            var content = await response.Content.ReadAsStringAsync();
            Log.Info($"Http Post failed {response.StatusCode}, reason {response.ReasonPhrase}, content: {content}");
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

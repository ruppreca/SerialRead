using NLog;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Z_PumpControl_Raspi;

internal class PwmKeba
{
    private static Logger Log = LogManager.GetCurrentClassLogger();

    private static Mqtt _mqtt;
    const int PcmPin = 18; //GPIO18 is pin 12 on RPi, is used as PCM0 output pin / /sys/class/pwm/pwmchip0

    const string PwmChip = @"/sys/class/pwm/pwmchip0";
    const string Pwm0 = @"/sys/class/pwm/pwmchip0/pwm0";

    public string Period { get; set; } = "1000000"; //period 1ms
    public int DutyCycle { get; set; } = 100000;
    public PwmKeba()
    {
        _mqtt = new Mqtt();
    }

    public async Task init()
    {
        try
        {
            Log.Info($"List Dir: {PwmChip}");
            string[] allfiles = Directory.GetFileSystemEntries(PwmChip, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in allfiles)
            {
                FileInfo info = new FileInfo(file);
                Log.Info($"       found: {info.Name}");
            }

            Log.Info($"List Dir: {Pwm0}");
            allfiles = Directory.GetFileSystemEntries(Pwm0, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in allfiles)
            {
                FileInfo info = new FileInfo(file);
                Log.Info($"       found: {info.Name}");
            }



            //if (!File.Exists(Pwm0))
            //{
            //    Log.Info("No pwm0 device -> Write a 0 to export");
            //    File.WriteAllText(Path.Combine(PwmChip, "export"), "0");
            //    await Task.Delay(500);
            //}

            if (Directory.Exists(Pwm0))
            {
                Log.Info($"Device {Pwm0} found");
                File.WriteAllText(Path.Combine(Pwm0, "period"), Period);
                File.WriteAllText(Path.Combine(Pwm0, "duty_cycle"), DutyCycle.ToString());
                File.WriteAllText(Path.Combine(Pwm0, "enable"), "1");
                Log.Info($"Device {Pwm0} setup period {Period}, duty cycle {DutyCycle}");
            }
        }
        catch (System.Exception ex)
        {
            Log.Error("Exeption: faild setup pwm0");
            Log.Error($"{ex.Message}");
            throw;
        }
    }

    public async Task loop()
    {
        try
        {
            if (Directory.Exists(Pwm0))
            {
                if(DutyCycle > 900000)
                {
                    DutyCycle = 0;
                }
                else
                {
                    DutyCycle += 100000;
                }
                File.WriteAllText(Path.Combine(Pwm0, "duty_cycle"), DutyCycle.ToString());
                Log.Info($"Device {Pwm0} changed duty cycle {DutyCycle}");
                await Task.Delay(100);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error("Exeption faild to swich Z-Pump off");
            Log.Error($"{ex.Message}");
            throw;
        }
    }
}

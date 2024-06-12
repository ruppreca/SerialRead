using GPIOControl;
using NLog;
using System;
using System.Device.Gpio;
using System.Threading;
using System.Threading.Tasks;

namespace GPIO_Control.Zpump;

internal class GpioPins
{
    private static Logger Log = LogManager.GetCurrentClassLogger();
    private static GpioController _controller = new();
    private static Mqtt _mqtt;
    const int PumpPin = 24; //GPIO24 is pin 18 on RPi, Switches on the Solidstate Relais für Zpump
    const int KemoPin = 23; //GPIO23 is pin 16 on RPi, Switches on the Solidstate Relais for Kemo Control Heizstab

    bool IsPumpOn = false;
    bool _kemoIsOn = false;

    public GpioPins()
    {
        try
        {
            _controller.OpenPin(PumpPin, PinMode.Output);
            _controller.OpenPin(KemoPin, PinMode.Output);
            _mqtt = new Mqtt();
            Log.Info("Z-Pump constructor done");
        }
        catch (Exception ex)
        {
            Log.Info("Exeption faild construct Zpump");
            Log.Error($"{ex.Message}");
        }
    }
    public async Task On()
    {
        try
        {
            await _mqtt.publishZ("0");
            _controller.Write(PumpPin, PinValue.High);
            Log.Info("Z-PumpPin is on");
            await Task.Delay(1100);
            await _mqtt.publishZ("1");
        }
        catch (Exception ex)
        {
            Log.Error("Exeption: failed to swich Z-Pump on");
            Log.Error($"{ex.Message}");
        }
    }
    public async Task Off()
    {
        try
        {
            await _mqtt.publishZ("1");
            await Task.Delay(1100);
            _controller.Write(PumpPin, PinValue.Low);
            Log.Info("Z-PumpPin is off");
            await _mqtt.publishZ("0");
        }
        catch (Exception ex)
        {
            Log.Error("Exeption: failed to swich Z-Pump off");
            Log.Error($"{ex.Message}");
        }
    }

    DateTime _pumpStartTime;
    public async Task RunForSec(int sec)
    {
        int extraSec = 0;
        sec = sec < 3 ? 3 : sec;
        try
        {
            if(IsPumpOn)
            {

                extraSec = (int)(DateTime.Now - _pumpStartTime).TotalSeconds;
                while (IsPumpOn)
                {
                    await Task.Delay(500);
                }
            }
            else
            {
                _pumpStartTime = DateTime.Now;
                IsPumpOn = true;
                await On();
                await Task.Delay(sec * 1000 - 1100);
            }
            if (extraSec > 0)
            {
                Log.Info($"Add extra runtime for pump by {extraSec}");
                IsPumpOn = true;
                await On();
                await Task.Delay(extraSec * 1000);
            }
            await Off();
            IsPumpOn = false;
            return;
        }
        catch (Exception ex)
        {

            Log.Info($"Failed to run Z-Pump for {sec}sec");
            Log.Error($"{ex.Message}");
        }
    }

    public void KemoOn()
    {
        try
        {
            if (!_kemoIsOn)
            {
                _controller.Write(KemoPin, PinValue.High);
                _kemoIsOn = true;
                Log.Info("Kemo-Pin is on");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exeption: failed to swich Kemo-Pin on");
            Log.Error($"{ex.Message}");
        }
    }

    public void KemoOff()
    {
        try
        {
            if (_kemoIsOn)
            {
                _controller.Write(KemoPin, PinValue.Low);
                _kemoIsOn = false;
                Log.Info("Kemo-Pin is off");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exeption: failed to swich Kemo-Pin off");
            Log.Error($"{ex.Message}");
        }
    }
}

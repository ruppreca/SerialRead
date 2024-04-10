using GPIOControl;
using NLog;
using System;
using System.Device.Gpio;
using System.Threading;
using System.Threading.Tasks;

namespace GPIO_Control.Zpump;

internal class Zpump
{
    private static Logger Log = LogManager.GetCurrentClassLogger();
    private static GpioController _controller = new();
    private static Mqtt _mqtt;
    const int PumpPin = 24; //GPIO24 is pin 18 on RPi, Switches on the Solidstate Relais für pump

    public Zpump()
    {
        try
        {
            _controller.OpenPin(PumpPin, PinMode.Output);
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
            _mqtt.publishZ("0");
            _controller.Write(PumpPin, PinValue.High);
            Log.Info("Z-PumpPin is on");
            await Task.Delay(1100);
            _mqtt.publishZ("1");
        }
        catch (Exception ex)
        {
            Log.Error("Exeption failed to swich Z-Pump on");
            Log.Error($"{ex.Message}");
        }
    }
    public async Task Off()
    {
        try
        {
            _mqtt.publishZ("1");
            await Task.Delay(1100);
            _controller.Write(PumpPin, PinValue.Low);
            Log.Info("Z-PumpPin is off");
            _mqtt.publishZ("0");
        }
        catch (Exception ex)
        {
            Log.Error("Exeption failed to swich Z-Pump off");
            Log.Error($"{ex.Message}");
        }
    }

    public async Task RunForSec(int sec)
    {
        sec = sec < 3 ? 3 : sec;
        try
        {
            await On();
            await Task.Delay(sec * 1000 - 1100);
            await Off();
            return;
        }
        catch (Exception ex)
        {

            Log.Info($"Failed to run Z-Pump for {sec}sec");
            Log.Error($"{ex.Message}");
        }
    }
}

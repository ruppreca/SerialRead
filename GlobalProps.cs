using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPIO_Control;

public class GlobalProps
{
    // if true there was an Alarm. Do not run heater any more until restart.
    public bool FlanschHotAlarm { get; set; } = false;
}

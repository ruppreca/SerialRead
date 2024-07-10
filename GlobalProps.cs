﻿namespace SerialRead;

public class GlobalProps
{
    // if true there was an Alarm. Do not run heater any more until restart.
    public bool FlanschHotAlarm { get; set; } = false;

    public bool BatterieVoltageLow { get; set; } = false;
}

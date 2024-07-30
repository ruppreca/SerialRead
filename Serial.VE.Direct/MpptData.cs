using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SerialRead.Serial.VE.Direct
{
    internal class MpptData
    {
        public string Name { get; set; }
        public string Ser { get; set; }
        public double Vbatt_V { get; set; }
        public double Ibatt_A { get; set; }
        public int PowerPV_W { get; set; }
        public string State { get; set; }
        public bool HasError { get; set; }
        public string Err { get; set; }
        public bool LoadOn { get; set; }
        public double YieldToday { get; set; }
        public int MaxPowerToday { get; set; }
    }

    internal class ShuntData
    {
        public string Name { get; set; }
        public double Vbatt_V { get; set; } //V
        public double Ibatt_A { get; set; } //A
        public double SOC { get; set; } // % State of Charge
        public double DM { get; set; } // % Midpoint deviation
        public int Power_W { get; set; }
        public int TimeToGo_min { get; set; }
        public double Consumed_Ah { get; set; } // Consumed Amp Hours
        public bool HasError { get; set; }
        public string Err { get; set; }
    }
}

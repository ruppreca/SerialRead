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

    }
}

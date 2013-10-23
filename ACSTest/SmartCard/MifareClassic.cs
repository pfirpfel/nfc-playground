using PCSC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACSTest
{
    class MifareClassic : AbstractSmartCard
    {
        public override byte[] GetCardMemory(SCardReader reader)
        {
            return new byte[0];
        }
    }
}

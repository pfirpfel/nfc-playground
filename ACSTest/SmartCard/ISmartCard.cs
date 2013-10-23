using PCSC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACSTest
{
    public interface ISmartCard
    {
        byte[] GetCardMemory(SCardReader reader);
    }
}

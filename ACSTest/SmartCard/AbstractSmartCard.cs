using PCSC;
using PCSC.Iso7816;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACSTest
{
    abstract class AbstractSmartCard : ISmartCard
    {
        public abstract byte[] GetCardMemory(SCardReader reader);

        public byte[] GetUid(SCardReader reader)
        {
            SCardError sc = reader.BeginTransaction();

            var apdu = new CommandApdu(IsoCase.Case2Short, reader.ActiveProtocol)
            {
                CLA = 0xFF,
                Instruction = InstructionCode.GetData,
                P1 = 0x00,
                P2 = 0x00,
                Le = 0x00  
            };

            var responseApdu = SendAPDU(apdu, reader);

            reader.EndTransaction(SCardReaderDisposition.Leave);

            if (responseApdu != null && responseApdu.HasData)
            {
                return responseApdu.GetData();
            }
            else
            {
                return new byte[0];
            }
        }
        protected ResponseApdu SendAPDU(CommandApdu apdu, SCardReader reader)
        {
            var receivePci = new SCardPCI(); // IO returned protocol control information.
            var sendPci = SCardPCI.GetPci(reader.ActiveProtocol);

            var receiveBuffer = new byte[256];
            var command = apdu.ToArray();

            SCardError sc = reader.Transmit(
                sendPci,            // Protocol Control Information (T0, T1 or Raw)
                command,            // command APDU
                receivePci,         // returning Protocol Control Information
                ref receiveBuffer); // data buffer

            if (sc != SCardError.Success)
            {
                // todo
                return null;
            }

            return new ResponseApdu(receiveBuffer, IsoCase.Case2Short, reader.ActiveProtocol);
        }
    }
}

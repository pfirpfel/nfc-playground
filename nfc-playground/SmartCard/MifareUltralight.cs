using PCSC;
using PCSC.Iso7816;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACSTest
{
    class MifareUltralight : AbstractSmartCard
    {
        public override byte[] GetCardMemory(SCardReader reader)
        {
            SCardError sc = reader.BeginTransaction();
            if (sc != SCardError.Success)
            {
                //TODO
                return new byte[0];
            }
            byte[] memory = new byte[12 * 4];
            int offset = 0;
            for (byte i = 0x04; i < 0x10; i++)
            {
                byte[] page = getPage(i, reader);
                Array.Copy(page, 0, memory, offset, page.Length);
                offset += page.Length;
            }
            reader.EndTransaction(SCardReaderDisposition.Leave);
            return memory;
        }

        private byte[] getPage(byte page, SCardReader reader)
        {
            var apdu = new CommandApdu(IsoCase.Case2Short, reader.ActiveProtocol)
            {
                CLA = 0xFF,
                Instruction = InstructionCode.ReadBinary,
                P1 = 0x00,
                P2 = page, // block number
                Le = 0x04  // bytes to read
            };

            var responseApdu = SendAPDU(apdu, reader);

            if (responseApdu != null && responseApdu.HasData)
            {
                return responseApdu.GetData();
            }
            else
            {
                return new byte[0];
            }
        }

        public override string GetCardType()
        {
            return "Mifare Ultralight";
        }
    }
}

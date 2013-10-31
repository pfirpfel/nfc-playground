using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace nfc_playground
{
    class BuzzKiller
    {
        public static void DisableAllBuzzers()
        {
            int returnCode, context = 0;

            returnCode = ModWinsCard.SCardEstablishContext(ModWinsCard.SCARD_SCOPE_USER, 0, 0, ref context);
            if (returnCode != ModWinsCard.SCARD_S_SUCCESS) return;

            int pcchReaders = 0;
            returnCode = ModWinsCard.SCardListReaders(context, null, null, ref pcchReaders);
            if (returnCode != ModWinsCard.SCARD_S_SUCCESS) return;

            byte[] ReadersList = new byte[pcchReaders];
            returnCode = ModWinsCard.SCardListReaders(context, null, ReadersList, ref pcchReaders);
            if (returnCode != ModWinsCard.SCARD_S_SUCCESS) return;

            List<String> readerNames = new List<String>();

            String rName = "";
            int i = 0;

            //Convert reader buffer to string
            while (ReadersList[i] != 0)
            {
                while (ReadersList[i] != 0)
                {
                    rName += (char)ReadersList[i];
                    i++;
                }
                readerNames.Add(rName);
                rName = "";
                i++;
            }

            foreach (String readerName in readerNames)
            {

            }
        }
    }
}

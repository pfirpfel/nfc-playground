using PCSC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nfc_playground
{
    public interface ISmartCard
    {
        /// <summary>
        /// Returns the entire accessible memory of a card.
        /// </summary>
        /// <param name="reader">Reader the card is connected to</param>
        /// <returns>Card memory as byte array</returns>
        byte[] GetCardMemory(SCardReader reader);

        /// <summary>
        /// Returns the card's unique identifier.
        /// </summary>
        /// <param name="reader">Card UID as byte array</param>
        /// <returns></returns>
        byte[] GetUid(SCardReader reader);

        /// <summary>
        /// Returns the card type name.
        /// </summary>
        /// <returns>Card type</returns>
        String GetCardType();
    }
}

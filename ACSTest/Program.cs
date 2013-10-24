using NdefLibrary.Ndef;
using PCSC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACSTest
{
    class Program
    {

       
        

        static void Main(string[] args)
        {

            PCSCReader r = new PCSCReader();
            r.NewNdefMessageDetected += r_NewNdefMessageDetected;
            Console.ReadLine();
            r.NewNdefMessageDetected -= r_NewNdefMessageDetected;


        }

        static void r_NewNdefMessageDetected(NdefMessage message, int readerId)
        {
            Console.WriteLine("NdefMessage at reader {0}", readerId);
        }


    }
}

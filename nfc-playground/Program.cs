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
        static List<Answer> answers = new List<Answer>();
        
        static void Main(string[] args)
        {
            PCSCReader r = new PCSCReader();
            r.NewNdefMessageDetected += r_NewNdefMessageDetected;
            Console.WriteLine("Tag mit NDEF-Message auf Leser legen, um Buzzer zu initialisieren");
            Console.ReadLine();
            r.NewNdefMessageDetected -= r_NewNdefMessageDetected;
        }

        static void r_NewNdefMessageDetected(NdefMessage message, int readerId)
        {
            Answer a = answers.Find(an => an.ReaderID == readerId);
            if (a == null)
            {
                a = new Answer(readerId);
                answers.Add(a);
                Console.WriteLine("Buzzer '{0}' initialisiert", a.Label);
            }
            var textRecord = new NdefTextRecord(message.First());

            Console.WriteLine("Tag '{0}' an Buzzer '{1}'", textRecord.Text, a.Label);
        }


    }

    public class Answer
    {
        private static int i = 0;
        static String[] labels = new String[] { "Eins", "Zwei", "Drei", "Vier", "Fünf" };

        private String _label;
        public String Label
        {
            get { return _label; }
        }

        private int _readerId;
        public int ReaderID
        {
            get { return _readerId; }
        }

        public Answer(int readerId)
        {
            _readerId = readerId;
            _label = labels[i++];
        }
    }
}

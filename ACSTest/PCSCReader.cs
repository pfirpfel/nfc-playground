using NdefLibrary.Ndef;
using PCSC;
using PCSC.Iso7816;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACSTest
{
    public delegate void NewUidHandler(byte[] uid);
    class PCSCReader : IDisposable
    {
        private SCardContext context;
        private String[] readerNames;
        private int selectedReader = 0;
        private SCardReader reader;
        private ISmartCard card;

        /// <summary>
        /// Variables for card polling.
        /// </summary>
        private Thread poller;
        private CancellationTokenSource cancelSource;
        private const int pollerInterval = 200; // in milliseconds
        private byte[] lastMessage;

        public event NewUidHandler NewUidDetected;

        public PCSCReader()
        {
            context = new SCardContext();
            context.Establish(SCardScope.System);
            readerNames = context.GetReaders();

            // TODO move to connect function:
            reader = new SCardReader(context);

            //SCardError sc = reader.Connect(readerNames[selectedReader], SCardShareMode.Shared, SCardProtocol.Any);
            //if (sc != SCardError.Success)
            //{
            //    Console.WriteLine("Could not connect to reader {0}:\n{1}",
            //        readerNames[selectedReader],
            //        SCardHelper.StringifyError(sc));
            //    Console.ReadKey();
            //    return;
            //}
            //sc = reader.BeginTransaction();

            //NdefMessage msg = GetNdefMessage();
            
            //foreach (NdefRecord record in msg){
            //    var specializedType = record.CheckSpecializedType(false);
            //    if (specializedType == typeof(NdefTextRecord))
            //    {
            //        // Convert and extract Text record info
            //        var textRecord = new NdefTextRecord(record);
            //        Console.WriteLine("Text: " + textRecord.Text);
            //        Console.WriteLine("Language code: " + textRecord.LanguageCode);
            //        var textEncoding = (textRecord.TextEncoding == NdefTextRecord.TextEncodingType.Utf8 ? "UTF-8" : "UTF-16");
            //        Console.WriteLine("Encoding: " + textEncoding);
            //    }
            //}

            //reader.EndTransaction(SCardReaderDisposition.Leave);
            
        }

        private bool Connect()
        {
            SCardError sc = reader.Connect(readerNames[selectedReader], SCardShareMode.Shared, SCardProtocol.Any);
            if (sc != SCardError.Success) return false;
            byte[] atr = GetATR();
            if (atr.Length != 20) // is non-ISO14443A-3 card?
            {                
                Disconnect();
                return false;
            }
            switch (atr[14])
            {
                case 0x01:
                    card = new MifareClassic();
                    break;
                case 0x03:
                    card = new MifareUltralight();
                    break;
                default:
                    throw new NotImplementedException();
            }
            return true;
        }

        private void Disconnect()
        {
            reader.Disconnect(SCardReaderDisposition.Reset);
        }

        public byte[] GetMemory()
        {
            if (!Connect()) return new byte[0]; // TODO
            byte[] memory = card.GetCardMemory(reader);
            Disconnect();
            return memory;
        }

        public NdefMessage GetNdefMessage()
        {
            byte[] memory = GetMemory();

            byte[] message;
            byte startFlag = 0x03;
            int currentPosition = 0;
            bool readMessage = true;

            // look for data begin
            while (readMessage && currentPosition < memory.Length)
            {
                if (memory[currentPosition] == startFlag) readMessage = false;
                currentPosition++;
            }
            if (!readMessage)
            {
                int messageLength = 0;
                if (memory[currentPosition] == 0xFF) // flag for long message size
                {
                    messageLength += (int)memory[currentPosition] * 256;
                    currentPosition++;
                }
                messageLength += (int)memory[currentPosition];
                message = new byte[messageLength];

                currentPosition++; // go to message begin
                Buffer.BlockCopy(memory, currentPosition, message, 0, messageLength);

                if (memory[currentPosition + messageLength] != 0xFE) // check for message end
                    return null; // throw new Exception("NDEF Message end not found.");

                return NdefMessage.FromByteArray(message);
            }
            return null;
        }

        private byte[] GetATR(){
            SCardReaderState state = context.GetReaderStatus(readerNames[selectedReader]);
            
            return state.Atr ?? new byte[0];
        }

        public byte[] GetUID()
        {
            if (!Connect()) return new byte[0]; // TODO

            byte[] uid = card.GetUid(reader);
            Disconnect();

            return uid;
        }

        public String[] GetReaderNames()
        {
            return readerNames;
        }

        public void SetReader(int readerId)
        {
            selectedReader = readerId;
        }

        public void SetReader(String readerName)
        {
            for (int i = 0; i < readerNames.Length; i++)
            {
                if (readerNames[i].Equals(readerName))
                {
                    selectedReader = i;
                    break;
                }
            }
        }

        public void StartPolling()
        {
            cancelSource = new CancellationTokenSource();
            poller = new Thread(() => Poll(cancelSource.Token));
            poller.IsBackground = true;
            lastMessage = new byte[0];
            poller.Start();
        }

        /// <summary>
        /// Stops polling for visible cards on reader.
        /// </summary>
        public void StopPolling()
        {
            if (poller == null) return;
            cancelSource.Cancel();
        }

        /// <summary>
        /// Polls the reader for new
        /// </summary>
        /// <param name="cancelToken">token for stopping polling thread</param>
        private void Poll(CancellationToken cancelToken)
        {
            while (true)
            {
                cancelToken.ThrowIfCancellationRequested();
                try
                {
                    if (Connect())
                    {
                        byte[] uid = GetUID();
                        if (!uid.SequenceEqual(new byte[0]) && !uid.SequenceEqual(lastMessage)) // only update if new message with content
                        {
                            NewUidDetected(uid);
                            lastMessage = uid;
                        }
                        Disconnect();
                    }
                    else
                    {
                        // reset last message
                        lastMessage = new byte[0];
                    }
                }
                catch (Exception)
                { }
                Thread.Sleep(pollerInterval);
            }
        }


        
        //GetNDEFMessage
        //GetUID

        public void Dispose()
        {
            try
            {
                context.Release();
            }
            catch (Exception) { }
        }
    }
}

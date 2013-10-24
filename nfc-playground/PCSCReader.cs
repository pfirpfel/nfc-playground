using NdefLibrary.Ndef;
using PCSC;
using PCSC.Iso7816;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nfc_playground
{
    public delegate void NewNdefMessageHandler(NdefMessage message, int readerId);
    class PCSCReader : IDisposable
    {
        private SCardContext context;
        private String[] readerNames;
        private SCardReader reader;
        private ISmartCard card;

        private NewNdefMessageHandler _newNdefMessageDetected;
        private SCardMonitor monitor;
        private bool monitorRunning = false;
        private int listeners = 0;
        public event NewNdefMessageHandler NewNdefMessageDetected
        {
            add {
                listeners++;
                if (!monitorRunning)
                    startMonitor();
                _newNdefMessageDetected += value;
            }

            remove {
                _newNdefMessageDetected -= value;
                listeners--;
                if (listeners <= 0)
                {
                    stopMonitor();
                }
            }
        }

        public PCSCReader()
        {
            context = new SCardContext();
            context.Establish(SCardScope.System);
            readerNames = context.GetReaders();
            reader = new SCardReader(context);
        }

        private void startMonitor()
        {
            monitor = new SCardMonitor(new SCardContext(), SCardScope.System);
            monitor.Initialized += (sender, args) => ReaderInitializedEvent(args);
            monitor.CardInserted += (sender, args) => CardInsertedEvent(args);
            monitor.MonitorException += MonitorException;
            monitor.Start(readerNames);
            monitorRunning = true;
        }

        private void stopMonitor()
        {
            if (monitor != null)
                monitor.Dispose();
            monitorRunning = false;
        }

        private void ReaderInitializedEvent(CardStatusEventArgs args)
        {
            Debug.WriteLine("Reader {0} initialized", args.ReaderName);
        }

        private void CardInsertedEvent(CardStatusEventArgs args)
        {
            int readerId = -1;
            for (int i = 0; i < readerNames.Length; i++)
            {
                if(readerNames[i].Equals(args.ReaderName)){
                    readerId = i;
                    break;
                }
            }
            if(readerId < 0) return;

            NdefMessage message = GetNdefMessage(args.ReaderName);

            if(message != null){
                _newNdefMessageDetected(message, readerId);
            }
        }

        private void MonitorException(object sender, PCSCException ex)
        {
            Debug.WriteLine("Monitor exited due an error: {0}", SCardHelper.StringifyError(ex.SCardError));
        }

        private bool Connect(String readerName)
        {
            SCardError sc = reader.Connect(readerName, SCardShareMode.Shared, SCardProtocol.Any);
            if (sc != SCardError.Success) return false;
            byte[] atr = GetATR(readerName);
            if (atr.Length != 20) // is non-ISO14443A-3 card?
            {
                Debug.WriteLine("Can't connect to non-ISO14443A-3-cards.");
                Disconnect();
                return false;
            }
            switch (atr[14])
            {
                case 0x01:
                    card = new MifareClassic(MifareClassic.MemorySize.Classic1K);
                    break;
                case 0x02:
                    card = new MifareClassic(MifareClassic.MemorySize.Classic4K);
                    break;
                case 0x03:
                    card = new MifareUltralight();
                    break;
                case 0x26:
                    card = new MifareClassic(MifareClassic.MemorySize.ClassicMini);
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

        public byte[] GetMemory(String readerName)
        {
            byte[] memory = new byte[0];
            bool lockTaken = false;
            try
            {
                Monitor.Enter(reader, ref lockTaken);
                if (Connect(readerName))
                {
                    memory = card.GetCardMemory(reader);
                    Disconnect();
                }                
            }
            finally
            {
                if (lockTaken) Monitor.Exit(reader);
            }
            return memory;
        }

        public NdefMessage GetNdefMessage(String readerName)
        {
            byte[] memory = GetMemory(readerName);

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
                {
                    Debug.WriteLine("NDEF Message end not found.");
                    return null;
                }

                return NdefMessage.FromByteArray(message);
            }
            return null;
        }

        private byte[] GetATR(String readerName){
            SCardReaderState state = context.GetReaderStatus(readerName);
            return state.Atr ?? new byte[0];
        }

        public byte[] GetUID(String readerName)
        {
            byte[] uid = new byte[0];
            bool lockTaken = false;
            try
            {
                Monitor.Enter(reader, ref lockTaken);
                if (Connect(readerName))
                {
                    uid = card.GetUid(reader);
                    Disconnect();
                }
            }
            finally
            {
                if (lockTaken) Monitor.Exit(reader);
            }
            return uid;
        }

        public String[] GetReaderNames()
        {
            return readerNames;
        }

        public void Dispose()
        {
            try
            {
                stopMonitor();

                if (context != null)
                    context.Release();
            }
            catch (Exception) { }
        }
    }
}

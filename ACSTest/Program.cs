using NdefLibrary.Ndef;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACSTest
{
    class Program
    {
        public int retCode, hContext, hCard, Protocol;
        public bool connActive = false;
        public bool autoDet;
        public byte[] SendBuff = new byte[263];
        public byte[] RecvBuff = new byte[263];
        public int SendLen, RecvLen, nBytesRet, reqType, Aprotocol, dwProtocol, cbPciLength;
        public ModWinsCard.SCARD_READERSTATE RdrState;
        public ModWinsCard.SCARD_IO_REQUEST pioSendRequest;

        private List<String> readers = new List<String>();
        private String selectedReader;

        private byte[] cardMemory = new byte[15 * 3 * 16];

        public enum KeyType { A, B }
       

        static void Main(string[] args)
        {
            Program test = new Program();

            test.initReader();
            test.connectReader();
            test.loadAuthenticationKey();

            test.readCardMemory();

            NdefMessage msg = NdefMessage.FromByteArray(test.getNDEFMessage());

            foreach (NdefRecord record in msg)
            {
                Console.WriteLine("Record type: {0}, payload size {1}", Encoding.UTF8.GetString(record.Type, 0, record.Type.Length), record.Payload.Length);
                Console.WriteLine(Encoding.UTF8.GetString(record.Payload, 0, record.Payload.Length));
                
            }

            Console.ReadLine();

            test.closeConnection();
        }

        private void initReader()
        {
            
            int pcchReaders = 0;
            int indx;
            string rName = "";

            //Establish Context
            retCode = ModWinsCard.SCardEstablishContext(ModWinsCard.SCARD_SCOPE_USER, 0, 0, ref hContext);
            if (retCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new Exception("Could not establish context. " + ModWinsCard.GetScardErrMsg(retCode));

            // 2. List PC/SC card readers installed in the system
            retCode = ModWinsCard.SCardListReaders(this.hContext, null, null, ref pcchReaders);
            if (retCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new Exception("Could not list readers. " + ModWinsCard.GetScardErrMsg(retCode));

            byte[] ReadersList = new byte[pcchReaders];
            // Fill reader list
            retCode = ModWinsCard.SCardListReaders(this.hContext, null, ReadersList, ref pcchReaders);
            if (retCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new Exception("Could not list readers. " + ModWinsCard.GetScardErrMsg(retCode));

            rName = "";
            indx = 0;

            //Convert reader buffer to string
            while (ReadersList[indx] != 0){
                while (ReadersList[indx] != 0){
                    rName = rName + (char)ReadersList[indx];
                    indx++;
                }
                //Add reader name to list
                readers.Add(rName);
                rName = "";
                indx++;
            }
            
            if (readers.Count > 0)
                selectedReader = readers[0];

            Console.WriteLine("{0} readers found, '{1}' selcted", readers.Count, selectedReader);
        }

        private void connectReader()
        {
            retCode = ModWinsCard.SCardConnect(hContext, selectedReader, ModWinsCard.SCARD_SHARE_SHARED,
                                              ModWinsCard.SCARD_PROTOCOL_T0 | ModWinsCard.SCARD_PROTOCOL_T1, ref hCard, ref Protocol);
            if (retCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new Exception("Could not connect to reader. " + ModWinsCard.GetScardErrMsg(retCode));

            Console.WriteLine("Connected to {0}", selectedReader);
            connActive = true;
        }

        private void loadAuthenticationKey()
        {
            ClearBuffers();
            // Load Authentication Keys command
            SendBuff[0] = 0xFF;       // Class
            SendBuff[1] = 0x82;       // INS
            SendBuff[2] = 0x00;       // P1 : Key Structure
            SendBuff[3] = 0x00;       // P2 : Key Number
            SendBuff[4] = 0x06;       // P3 : Lc
            SendBuff[5] = 0xFF;       // Key 1 value
            SendBuff[6] = 0xFF;       // Key 2 value
            SendBuff[7] = 0xFF;       // Key 3 value
            SendBuff[8] = 0xFF;       // Key 4 value
            SendBuff[9] = 0xFF;       // Key 5 value
            SendBuff[10] = 0xFF;      // Key 6 value

            SendLen = 11;
            RecvLen = 2;

            retCode = SendAPDU();
            String tmpStr = "";

            if (retCode != ModWinsCard.SCARD_S_SUCCESS){
                return;
            }
            else {
                for (int indx = RecvLen - 2; indx <= RecvLen - 1; indx++){
                    tmpStr += " " + string.Format("{0:X2}", RecvBuff[indx]);
                }
                tmpStr = tmpStr.Trim();
            }
            if (tmpStr != "90 00"){
                Console.WriteLine("Load authentication key error!");
            } else {
                Console.WriteLine("Load authentication key succesfully loaded.");
            }
        }

        private void authenticateBlock(byte blockNumber, KeyType keyType, byte keyNumber)
        {
            ClearBuffers();

            SendBuff[0] = 0xFF;            // Class
            SendBuff[1] = 0x86;            // INS
            SendBuff[2] = 0x00;            // P1
            SendBuff[3] = 0x00;            // P2
            SendBuff[4] = 0x05;            // Lc
            SendBuff[5] = 0x01;            // Byte 1 : Version number
            SendBuff[6] = 0x00;            // Byte 2
            SendBuff[7] = blockNumber;     // Byte 3 : Block number
            if (keyType == KeyType.A){     // Key type (0x60 A, 0x61 B)
                SendBuff[8] = 0x60;
            } else {
                SendBuff[8] = 0x61;
            }
            SendBuff[9] = keyNumber;       // Key number

            SendLen = 10;
            RecvLen = 2;

            retCode = SendAPDU();

            String tmpStr = "";

            if (retCode != ModWinsCard.SCARD_S_SUCCESS){
                return;
            } else {
                for (int indx = 0; indx <= RecvLen - 1; indx++) {
                    tmpStr += " " + string.Format("{0:X2}", RecvBuff[indx]);
                }
                tmpStr = tmpStr.Trim();
            }
            if (tmpStr.Trim() == "90 00"){
                Console.WriteLine("Authentication success!");
            } else {
                Console.WriteLine("Authentication failed!");
            }
        }

        private byte[] readBinaryBlock(byte blockNumber, byte bytesToRead)
        {
            if (blockNumber > 0x40 || bytesToRead > 0x10) // Ranges for Mifare 1k
                throw new ArgumentOutOfRangeException();   

            ClearBuffers();
            SendBuff[0] = 0xFF;
            SendBuff[1] = 0xB0;
            SendBuff[2] = 0x00;
            SendBuff[3] = blockNumber;
            SendBuff[4] = bytesToRead;

            SendLen = 5;
            RecvLen = SendBuff[4] + 2;

            retCode = SendAPDU();

            String tmpStr = "";

            if (retCode != ModWinsCard.SCARD_S_SUCCESS) {
                throw new Exception("Reading binary block failed: " + ModWinsCard.GetScardErrMsg(retCode));  
            } else {
                for (int indx = RecvLen - 2; indx <= RecvLen - 1; indx++) {
                    tmpStr += " " + string.Format("{0:X2}", RecvBuff[indx]);
                }
                tmpStr = tmpStr.Trim();
                Console.WriteLine("Response rading binary block: {0}", tmpStr);
            }

            if (tmpStr == "90 00") {
                tmpStr = "";
                for (int indx = 0; indx <= RecvLen - 3; indx++){
                    tmpStr += Convert.ToChar(RecvBuff[indx]);
                }
                Console.WriteLine("Data: {0}", tmpStr);
            } else {
                Console.WriteLine("Read block error!");
            }

            return RecvBuff;
        }

        private byte[] getBlock(byte blockNumber)
        {
            authenticateBlock(blockNumber, KeyType.B, 0x00);
            return readBinaryBlock(blockNumber, 0x10).Take(16).ToArray<byte>(); // return only data
        }

        private void readCardMemory()
        {
            bool readMemory = true;
            int currentSector = 1;

            while (readMemory && currentSector < 16) // only 15 sectors accessible
            {
                bool readSector = true;
                int sectorOffset = currentSector * 4;
                int currentBlock = 0;
                while (readSector && currentBlock < 3)
                {
                    readSector = false;
                    byte[] block = getBlock((byte)(sectorOffset + currentBlock));
                    int i = 0;
                    while (!readSector && i < block.Length) { // check for data
                        if (block[i] != 0x00) readSector = true;
                        i++;
                    }
                    if (readSector) // update only if data
                        Buffer.BlockCopy(block, 0, cardMemory, 48 * (currentSector - 1) + (currentBlock * 16), 16);
                    currentBlock++;
                }
                readMemory = readSector;
                currentSector++;
            }
        }

        private byte[] getNDEFMessage()
        {
            byte[] message;
            byte startFlag = 0x03;
            int currentPosition = 0;
            bool readMessage = true;

            // look for data begin
            while (readMessage && currentPosition < cardMemory.Length)
            {
                if (cardMemory[currentPosition] == startFlag) readMessage = false;
                currentPosition++;
            }
            if (readMessage) return null; // nothing found

            int messageLength = 0;
            if (cardMemory[currentPosition] == 0xFF) // flag for long message size
            {
                currentPosition++;
                messageLength += (int)cardMemory[currentPosition] * 256;
            }
            currentPosition++;
            messageLength += (int)cardMemory[currentPosition];
            message = new byte[messageLength];

            currentPosition++; // go to message beginn
            Buffer.BlockCopy(cardMemory, currentPosition, message, 0, messageLength);

            if (cardMemory[currentPosition + messageLength] != 0xFE) // check for message end
                throw new Exception("Error during message parsing");

            return message;
        }

        private void ClearBuffers()
        {
            for (long i = 0; i <= 262; i++) {
                RecvBuff[i] = 0;
                SendBuff[i] = 0;
            }
        }

        private int SendAPDU()
        {
            int indx;
            string tmpStr;

            pioSendRequest.dwProtocol = Aprotocol;
            pioSendRequest.cbPciLength = 8;

            // Display Apdu In
            tmpStr = "";
            for (indx = 0; indx <= SendLen - 1; indx++){
                tmpStr += " " + string.Format("{0:X2}", SendBuff[indx]);
            }
            Console.WriteLine("Send APDU: {0}", tmpStr);

            retCode = ModWinsCard.SCardTransmit(hCard, ref pioSendRequest, ref SendBuff[0], SendLen, ref pioSendRequest, ref RecvBuff[0], ref RecvLen);

            if (retCode != ModWinsCard.SCARD_S_SUCCESS){
                throw new Exception("Command returned with error: " + ModWinsCard.GetScardErrMsg(retCode));
                //return retCode;
            }
            tmpStr = "";
            for (indx = 0; indx <= RecvLen - 1; indx++) {
                tmpStr += " " + string.Format("{0:X2}", RecvBuff[indx]);
            }
            Console.WriteLine("Response APDU: {0}", tmpStr);
            return retCode;
        }

        private void closeConnection() {
            retCode = ModWinsCard.SCardReleaseContext(hContext);
            retCode = ModWinsCard.SCardDisconnect(hCard, ModWinsCard.SCARD_UNPOWER_CARD);
        }
    }
}

using NdefLibrary.Ndef;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACSTest
{
    public delegate void NewNDEFMessageHandler(NdefMessage message);
    class ACSMifareReader
    {
        /// <summary>
        /// Variables for communication with NFC reader.
        /// </summary>
        private int readerContext, hCard, Protocol, SendLen, RecvLen, Aprotocol;
        private byte authentificatedBlock;
        private bool connectionActive = false;
        private byte[] SendBuff = new byte[263];
        private byte[] RecvBuff = new byte[263];
        private ModWinsCard.SCARD_READERSTATE RdrState;
        private ModWinsCard.SCARD_IO_REQUEST pioSendRequest;
        private List<String> readers;
        private String selectedReader;

        /// <summary>
        /// Memory content of read card.
        /// </summary>
        private byte[] cardMemory;

        /// <summary>
        /// Publishes read NDEF messages if polling is activated.
        /// </summary>
        public event NewNDEFMessageHandler NewNDEFMessage;

        /// <summary>
        /// Variables for card polling.
        /// </summary>
        private Thread poller;
        private CancellationTokenSource cancelSource;
        private const int pollerInterval = 200; // in milliseconds

        /// <summary>
        /// Raised when connection problems with reader and/or card occur.
        /// </summary>
        public class ConnectionException : Exception {
            public ConnectionException(string message) : base(message) { }
        }

        /// <summary>
        /// Raised when errors during card authentifiactio occur.
        /// </summary>
        public class AuthentificationException : Exception
        {
            public AuthentificationException(string message) : base(message) { }
        }

        /// <summary>
        /// Raised when content of card memory doesn't meet expectations.
        /// </summary>
        public class CardFormatException : Exception
        {
            public CardFormatException(string message) : base(message) { }
        }

        public class ReaderException : Exception
        {
            public ReaderException(string message) : base(message) { }
        }
        
        /// <summary>
        /// Mifare authentification key type.
        /// </summary>
        public enum KeyType { A, B }

        /// <summary>
        /// Initializes context and reader list.
        /// </summary>
        /// <exception cref="ConnectionException">If initialization of reader list fails.</exception>
        public void ACSMifareReader()
        {
            readers = new List<String>();
            cardMemory = new byte[15 * 3 * 16]; // TODO: init memory according to card type

            // Establish Context
            int contextRetCode = ModWinsCard.SCardEstablishContext(ModWinsCard.SCARD_SCOPE_USER, 0, 0, ref readerContext);
            if (contextRetCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new ConnectionException("Could not establish context: " + ModWinsCard.GetScardErrMsg(contextRetCode));

            // List PC/SC card readers installed in the system
            int pcchReaders = 0;
            int listReadersRetCode = ModWinsCard.SCardListReaders(this.readerContext, null, null, ref pcchReaders);
            if (listReadersRetCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new ConnectionException("Could not list readers: " + ModWinsCard.GetScardErrMsg(listReadersRetCode));

            byte[] ReadersList = new byte[pcchReaders];
            // Fill reader list
            listReadersRetCode = ModWinsCard.SCardListReaders(this.readerContext, null, ReadersList, ref pcchReaders);
            if (listReadersRetCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new ConnectionException("Could not list readers: " + ModWinsCard.GetScardErrMsg(listReadersRetCode));

            //Convert reader buffer to string
            String rName = "";
            int i = 0;
            while (ReadersList[i] != 0)
            {
                while (ReadersList[i] != 0)
                {
                    rName += (char)ReadersList[i];
                    i++;
                }
                //Add reader name to list
                readers.Add(rName);
                Debug.WriteLine("Reader found: " + rName);
                rName = "";
                i++;
            }
        }

        /// <summary>
        /// Connects to the first available reader.
        /// </summary>
        /// <exception cref="ConnectionException">When connection cannot be established.</exception>
        public void connect(){
            connect(0);
        }

        /// <summary>
        /// Connects to the reader with the given id.
        /// </summary>
        /// <param name="readerId">Id of the reader, starting with 0.</param>
        /// <exception cref="ConnectionException">When connection cannot be established.</exception>
        /// <exception cref="ArgumentOutOfRangeException  ">If an invalid id was given.</exception>
        public void connect(int readerId) {
            if (readers.Count <= readerId) throw new ArgumentOutOfRangeException("Invalid readerId.");

            int connectionRetCode = ModWinsCard.SCardConnect(readerContext, readers[readerId], ModWinsCard.SCARD_SHARE_SHARED,
                                              ModWinsCard.SCARD_PROTOCOL_T0 | ModWinsCard.SCARD_PROTOCOL_T1, ref hCard, ref Protocol);

            if (connectionRetCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new ConnectionException("Could not connect to reader. " + ModWinsCard.GetScardErrMsg(connectionRetCode));

            Debug.WriteLine("Connected to " + readers[readerId]);
            connectionActive = true;
        }

        /// <summary>
        /// Closes the connection to the reader.
        /// </summary>
        private void close()
        {
            int closeRetCode = ModWinsCard.SCardReleaseContext(readerContext);
            closeRetCode = ModWinsCard.SCardDisconnect(hCard, ModWinsCard.SCARD_UNPOWER_CARD);

            connectionActive = false;
            Debug.WriteLine("Connection closed");
        }

        /// <summary>
        /// Lists available readers, index of the reader inside the returned array
        /// equals the readerId for the connect function.
        /// </summary>
        /// <returns>List of available readers.</returns>
        public String[] listAvailableReaders()
        {
            return readers.ToArray<String>();
        }

        /// <summary>
        /// Loads authentification keys into the reader.
        /// </summary>
        /// <exception cref="ConnectionException">If connection to card dropped during loading.</exception>
        /// <exception cref="AuthentificationException">If loading of keys failed.</exception>
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

            int loadAuthenticationKeyRetCode = SendAPDU();

            if (loadAuthenticationKeyRetCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new ConnectionException("Connection problem while loading authentication key.");

            StringBuilder response = new StringBuilder();
            for (int i = RecvLen - 2; i <= RecvLen - 1; i++)
            {
                response.AppendFormat("{0:X2}", RecvBuff[i]);
            }
            if (response.ToString().Trim() != "90 00")
                throw new AuthentificationException("Error while loading authentication key.");
        }

        /// <summary>
        /// Authentificate block.
        /// </summary>
        /// <param name="blockNumber">Number/address of block</param>
        /// <param name="keyType">Key type used for authentification</param>
        /// <param name="keyNumber">Key number/location</param>
        /// <exception cref="ConnectionException">If connection to card dropped during authentificating.</exception>
        /// <exception cref="AuthentificationException">If authentificating of block failed.</exception>
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
            SendBuff[8] = (keyType == KeyType.A) ? (byte)0x60 : (byte)0x61; // Key type (0x60 A, 0x61 B)
            SendBuff[9] = keyNumber;       // Key number

            SendLen = 10;
            RecvLen = 2;

            int authenticateBlockRetCode = SendAPDU();

            if (authenticateBlockRetCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new ConnectionException("Connection problem while authentificating block.");

            StringBuilder response = new StringBuilder();
            for (int i = 0; i <= RecvLen - 1; i++)
            {
                response.AppendFormat("{0:X2}", RecvBuff[i]);
            }
            if (response.ToString().Trim() != "90 00")
                throw new AuthentificationException("Error while authentificating block.");
            authentificatedBlock = blockNumber;
        }

        /// <summary>
        /// Retrieve binary content of a block.
        /// </summary>
        /// <param name="blockNumber">Number/address of block</param>
        /// <param name="bytesToRead">Amount of bytes to read</param>
        /// <returns>Requested bytes</returns>
        /// <exception cref="AuthentificationException">When requested block is not authentificated</exception>
        /// <exception cref="ArgumentOutOfRangeException">If blockNumber invalid or bytesToRead longer than block size.</exception>
        /// <exception cref="ConnectionException">If connection to card dropped during reading.</exception>
        /// <exception cref="ReaderException">If read command was otherwise unsuccesfull.</exception>
        private byte[] readBinaryBlock(byte blockNumber, byte bytesToRead)
        {
            if (authentificatedBlock != blockNumber)
                throw new AuthentificationException("Requested block not authentificated.");
            if (blockNumber > 0x40 || bytesToRead > 0x10) // Todo Ranges for Mifare 1k
                throw new ArgumentOutOfRangeException();

            ClearBuffers();
            SendBuff[0] = 0xFF;
            SendBuff[1] = 0xB0;
            SendBuff[2] = 0x00;
            SendBuff[3] = blockNumber;
            SendBuff[4] = bytesToRead;

            SendLen = 5;
            RecvLen = SendBuff[4] + 2;

            int readBinaryBlockRetCode = SendAPDU();

            if (readBinaryBlockRetCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new ConnectionException("Connection problem while reading block.");

            StringBuilder response = new StringBuilder();
            for (int i = RecvLen - 2; i <= RecvLen - 1; i++)
            {
                response.AppendFormat("{0:X2}", RecvBuff[i]);
            }
            if (response.ToString().Trim() != "90 00")
                throw new ReaderException("Error while reading block.");

            return RecvBuff;
        }

        /// <summary>
        /// Get binary block data. Automatically authentificates.
        /// </summary>
        /// <param name="blockNumber"></param>
        /// <returns></returns>
        /// <exception cref="ConnectionException">If connection to card dropped during authentificating or reading.</exception>
        /// <exception cref="AuthentificationException">If authentificating or reading of block failed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If blockNumber invalid or bytesToRead longer than block size.</exception>
        /// <exception cref="ReaderException">If read command was unsuccesfull.</exception>
        private byte[] getBlock(byte blockNumber)
        {
            try {
                authenticateBlock(blockNumber, KeyType.B, 0x00); // todo: generic keys!
                return readBinaryBlock(blockNumber, 0x10).Take(16).ToArray<byte>(); // return only data
            }
            catch(Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Reads the entire accessible memory of a card.
        /// </summary>
        /// <exception cref="AuthentificationException">When requested block is not authentificated</exception>
        /// <exception cref="ArgumentOutOfRangeException">If blockNumber invalid or bytesToRead longer than block size.</exception>
        /// <exception cref="ConnectionException">If connection to card dropped during reading.</exception>
        private void readCardMemory()
        {
            // todo make generic
            bool readMemory = true;
            int currentSector = 1;

            while (readMemory && currentSector < 16) // only 15 sectors accessible TODO: generic
            {
                bool readSector = true;
                int sectorOffset = currentSector * 4;
                int currentBlock = 0;
                while (readSector && currentBlock < 3)
                {
                    readSector = false;
                    try {
                        byte[] block = getBlock((byte)(sectorOffset + currentBlock));
                        int i = 0;
                        while (!readSector && i < block.Length)
                        { // check for data
                            if (block[i] != 0x00) readSector = true;
                            i++;
                        }
                        if (readSector) // update only if data
                            Buffer.BlockCopy(block, 0, cardMemory, 48 * (currentSector - 1) + (currentBlock * 16), 16);
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                    currentBlock++;
                }
                readMemory = readSector;
                currentSector++;
            }
        }

        /// <summary>
        /// Exctracts NDEFMessage from already read memory.
        /// </summary>
        /// <returns>raw NDEFMessage</returns>
        /// <exception cref="CardFormatException">Content of card memory doesn't meet expectations</exception>
        public byte[] getNDEFMessage()
        {
            if (!connectionActive)
                throw new ConnectionException("No open connection.");

            readCardMemory();

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

            currentPosition++; // go to message begin
            Buffer.BlockCopy(cardMemory, currentPosition, message, 0, messageLength);

            if (cardMemory[currentPosition + messageLength] != 0xFE) // check for message end
                throw new CardFormatException("NDEF Message end not found.");

            return message;
        }

        /// <summary>
        /// Gets UID of a card.
        /// </summary>
        /// <returns>UID of card as byte array</returns>
        /// <exception cref="ConnectionException">If connection to card dropped during reading.</exception>
        /// <exception cref="ReaderException">If read command was otherwise unsuccesfull.</exception>
        public byte[] getUID()
        {
            if (!connectionActive)
                throw new ConnectionException("No open connection.");

            ClearBuffers();

            SendBuff[0] = 0xFF;            // Class
            SendBuff[1] = 0xCA;            // INS
            SendBuff[2] = 0x00;            // P1
            SendBuff[3] = 0x00;            // P2
            SendBuff[4] = 0x00;            // Le

            SendLen = 5;
            RecvLen = 6;

            int getUIDRetCode = SendAPDU();


            if (getUIDRetCode != ModWinsCard.SCARD_S_SUCCESS)
                throw new ConnectionException("Connection problem while reading UID.");

            // Get status
            StringBuilder status = new StringBuilder();
            for (int i = RecvLen - 2; i <= RecvLen - 1; i++)
            {
                status.AppendFormat("{0:X2}", RecvBuff[i]);
            }
            if (status.ToString().Trim() != "90 00")
                throw new ReaderException("Error while reading UID.");

            return RecvBuff.Take(4).ToArray();
        }

        /// <summary>
        /// Clears the send- and receive-buffer.
        /// </summary>
        private void ClearBuffers()
        {
            for (int i = 0; i <= 262; i++)
            {
                RecvBuff[i] = 0;
                SendBuff[i] = 0;
            }
        }

        /// <summary>
        /// Sends the APDU command.
        /// </summary>
        /// <returns>APPDU response code</returns>
        private int SendAPDU()
        {
            pioSendRequest.dwProtocol = Aprotocol;
            pioSendRequest.cbPciLength = 8;

            // Log Apdu In
            StringBuilder command = new StringBuilder();
            for (int i = 0; i <= SendLen - 1; i++)
            {
                command.AppendFormat("{0:X2}", SendBuff[i]);
            }
            Debug.WriteLine("Sending APDU: {0}", command.ToString());

            int APDURetCode = ModWinsCard.SCardTransmit(hCard, ref pioSendRequest, ref SendBuff[0], SendLen, ref pioSendRequest, ref RecvBuff[0], ref RecvLen);

            // Log Apdu Response
            StringBuilder response = new StringBuilder();
            for (int i = 0; i <= RecvLen - 1; i++)
            {
                response.AppendFormat("{0:X2}", RecvBuff[i]);
            }
            Debug.WriteLine("Response APDU: {0}", response.ToString());

            return APDURetCode;
        }

        /// <summary>
        /// Starts polling for visible cards on reader.
        /// </summary>
        public void startPolling()
        {
            if (!connectionActive)
                throw new ConnectionException("No open connection.");

            cancelSource = new CancellationTokenSource();
            poller = new Thread(() => poll(cancelSource.Token));
            poller.IsBackground = true;
            poller.Start();
        }

        /// <summary>
        /// Stops polling for visible cards on reader.
        /// </summary>
        public void stopPolling()
        {
            if (poller == null) return;
            cancelSource.Cancel();
        }

        private void poll(CancellationToken cancelToken)
        {
            while (true)
            {
                cancelToken.ThrowIfCancellationRequested();


                //todo lock object


                
                // todo
                //NewNDEFMessage(null);
                Thread.Sleep(pollerInterval);
            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;

namespace CottonwoodRfidReader
{
    /// <summary>
    /// The Cottonwood board is communicated with Serially through a 
    /// byte arrays that represent specific commands. The board also
    /// responds in byte arrays. We only use a few commands here.
    /// For a full list of available commands and expected byte array
    /// responses, please download the following PDF here: 
    /// https://s3.amazonaws.com/linksprite/cuttonwood/datasheet.pdf
    /// </summary>
    public class Cottonwood
    {
        private SerialDevice _rfidReader = null;

        //Cottonwood Command Arrays used in this Proof of Concept
        private byte[] CMD_TURN_ON_ANTENNA = new byte[] { 0x18, 0x03, 0xFF };
        private byte[] CMD_TURN_OFF_ANTENNA = new byte[] { 0x18, 0x03, 0x00 };
        private byte[] CMD_SET_US_FREQUENCY = new byte[] { 0x41, 0x08, 0x08,0x12,0x26, 0x0e, 0xd8, 0x01 };
        private byte[] CMD_INVENTORY_SCAN = new byte[] { 0x43, 0x03, 0x01 };

        /// <summary>
        /// Constructor configures the Serial settings for interfacing
        /// with the Cottonwood board
        /// </summary>
        /// <param name="device">Serial device object representing 
        ///  the Cottonwood board</param>
        public Cottonwood(SerialDevice device)
        {
            //configures the serial settings for interfacing with 
            //the Cottonwood board
            _rfidReader = device;
            _rfidReader.WriteTimeout = TimeSpan.FromMilliseconds(1000);
            _rfidReader.ReadTimeout = TimeSpan.FromMilliseconds(1000);
            _rfidReader.BaudRate = 9600;
            _rfidReader.Parity = SerialParity.None;
            _rfidReader.StopBits = SerialStopBitCount.One;
            _rfidReader.DataBits = 8;
        }

        /// <summary>
        /// Turns on antenna
        /// </summary>
        /// <returns>Is Successful?</returns>
        public async Task<bool> TurnOnAntenna()
        {
            return await AntennaPower(CMD_TURN_ON_ANTENNA);
        }

        /// <summary>
        /// Turns off antenna
        /// </summary>
        /// <returns>Is Successful?</returns>
        public async Task<bool> TurnOffAntenna()
        {
            return await AntennaPower(CMD_TURN_OFF_ANTENNA);
        }

        /// <summary>
        /// Turns off hop mode - scans only us frequencies
        /// </summary>
        /// <returns>Is Successful?</returns>
        public async Task<bool> ConfigureUnitedStatesFrequency()
        {
            bool retvalue = false;
            byte[] result = await SendCommand(CMD_SET_US_FREQUENCY);
            if (result != null)
            {
                //check for the expected result
                if (result.Length > 3
                           && result[0] == 0x42
                           && result[1] == 0x40
                           && result[2] == 0xFE
                           && result[3] == 0xFF)
                {
                    retvalue = true;
                }
            }
            return retvalue;
        }

        /// <summary>
        /// Performs an inventory scan
        /// </summary>
        /// <returns>list of Tag Ids</returns>
        public async Task<List<byte[]>> PerformInventoryScan()
        {
            List<byte[]> retvalue = new List<byte[]>();
            byte[] result = await SendCommand(CMD_INVENTORY_SCAN);
            //check for the expected result
            if (result != null)
            {
                if (result.Length > 3
                    && result[0] == 0x44
                   )
                {
                    //determine the number of tags read
                    int numTags = result[2];
                    if (numTags > 0)
                    {
                        //collect the id's of the tags read
                        int arrayIdx = 0;
                        for (int i = 0; i < numTags; i++)
                        {
                            //10 skip bytes (header of the frame)
                            arrayIdx += 10;

                            //12 byte Tag Id
                            byte[] tagid = new byte[12];
                            for (int j = 0; j < 12; j++)
                            {
                                tagid[j] = result[arrayIdx];
                                arrayIdx += 1;
                            }
                            retvalue.Add(tagid);
                        }
                    }
                }

            }
            return retvalue;
        }

        /// <summary>
        /// A method that sends antenna commands and parses the
        /// expected response from the Cottonwood
        /// </summary>
        /// <param name="command">command array sent to the Cottonwood</param>
        /// <returns>Is Successful?</returns>
        private async Task<bool> AntennaPower(byte[] command)
        {
            bool retvalue = false;
            byte[] result = await SendCommand(command);
            if (result != null)
            {
                if (result.Length == 3
                           && result[0] == 0x19
                           && result[1] == 0x03
                           && result[2] == 0x00)
                {
                    retvalue = true;
                }
            }
            return retvalue;
        }

        /// <summary>
        /// Serially writes a command byte array to the Cottonwood board
        /// </summary>
        /// <param name="command">command byte array</param>
        /// <returns>byte array response from the command obtained from
        /// the Cottonwood board
        /// </returns>
        private async Task<byte[]> SendCommand(byte[] command)
        {
            byte[] retvalue = null;
            //send command to the Cottonwood
            var writeResult = await Write(command);
            if (writeResult.IsSuccessful)
            {
                //get response from the Cottonwood
                var readResult = await Read();
                if (readResult.IsSuccessful)
                {
                    retvalue = readResult.Result;
                }
                else
                {
                    throw new Exception("Reader did not respond");
                }
            }
            else
            {
                throw new Exception("Could not write to the reader");
            }
            return retvalue;
        }
        
        /// <summary>
        /// internal encapsulation of the response read from the Cottonwood
        /// after issuing a command
        /// </summary>
        internal class RfidReaderResult
        {
            public bool IsSuccessful { get; set; }
            public byte[] Result { get; set; }
 
        }

        /// <summary>
        /// Serial read from the Cottonwood
        /// </summary>
        /// <returns>bytes read</returns>
        private async Task<RfidReaderResult> Read()
        {
            RfidReaderResult retvalue = new RfidReaderResult();
            var dataReader = new DataReader(_rfidReader.InputStream);
            try
            {
                //Awaiting Data from RFID Reader
                var numBytesRecvd = await dataReader.LoadAsync(1024);
                retvalue.Result = new byte[numBytesRecvd];
                if (numBytesRecvd > 0)
                {
                    //Data successfully read from RFID Reader"
                    dataReader.ReadBytes(retvalue.Result);
                    retvalue.IsSuccessful = true;
                }
            }
            catch (Exception ex)
            {
                retvalue.IsSuccessful = false;
                throw ex;
            }
            finally
            {
                if (dataReader != null)
                {
                    dataReader.DetachStream();
                    dataReader = null;
                }
            }
            return retvalue;
        }

        /// <summary>
        /// Serial write function to the Cottonwood
        /// </summary>
        /// <param name="writeBytes">byte array sent to the Cottonwood</param>
        /// <returns>bytes written and success indicator</returns>
        private async Task<RfidReaderResult> Write(byte[] writeBytes)
        {
            var dataWriter = new DataWriter(_rfidReader.OutputStream);
            RfidReaderResult retvalue = new RfidReaderResult();
            try
            {
                //send the message
                //Writing command to RFID Reader
                dataWriter.WriteBytes(writeBytes);
                await dataWriter.StoreAsync();
                retvalue.IsSuccessful = true;
                retvalue.Result = writeBytes;
                //Writing of command has been successful
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                if (dataWriter != null)
                {
                    dataWriter.DetachStream();
                    dataWriter = null;
                }
            }
            return retvalue;
        }
    }
}

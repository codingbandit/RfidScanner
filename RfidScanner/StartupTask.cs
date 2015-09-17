using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using Windows.ApplicationModel.Background;
using Windows.System.Threading;
using CottonwoodRfidReader;
using Windows.Devices.SerialCommunication;
using Windows.Devices.Enumeration;
using Windows.Networking.Connectivity;
using System.Threading.Tasks;
using System.Net.Http.Headers;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace RfidScanner
{
    public sealed class StartupTask : IBackgroundTask
    {
        private BackgroundTaskDeferral _deferral;
        private ThreadPoolTimer _timer;
        private string _uartBridgeName = "CP2102 USB to UART Bridge Controller";
        private Cottonwood _reader = null;
        private string _ipAddress = null;

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            //keeps this process alive in the OS
            _deferral = taskInstance.GetDeferral();

            if (SetDeviceIpv4Address())
            {
                bool isStartedUp = await ConfigureCottonwood();
                if (isStartedUp)
                {
                    //only kick off timer if everything gets configured properly
                    //reads will occur every 10 seconds
                    _timer = ThreadPoolTimer.CreatePeriodicTimer(Timer_Tick, TimeSpan.FromMilliseconds(10000));
                }
            }
        }

        private async void Timer_Tick(ThreadPoolTimer timer)
        {
            try
            {
                //perform inventory scan and read available RFID tags
                var tagInventory = await _reader.PerformInventoryScan();
                if(tagInventory.Count() > 0)
                {
                    //assemble readings in the expected structure
                    List<TrackerReadingModel> readings = new List<TrackerReadingModel>();
                    foreach(var tag in tagInventory)
                    {
                        TrackerReadingModel reading = new TrackerReadingModel();
                        reading.IpAddress = _ipAddress;
                        reading.TagId = BitConverter.ToString(tag);
                        reading.Reading = DateTime.Now;
                        readings.Add(reading);
                    }

                    //send reading data to the cloud service
                    using (var client = new HttpClient())
                    {
                        client.BaseAddress = new Uri("http://YOURBASEURL.COM/");
                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        var response = await client.PostAsJsonAsync("api/reading/add-multi-readings", readings);
                    }
                }
            }
            catch (Exception ex)
            {
                //TODO: Logging of exception
            }
        }

        /// <summary>
        /// internal encapsulation class of a reading
        /// this is the structure that the ASP.NET Web API
        /// service is expecting
        /// </summary>
        internal class TrackerReadingModel
        {
            public string IpAddress { get; set; }
            public string TagId { get; set; }
            public DateTime Reading { get; set; }
        }

        /// <summary>
        /// Obtains the IP address of the Raspberry Pi
        /// The IP is used to identify the Pi.
        /// </summary>
        /// <returns>Is Successful?</returns>
        private bool SetDeviceIpv4Address()
        {
            var hostInfo = NetworkInformation.GetHostNames().Where(x => x.Type == Windows.Networking.HostNameType.Ipv4).FirstOrDefault();
            if (hostInfo != null)
            {
                _ipAddress = hostInfo.RawName;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Retrieves the serial device, instantiates and configures
        /// the Cottonwood board. Also turns on the antenna so that
        /// the device is ready to perform inventory scans
        /// </summary>
        /// <returns></returns>
        private async Task<bool> ConfigureCottonwood()
        {
            //Retrieve serial device representing the Cottonwood board
            string deviceQuery = SerialDevice.GetDeviceSelector();
            var discovered = await DeviceInformation.FindAllAsync(deviceQuery);
            var readerInfo = discovered.Where(x => x.Name == _uartBridgeName).FirstOrDefault();
            if (readerInfo != null)
            {
                var bridgeDevice = await SerialDevice.FromIdAsync(readerInfo.Id);
                if (bridgeDevice != null)
                {
                    //instantiate the Cottonwood with the serial device
                    _reader = new Cottonwood(bridgeDevice);
                    bool isAntennaOn = await _reader.TurnOnAntenna();
                    if (isAntennaOn)
                    {
                        //set us frequency
                        var isUsFrequency = await _reader.ConfigureUnitedStatesFrequency();
                        if (isUsFrequency)
                        {
                            return true;
                        }
                    }
                }//end bridge device retrieved
            } //end serial device found
            return false;
        }
    }
}

using Microsoft.Extensions.Hosting;
using SharpPcap;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Management;
using System.Diagnostics;
using AbpLib;
using System.Net.Http;
using AbpLib.Web;
using System.ComponentModel;
using Microsoft.SqlServer.XEvent.XELite;
using System.Text;
using System.Text.RegularExpressions;
using AbpLib.Networking;

namespace Sys_Info_LocalSVC
{
    public class Worker : BackgroundService
    {
        private static bool LoggingIsPaused;
        private static int LoggingInterval = 1000;

        public static readonly bool IsLocal = Environment.MachineName == "IT-ANDREAS";
        public static readonly CaptureDeviceList devices;
        private static readonly ConcurrentBag<int> NetworkByteLength = new ConcurrentBag<int>();
        private static readonly ConcurrentBag<int> ApiCallBag = new ConcurrentBag<int>();

        static Worker()
        {
            devices = CaptureDeviceList.Instance;
            RegisterNetDevices();
            RegisterLogEvents(true);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            SysInfo();
            SqlEventStream("QueryLogging", IsLocal ? "DummyDB" : "MaxiToysDB");

            await RegisterTcpListener(6868);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }


        public static void SysInfo()
        {
            new Thread(async _ =>
            {

                double memPct = 0;
                ManagementObjectSearcher manageProcessor = new ManagementObjectSearcher("Select * from Win32_Processor");
                ManagementObjectSearcher manageRam = new ManagementObjectSearcher("select * from Win32_OperatingSystem");
                int processorCores = manageProcessor.Get().Cast<ManagementBaseObject>().Sum(item => int.Parse(item["NumberOfLogicalProcessors"].ToString()));

                PerformanceCounter[] pcounters = new PerformanceCounter[processorCores];
                for (int i = 0; i < processorCores; i++)
                {
                    pcounters[i] = new PerformanceCounter("Processor", "% Processor Time", i.ToString());
                }

                while (true)
                {
                    try
                    {
                        SysInfoLogModel sysInfo = new SysInfoLogModel
                        {
                            CreatedDateTime = DateTime.Now,
                            MeasureInterval = LoggingInterval
                        };

                        double freeMem = manageRam.Get().Cast<ManagementObject>().Select(m => double.Parse(m["FreePhysicalMemory"].ToString())).FirstOrDefault();
                        double visibleMem = manageRam.Get().Cast<ManagementObject>().Select(m => double.Parse(m["TotalVisibleMemorySize"].ToString())).FirstOrDefault();
                        memPct = visibleMem == 0 ? 0 : (visibleMem - freeMem) / visibleMem * 100;

                        for (int i = 0; i < pcounters.Length; i++)
                        {
                            PerformanceCounter p = pcounters[i];
                            sysInfo.CurrentProcessorUsage += p.NextValue().RoundUp();
                        }
                        sysInfo.CurrentProcessorUsage = (sysInfo.CurrentProcessorUsage / pcounters.Length).RoundUp();

                        sysInfo.CurrentMemoryUsage = memPct.RoundUp();

                        ManagementObject manageDisk = new ManagementObject("Win32_PerfFormattedData_PerfDisk_PhysicalDisk.Name='0 C:'");
                        Dictionary<string, object> d = manageDisk.Properties.Cast<PropertyData>().ToDictionary(p => p.Name, p => p.Value);

                        sysInfo.AvgKBPerDiskWrite = d.TryGetValue("AvgDiskBytesPerWrite", out object avgBytes) ? double.Parse(avgBytes.ToString()) : 0;
                        sysInfo.CurrentDiskUsage = d.TryGetValue("PercentDiskTime", out object diskPct) ? int.Parse(diskPct.ToString()) : 0;

                        sysInfo.CurrentNetworkUsage = NetworkByteLength.GetSumOfBag();
                        sysInfo.APICalls = ApiCallBag.GetSumOfBag();
                        bool canInsert = sysInfo.CurrentProcessorUsage <= 100 && sysInfo.CurrentMemoryUsage <= 100 && sysInfo.CurrentDiskUsage <= 100;

                        if (canInsert && sysInfo.CurrentProcessorUsage > 0 && !LoggingIsPaused)
                        {
                            Task task = sysInfo.LogSystemInfo();
                            await task;
                        }
                    }
                    catch (Exception err)
                    {
                        await err.LogErrors("Worker", "SysInfo");
                    }
                    await Task.Delay(LoggingInterval);
                }
            })
            { IsBackground = true }.Start();
        }

        public static void RegisterNetDevices()
        {
            for (int i = 0; i < devices.Count; i++)
            {
                ICaptureDevice device = devices[i];

                try
                {
                    device.OnPacketArrival += CaptureDevice_OnPacketArrival;
                    device.Open(DeviceMode.Promiscuous, 1000);
                    device.StartCapture();

                }
                catch (Exception err)
                {
                    err.LogErrors("Worker", "RegisterNetDevices").Wait();
                }
            }
        }

        private static void CaptureDevice_OnPacketArrival(object sender, CaptureEventArgs e)
        {
            try
            {
                int dataLength = e.Packet.Data.Length;
                NetworkByteLength.Add(dataLength);
            }
            catch (Exception err)
            {
                err.LogErrors("Worker", "CaptureDevice_OnPacketArrival").Wait();
            }
        }

        public static void RegisterLogEvents(bool resgister)
        {
            foreach (EventLog log in EventLog.GetEventLogs(Environment.MachineName))
            {
                log.EnableRaisingEvents = resgister;

                if (resgister) log.EntryWritten += On_EventWritten;
                else log.EntryWritten -= On_EventWritten;
            }
        }

        private static async void On_EventWritten(object sender, EntryWrittenEventArgs e)
        {
            try
            {
                SysEventLogModel m = new SysEventLogModel
                {
                    MachineName = e.Entry.MachineName,
                    Index = e.Entry.Index,
                    CategoryNumber = e.Entry.CategoryNumber,
                    EntryType = e.Entry.EntryType.ToString(),
                    InstanceId = e.Entry.InstanceId,
                    Message = e.Entry.Message,
                    Source = e.Entry.Source,
                    TimeGenerated = e.Entry.TimeGenerated
                };

                if (!LoggingIsPaused)
                {
                    if (e.Entry.EntryType == EventLogEntryType.Error && IsLocal)
                    {
                        Task task = m.LogSystemEventInfo();
                        await task;
                    }

                    if (!IsLocal)
                    {
                        Task task = m.LogSystemEventInfo();
                        await task;
                    }

                    await Task.Delay(LoggingInterval);
                }
            }
            catch (Exception err)
            {
                await err.LogErrors("Worker", "On_EventWritten");
            }
        }

        public static async Task RegisterTcpListener(int port)
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();
            TCP tcp = new TCP(port);
            tcp.PacketChanged += ON_PACKETRECEIVE;
            bool start = tcp.Start();
            string msg = start ? $"Tcp listener started on port {port}" : "Tcp listener failed to start";
            watch.Stop();
            await watch.LogTime("Worker", "RegisterTcpListener", msg);
        }

        private static void ON_PACKETRECEIVE(object sender, PropertyChangedEventArgs e)
        {
            KeyValuePair<string, object> command = sender.ToString().DESERIALIZE<KeyValuePair<string, object>>();
            switch (command.Key)
            {
                case "api_calls":
                    if (int.TryParse(command.Value.ToString(), out int count))
                    {
                        ApiCallBag.Add(count);
                    }
                    break;
                case "pause_logging":
                    LoggingIsPaused = Convert.ToBoolean(command.Value.ToString());
                    break;
                case "set_interval":
                    if (int.TryParse(command.Value.ToString(), out int newInterval) && newInterval >= 100)
                    {
                        LoggingInterval = newInterval;
                    }
                    break;
            }
        }

        public static void SqlEventStream(string SessionName, params string[] database)
        {
            new Thread(async _ =>
            {
                while (true)
                {
                    try
                    {
                        string cn = IsLocal ? "Data Source=IT-ANDREAS;Initial Catalog=master;Persist Security Info=True;User ID=sa;Password=password" : "Data Source=SQL_01;Initial Catalog=master;Persist Security Info=True;User ID=sa;Password=Password2020!";
                        XELiveEventStreamer stream = new XELiveEventStreamer(cn, SessionName);
                        await stream.ReadEventStream(async x =>
                        {
                            if (x.Actions.TryGetValue("database_name", out object db) && db.ToString().EqualsAny(database))
                            {
                                XEventModel m = new XEventModel()
                                {
                                    Statement = x?.Fields["statement"]?.ToString() ?? "",
                                    TimeStamp = x.Timestamp.DateTime,
                                    Duration = long.TryParse(x?.Fields["duration"]?.ToString(), out long time) ? time : 0,
                                    RowsAffected = long.TryParse(x?.Fields["row_count"]?.ToString(), out long rowsAffected) ? rowsAffected : 0
                                };
                                if (!LoggingIsPaused)
                                {
                                    Task task = m.LogQueries();
                                    await task;
                                }
                                await Task.Delay(LoggingInterval);
                            }
                        }, new CancellationToken());

                    }
                    catch (Exception err)
                    {
                        await err.LogErrors("Watcher", "SqlEventStream");
                    }
                }
            })
            { IsBackground = true }.Start();
        }
    }

    public static class SysEX
    {
        public static readonly HttpClient client;

        static SysEX()
        {
            Uri uri = Worker.IsLocal ? new Uri("http://localhost/api/") : new Uri("http://maxitoys/api/");
            client = new HttpClient() { BaseAddress = uri };
            client.DefaultRequestHeaders.Add("MaxiToysKey", "D82Km!k?xTT");
        }

        public static async Task LogErrors(this Exception err, string className, string method)
        {
            LogModel m = new LogModel
            {
                API = "Sys Info Local Service",
                Class = className,
                Method = method,
                Info = err.PropertyView(),
                CreatedDateTime = DateTime.Now,
                IsError = true,
                MS = 0.0D
            };

            await client.PostAsync("Log/InputLog", m.SERIALIZE_CONTENT());
        }

        public static async Task LogSystemInfo(this SysInfoLogModel m)
        {
            await client.PostAsync("Log/InputSysLog", m.SERIALIZE_CONTENT());
        }

        public static async Task LogSystemEventInfo(this SysEventLogModel m)
        {
            await client.PostAsync("Log/InputEventLog", m.SERIALIZE_CONTENT());
        }

        public static async Task LogTime(this Stopwatch watch, string className, string method, string extraData = "")
        {
            LogModel m = new LogModel
            {
                API = "Sys Info Local Service",
                Class = className,
                Method = method,
                Info = extraData,
                CreatedDateTime = DateTime.Now,
                IsError = false,
                MS = watch.Elapsed.TotalMilliseconds.RoundUp()
            };
            await client.PostAsync("Log/InputLog", m.SERIALIZE_CONTENT());
        }

        public static async Task LogJsonData(this JsonDataModel m)
        {
            await client.PostAsync("Log/LogJsonData", m.SERIALIZE_CONTENT());
        }

        public static async Task LogQueries(this XEventModel xm)
        {
            await client.PostAsync("Log/InputQueryLog", xm.SERIALIZE_CONTENT());
        }

        public static int GetSumOfBag(this ConcurrentBag<int> bag)
        {
            List<int> list = new List<int>();
            while (!bag.IsEmpty)
            {
                if (bag.TryTake(out int l))
                {
                    list.Add(l);
                }
            }
            return list.Sum();
        }
    }
}

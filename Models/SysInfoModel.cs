using AbpLib;
using System;

namespace Sys_Info_LocalSVC
{
    public class SysInfoLogModel
    {
        private double _currentNetworkUsage;
        private double _avgKBPerDiskWrite;

        public double CurrentProcessorUsage { get; set; }
        public double CurrentMemoryUsage { get; set; }
        public double CurrentNetworkUsage { get { return _currentNetworkUsage; } set { _currentNetworkUsage = value > 0 ? (value / 1024).RoundUp() : 0; } }
        public double AvgKBPerDiskWrite { get { return _avgKBPerDiskWrite; } set { _avgKBPerDiskWrite = value > 0 ? (value / 1024).RoundUp() : 0; } }
        public int CurrentDiskUsage { get; set; }
        public int APICalls { get; set; }
        public int MeasureInterval { get; set; }
        public DateTime CreatedDateTime { get; set; }
    }
}

using System;

namespace Sys_Info_LocalSVC
{
    public class SysEventLogModel
    {
        private string _message;

        public string MachineName { get; set; }
        public int Index { get; set; }
        public short CategoryNumber { get; set; }
        public string EntryType { get; set; }
        public long InstanceId { get; set; }
        public string Message { get { return _message; } set { _message = value.Length > 4000 ? value[0..4000] : value; } }
        public string Source { get; set; }
        public DateTime TimeGenerated { get; set; }
    }
}

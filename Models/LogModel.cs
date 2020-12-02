using System;

namespace Sys_Info_LocalSVC
{
    public class LogModel
    {
        public string API { get; set; }
        public string Class { get; set; }
        public string Method { get; set; }
        public double MS { get; set; }
        public string Info { get; set; }
        public bool IsError { get; set; }
        public DateTime CreatedDateTime { get; set; }
    }
}

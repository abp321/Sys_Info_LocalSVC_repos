using AbpLib;
using System;

namespace Sys_Info_LocalSVC
{
    public class XEventModel
    {
        public string Statement { get; set; }
        public DateTime TimeStamp { get; set; }
        public long Duration { get; set; }
        public long RowsAffected { get; set; }
    }
}

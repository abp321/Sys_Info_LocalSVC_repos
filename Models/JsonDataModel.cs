using System;

namespace Sys_Info_LocalSVC
{
    public class JsonDataModel
    {
        private string _jsonString;
        private string _deviceDesc;
        private string _capturedFrom;

        public DateTime CaptureDateTime { get; set; }
        public string CapturedFrom { get { return _capturedFrom; } set { _capturedFrom = value.Length > 255 ? value[0..255] : value; } }
        public string DeviceDesc { get { return _deviceDesc; } set { _deviceDesc = value.Length > 255 ? value[0..255] : value; } }
        public string JsonString { get { return _jsonString; } set { _jsonString = value.Length > 8000 ? value[0..8000] : value; } }
    }
}

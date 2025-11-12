using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1.Entiteiten
{
    public class DevicesData
    {
        private string name;
        private string os;
        private string osVer;
        private DateTime lastUpdate;
        private List<apps> AppsInfo;
        private string MAC;
        private string HWID;
        private string GUID;
        private List<string> WinGetLog;


        public string Name { get { return name; } set { name = value; } }
        public string Os { get { return os; } set { os = value; } }
        public string OsVer { get { return osVer; } set { osVer = value; } }
        public List<apps> appsInfo { get { return AppsInfo; } set { AppsInfo = value; } }
        public DateTime LastUpdate { get { return lastUpdate; } set { lastUpdate = value; } }
        public string mac { get { return  MAC; } set { MAC = value; } }
        public string hwid { get { return HWID; } set { HWID = value; } }
        public string guid { get { return GUID; } set { GUID = value; } }
        public List<string> winGetLog { get { return WinGetLog; } set { WinGetLog = value; } }

    }
}

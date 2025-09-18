using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1.Entiteiten
{
    public class apps
    {
        private string name;
        private string id;
        private string installedVersion;
        private bool isUpdateAvailable;
        private string availableVersions;

        public string Name { get { return name; } set { name = value; } }
        public string Id { get { return id; } set { id = value; } }
        public string InstalledVersion { get { return installedVersion; } set { installedVersion = value; } }
        public bool IsUpdateAvailable { get { return isUpdateAvailable; } set { isUpdateAvailable = value; } }
        public string AvailableVersions { get { return availableVersions; } set { availableVersions = value; } }

        public override string ToString()
        {
            return $"Name: {name}, Id: {id}, InstalledVersion: {installedVersion}, IsUpdateAvailable: {isUpdateAvailable}, AvailableVersions: {availableVersions}";
        }
    }
}

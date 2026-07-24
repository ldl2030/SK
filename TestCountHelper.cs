using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TestPlatform
{
    public static class TestCountHelper
    {
        private static string CountFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestCount.xml");

        public static int GetCount(string projectName)
        {
            if (string.IsNullOrEmpty(projectName)) return 0;
            if (!File.Exists(CountFilePath)) return 0;
            try
            {
                XDocument doc = XDocument.Load(CountFilePath);
                var elem = doc.Root.Elements("Project").FirstOrDefault(e => (string)e.Attribute("Name") == projectName);
                if (elem != null && int.TryParse(elem.Attribute("Count")?.Value, out int count))
                    return count;
            }
            catch { }
            return 0;
        }

        public static void IncrementCount(string projectName)
        {
            if (string.IsNullOrEmpty(projectName)) return;
            XDocument doc;
            if (!File.Exists(CountFilePath))
            {
                doc = new XDocument(new XElement("TestCounts"));
            }
            else
            {
                doc = XDocument.Load(CountFilePath);
            }
            var elem = doc.Root.Elements("Project").FirstOrDefault(e => (string)e.Attribute("Name") == projectName);
            if (elem == null)
            {
                elem = new XElement("Project", new XAttribute("Name", projectName), new XAttribute("Count", 0));
                doc.Root.Add(elem);
            }
            int current = int.Parse(elem.Attribute("Count").Value);
            current++;
            elem.Attribute("Count").Value = current.ToString();
            doc.Save(CountFilePath);
        }

        public static void ResetCount(string projectName)
        {
            if (!File.Exists(CountFilePath)) return;
            XDocument doc = XDocument.Load(CountFilePath);
            var elem = doc.Root.Element(projectName);
            if (elem != null)
            {
                elem.Value = "0";
                doc.Save(CountFilePath);
            }
        }
    }
}
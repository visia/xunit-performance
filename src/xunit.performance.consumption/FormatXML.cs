﻿using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Microsoft.Xunit.Performance.Consumption
{

    static class FormatXML
    {
        public static string formatXML(string XMLPath, string outPath = null)
        {
            string fullPath = Path.GetDirectoryName(XMLPath);
            string fileName = Path.GetFileName(XMLPath);
            outPath = outPath ?? (XMLPath != null ? Path.Combine(fullPath, "performanceTempResults - " + fileName) 
                                                  : Path.Combine(fullPath, "performanceTempResults.xml"));
            XElement mainFile = XElement.Load(XMLPath);
            List<testResult> testResults = parseXML(mainFile);
            XElement formattedResults = writeResults(testResults);
            FileStream outStream = new FileStream(outPath, FileMode.Create);
            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
            settings.CheckCharacters = false;
            settings.Indent = true;
            settings.IndentChars = "  ";
            using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(outStream, settings))
                formattedResults.Save(writer);
            return outPath;
        }

        static XElement writeResults(List<testResult> testResults)
        {
            XElement mainFile = new XElement("ScenarioResults");
            foreach(var test in testResults)
            {
                XElement scenarioResult = new XElement("ScenarioResult");
                mainFile.Add(scenarioResult);
                scenarioResult.Add(new XAttribute("Name", test.testName));
                XElement counterResults = new XElement("CounterResults");
                scenarioResult.Add(counterResults);
                foreach(var counter in test.metrics)
                {
                    XElement counterResult = new XElement("CounterResult", counter.value);
                    counterResults.Add(counterResult);

                    counterResult.Add(new XAttribute("Name", counter.displayName));
                    counterResult.Add(new XAttribute("Units", counter.unit));
                    if (counter.displayName == "Duration")
                        counterResult.Add(new XAttribute("Default", "true"));
                    else
                        counterResult.Add(new XAttribute("Default", "false"));
                    counterResult.Add(new XAttribute("Top", "true"));
                    counterResult.Add(new XAttribute("Iteration", counter.iteration));
                }
                foreach(var ListResult in test.ListResults)
                {
                    counterResults.Add(ListResult);
                }
            }
            return mainFile;
        }

        static List<testResult> parseXML(XElement mainFile)
        {
            List<testResult> ret = new List<testResult>();
            List<XElement> assembly = getElements("assembly", mainFile);
            List<XElement> collection = getElements("collection", assembly);
            List<XElement> test = getElements("test", collection);
            foreach (var testRun in test)
            {
                testResult testResult = new testResult();
                testResult.testName = testRun.Attribute("name").Value;
                testResult.timeElapsed = testRun.Attribute("time").Value;
                testResult.result = testRun.Attribute("result").Value;

                XElement performance = testRun.Element("performance");
                testResult.runID = performance.Attribute("runid").Value;
                testResult.etlPath = performance.Attribute("etl").Value;

                XElement metrics = performance.Element("metrics");
                XElement iterations = performance.Element("iterations");
                if(iterations == null)
                {
                    continue;
                }
                foreach(var iteration in iterations.Elements())
                {
                    string it = iteration.Attribute("index").Value;
                    if (it == "0")
                        continue;
                    foreach (var metric in getMetrics(metrics))
                    {
                        if (metric.unit != "list")
                        {
                            metric.value = iteration.Attribute(metric.name).Value;
                            metric.iteration = it;
                            testResult.metrics.Add(metric);
                        }
                        else
                        {
                            foreach (var ListResult in iteration.Elements("ListResult"))
                            {
                                if(ListResult.Attribute("Name").Value == metric.name)
                                {
                                    testResult.ListResults.Add(ListResult);
                                }
                            }
                        }
                    }
                }
                ret.Add(testResult);
            }
            return ret;
        }

        static List<XElement> getElements(string name, XElement parent)
        {
            List<XElement> ret = new List<XElement>();
            foreach(var element in parent.Elements(name))
            {
                ret.Add(element);
            }

            return ret;
        }

        static List<XElement> getElements(string name, List<XElement> parent)
        {
            List<XElement> ret = new List<XElement>();
            foreach (var xelements in parent)
            {
                foreach (var element in xelements.Elements(name))
                {
                    ret.Add(element);
                }
            }
            return ret;
        }

        static IEnumerable<Metric> getMetrics(XElement parent)
        {
            foreach(var metric in parent.Elements())
            {
                Metric ret = new Metric();
                ret.name = metric.Name.LocalName;
                ret.displayName = metric.Attribute("displayName").Value;
                ret.unit = metric.Attribute("unit").Value;
                yield return ret;
            }
        }

        class Metric
        {
            public string name { get; set; }
            public string displayName { get; set; }
            public string unit { get; set; }
            public string value { get; set; }
            public string iteration { get; set; }
        }

        class testResult
        {
            public string testName { get; set; }
            public string timeElapsed { get; set; }
            public string result { get; set; }
            public string runID { get; set; }
            public string etlPath { get; set; }
            public List<Metric> metrics = new List<Metric>();
            public List<XElement> ListResults = new List<XElement>();
        }
    }
}

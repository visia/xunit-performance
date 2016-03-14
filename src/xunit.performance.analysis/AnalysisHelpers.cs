using MathNet.Numerics;
using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Microsoft.Xunit.Performance.Analysis
{
    public static class AnalysisHelpers
    {
        private const double ErrorConfidence = 0.95; // TODO: make configurable

        /// <summary>
        /// The name of the Duration metric, as provided by the XML.
        /// </summary>
        private const string DurationMetricName = "Duration";

        /// <summary>
        /// Runs xunit.performance.analysis - does analysis of xunit.performance xml output files and compares it to a baseline
        /// </summary>
        /// <param name="xmlPaths">List of xml file paths to be analyzed (baseline must be included)</param>
        /// <param name="baseline">File path of baseline xml (optional)</param>
        /// <param name="xmlOutputPath">Outputs an xml to this path (optional) (incomplete analysis)</param>
        /// <param name="htmlOutputPath">Outputs an html to this path (optional)</param>
        /// <param name="csvOutputPath">Outputs a csv to this path (optional) (incomplete analysis)</param>
        public static void runAnalysis(List<string> xmlPaths, string baseline = null, string xmlOutputPath = null, string htmlOutputPath = null, string csvOutputPath = null)
        {
            var allComparisonIds = new List<Tuple<string, string>>();

            if (baseline != null)
            {
                foreach (var file in xmlPaths)
                {
                    allComparisonIds.Add(Tuple.Create(Path.GetFileNameWithoutExtension(baseline), Path.GetFileNameWithoutExtension(file)));
                }
                xmlPaths.Add(baseline);
            }

            var allIterations = ParseXmlFiles(xmlPaths, baseline);

            var testResults = SummarizeTestResults(allIterations);

            var comparisonResults = DoComparisons(allComparisonIds, testResults);

            if (xmlOutputPath != null)
                WriteTestResultsXml(testResults, comparisonResults).Save(fileNameToStream(xmlOutputPath));

            if (htmlOutputPath != null)
                WriteTestResultsHtml(testResults, comparisonResults, htmlOutputPath);

            if (csvOutputPath != null)
                WriteTestResultsCSV(testResults, csvOutputPath);
        }

        private static List<TestResultComparison> DoComparisons(List<Tuple<string, string>> allComparisonIds, Dictionary<string, Dictionary<string, TestResult>> testResults)
        {
            var comparisonResults = new List<TestResultComparison>();

            foreach (var comparisonIds in allComparisonIds)
            {
                var baseline = testResults[comparisonIds.Item1];
                var comparison = testResults[comparisonIds.Item2];

                foreach (var comparisonTest in comparison.Values)
                {
                    var baselineTest = baseline[comparisonTest.TestName];

                    var comparisonResult = new TestResultComparison();
                    comparisonResult.BaselineResult = baselineTest;
                    comparisonResult.ComparisonResult = comparisonTest;
                    comparisonResult.TestName = comparisonTest.TestName;
                    var MetricComparisons = new Dictionary<string, MetricComparison>();

                    foreach (var metric in comparisonTest.Iterations.First().MetricValues.Keys)
                    {
                        var metricComparison = new MetricComparison(comparisonResult, metric);
                        // Compute the standard error in the difference
                        var baselineCount = baselineTest.Iterations.Count;
                        var baselineSum = baselineTest.Iterations.Sum(iteration => iteration.MetricValues[metric]);
                        var baselineSumSquared = baselineSum * baselineSum;
                        var baselineSumOfSquares = baselineTest.Iterations.Sum(iteration => iteration.MetricValues[metric] * iteration.MetricValues[metric]);

                        var comparisonCount = comparisonTest.Iterations.Count;
                        var comparisonSum = comparisonTest.Iterations.Sum(iteration => iteration.MetricValues[metric]);
                        var comparisonSumSquared = comparisonSum * comparisonSum;
                        var comparisonSumOfSquares = comparisonTest.Iterations.Sum(iteration => iteration.MetricValues[metric] * iteration.MetricValues[metric]);

                        var stdErrorDiff = Math.Sqrt((baselineSumOfSquares - (baselineSumSquared / baselineCount) + comparisonSumOfSquares - (comparisonSumSquared / comparisonCount)) * (1.0 / baselineCount + 1.0 / comparisonCount) / (baselineCount + comparisonCount - 1));
                        var interval = stdErrorDiff * MathNet.Numerics.ExcelFunctions.TInv(1.0 - ErrorConfidence, baselineCount + comparisonCount - 2);

                        metricComparison.PercentChange = (comparisonTest.Stats[metric].Mean - baselineTest.Stats[metric].Mean) / baselineTest.Stats[metric].Mean;
                        metricComparison.PercentChangeError = interval / baselineTest.Stats[DurationMetricName].Mean;
                        MetricComparisons.Add(metric, metricComparison);
                    }

                    comparisonResult.MetricComparisons = MetricComparisons;
                    comparisonResults.Add(comparisonResult);
                }
            }

            return comparisonResults;
        }

        private static Dictionary<string, Dictionary<string, TestResult>> SummarizeTestResults(IEnumerable<TestIterationResult> allIterations)
        {
            var testResults = new Dictionary<string, Dictionary<string, TestResult>>();

            foreach (var iteration in allIterations)
            {
                Dictionary<string, TestResult> runResults;
                if (!testResults.TryGetValue(iteration.RunId, out runResults))
                    testResults[iteration.RunId] = runResults = new Dictionary<string, TestResult>();

                TestResult result;
                if (!runResults.TryGetValue(iteration.TestName, out result))
                {
                    runResults[iteration.TestName] = result = new TestResult();
                    result.RunId = iteration.RunId;
                    result.TestName = iteration.TestName;
                }

                foreach (var metric in iteration.MetricValues)
                {
                    RunningStatistics stats;
                    if (!result.Stats.TryGetValue(metric.Key, out stats))
                        result.Stats[metric.Key] = stats = new RunningStatistics();
                    stats.Push(metric.Value);
                }

                result.isBaseline = iteration.isBaseline;

                result.Iterations.Add(iteration);
            }

            return testResults;
        }

        private static XDocument WriteTestResultsXml(Dictionary<string, Dictionary<string, TestResult>> testResults, List<TestResultComparison> comparisonResults)
        {
            var resultElem = new XElement("results");
            var xmlDoc = new XDocument(resultElem);

            foreach (var run in testResults)
            {
                var runIdElem = new XElement("run", new XAttribute("id", run.Key));
                resultElem.Add(runIdElem);

                foreach (var result in run.Value.Values)
                {
                    var testElem = new XElement("test", new XAttribute("name", result.TestName));
                    runIdElem.Add(testElem);

                    var summaryElem = new XElement("summary");
                    testElem.Add(summaryElem);

                    foreach (var stat in result.Stats)
                    {
                        summaryElem.Add(new XElement(stat.Key,
                            new XAttribute("min", stat.Value.Minimum.ToString("G3")),
                            new XAttribute("mean", stat.Value.Mean.ToString("G3")),
                            new XAttribute("max", stat.Value.Maximum.ToString("G3")),
                            new XAttribute("marginOfError", stat.Value.MarginOfError(ErrorConfidence).ToString("G3")),
                            new XAttribute("stddev", stat.Value.StandardDeviation.ToString("G3"))));
                    }
                }
            }

            foreach (var comparison in comparisonResults)
            {
                var comparisonElem = new XElement("comparison", new XAttribute("test", comparison.TestName), new XAttribute("baselineId", comparison.BaselineResult.RunId), new XAttribute("comparisonId", comparison.ComparisonResult.RunId));
                resultElem.Add(comparisonElem);

                comparisonElem.Add(
                    new XElement("duration",
                        new XAttribute("changeRatio", comparison.MetricComparisons[DurationMetricName].PercentChange.ToString("G3")),
                        new XAttribute("changeRatioError", comparison.MetricComparisons[DurationMetricName].PercentChangeError.ToString("G3"))));
            }

            return xmlDoc;
        }

        private static Dictionary<string, int> findFailedTests(IGrouping<string, TestResultComparison> comparison, bool includeDuration = true)
        {
            var ret = new Dictionary<string, int>();
            foreach (var test in from c in comparison select c)
            {
                foreach (var metric in test.MetricComparisons.Keys)
                {
                    if (metric == DurationMetricName && !includeDuration)
                        continue;

                    if (test.MetricComparisons[metric].Passed == false)
                    {
                        if (!ret.ContainsKey(test.TestName))
                            ret.Add(test.TestName, 1);
                        else
                            ret[test.TestName]++;
                    }
                }
            }
            return ret;
        }

        private static void WriteTestResultsHtml(Dictionary<string, Dictionary<string, TestResult>> testResults, List<TestResultComparison> comparisonResults, string htmlOutputPath)
        {
            using (var writer = new StreamWriter(fileNameToStream(htmlOutputPath), Encoding.UTF8))
            {
                writer.WriteLine("<html><body>");

                foreach (var comparison in comparisonResults.GroupBy(r => $"Comparison: {r.ComparisonResult.RunId} | Baseline: {r.BaselineResult.RunId}"))
                {
                    writer.WriteLine($"<h1>{comparison.Key}</h1>");
                    writer.WriteLine("<table>");
                    int anchorNum = 0;

                    var failedTests = findFailedTests(comparison, false);
                    if (failedTests.Count > 0)
                    {
                        writer.WriteLine($"<tr><th><font  color=red>Failed Tests</font></th><th><font  color=red># Failed Metrics</font></th></tr>");
                        foreach (var test in failedTests)
                        {
                            writer.WriteLine($"<tr><td><a href=\"#anchor{anchorNum}\"><font  color=red>{test.Key}</font></a></td><td><font  color=red>{test.Value}</font></td></tr>");
                            anchorNum++;
                        }
                        writer.WriteLine("</table>");
                    }

                    writer.WriteLine("<table>");
                    writer.WriteLine($"<tr><th>Test</th><th>Metric</th><th>Baseline Mean</th><th>Comparison Mean</th><th>Percent Change</th><th>Error</th><th>DegradeBar</th></tr>");
                    anchorNum = 0;

                    foreach (var test in from c in comparison select c)
                    {
                        bool isFailedTest = failedTests.ContainsKey(test.TestName);
                        foreach (var metric in test.MetricComparisons.Keys)
                        {
                            var passed = test.MetricComparisons[metric].Passed;
                            string color;
                            string degradeBar = null;
                            if (test.BaselineResult.DegradeBars.ContainsKey(metric))
                            {
                                string append = null;
                                if (test.BaselineResult.DegradeBars[metric].metricDegradeBarType == MetricDegradeBarType.Percent)
                                    append = "%";
                                else if (test.BaselineResult.DegradeBars[metric].metricDegradeBarType == MetricDegradeBarType.Value)
                                    append = "#";
                                degradeBar = test.BaselineResult.DegradeBars[metric].metricDegradeBar.ToString() + append;
                                if (test.BaselineResult.DegradeBars[metric].metricDegradeBarType == MetricDegradeBarType.None)
                                    degradeBar = "None";
                            }
                            else
                            {
                                degradeBar = "None Specified";
                            }
                            if (!passed.HasValue)
                                color = "black";
                            else if (passed.Value)
                                color = "green";
                            else
                                color = "red";
                            if (metric == DurationMetricName)
                                if (isFailedTest)
                                {
                                    writer.WriteLine($"<tr><td><a name=\"anchor{anchorNum}\">{test.TestName}</a></td><td>{metric}</td><td>{test.MetricComparisons[metric].BaselineMean.ToString("F3")}</td><td>{test.MetricComparisons[metric].ComparisonMean.ToString("F3")}</td><td><font  color={color}>{test.MetricComparisons[metric].PercentChange.ToString("+##.#%;-##.#%")}</font></td><td>+/-{test.MetricComparisons[metric].PercentChangeError.ToString("P1")}</td><td>{degradeBar}</td></tr>");
                                    anchorNum++;
                                }
                                else
                                    writer.WriteLine($"<tr><td>{test.TestName}</td><td>{metric}</td><td>{test.MetricComparisons[metric].BaselineMean.ToString("F3")}</td><td>{test.MetricComparisons[metric].ComparisonMean.ToString("F3")}</td><td><font  color={color}>{test.MetricComparisons[metric].PercentChange.ToString("+##.#%;-##.#%")}</font></td><td>+/-{test.MetricComparisons[metric].PercentChangeError.ToString("P1")}</td><td>{degradeBar}</td></tr>");
                            else
                                writer.WriteLine($"<tr><td></td><td>{metric}</td><td>{test.MetricComparisons[metric].BaselineMean.ToString("F3")}</td><td>{test.MetricComparisons[metric].ComparisonMean.ToString("F3")}</td><td><font  color={color}>{test.MetricComparisons[metric].PercentChange.ToString("+##.#%;-##.#%")}</font></td><td>+/-{test.MetricComparisons[metric].PercentChangeError.ToString("P1")}</td><td>{degradeBar}</td></tr>");
                        }
                    }
                    writer.WriteLine("</table>");
                }

                writer.WriteLine("<hr>");

                foreach (var run in testResults.OrderBy(r => r.Value.First().Value.isBaseline))
                {
                    if (run.Value.First().Value.isBaseline)
                        writer.WriteLine($"<h1>Baseline results: {run.Key}</h1>");
                    else
                        writer.WriteLine($"<h1>Individual results: {run.Key}</h1>");

                    writer.WriteLine($"<table>");
                    writer.WriteLine($"<tr><th>Test</th><th>Metric</th><th>Unit</th><th>Min</th><th>Mean</th><th>Max</th><th>Margin</th><th>StdDev</th></tr>");
                    foreach (var test in run.Value)
                    {
                        foreach (var stat in test.Value.Stats)
                        {
                            if (stat.Key == DurationMetricName) // should always be first
                                writer.WriteLine($"<tr><td>{test.Value.TestName}</td><td>{DurationMetricName}</td><td>{test.Value.Iterations.First().MetricUnits[DurationMetricName]}</td><td>{stat.Value.Minimum.ToString("F3")}</td><td>{stat.Value.Mean.ToString("F3")}</td><td>{stat.Value.Maximum.ToString("F3")}</td><td>{stat.Value.MarginOfError(ErrorConfidence).ToString("P1")}</td><td>{stat.Value.StandardDeviation.ToString("F3")}</td></tr>");
                            else
                                writer.WriteLine($"<tr><td></td><td>{stat.Key}</td><td>{test.Value.Iterations.First().MetricUnits[stat.Key]}</td><td>{stat.Value.Minimum.ToString("F3")}</td><td>{stat.Value.Mean.ToString("F3")}</td><td>{stat.Value.Maximum.ToString("F3")}</td><td>{stat.Value.MarginOfError(ErrorConfidence).ToString("P1")}</td><td>{stat.Value.StandardDeviation.ToString("F3")}</td></tr>");
                        }
                    }
                    writer.WriteLine($"</table>");
                }

                writer.WriteLine("</html></body>");
            }
        }

        private static string EscapeCsvString(string str)
        {
            // Escape the csv string
            if (str.Contains("\""))
            {
                str = "\"" + str.Replace("\"", "\"\"") + "\"";
            }
            if (str.Contains(","))
            {
                str = string.Format("\"{0}\"", str);
            }
            return str;
        }

        private static void WriteTestResultsCSV(Dictionary<string, Dictionary<string, TestResult>> testResults, string csvOutputPath)
        {
            using (var writer = new StreamWriter(fileNameToStream(csvOutputPath)))
            {
                foreach (var run in testResults)
                {
                    foreach (var result in run.Value.Values)
                    {
                        foreach (var iteration in result.Iterations)
                        {
                            writer.WriteLine(String.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\"",
                                EscapeCsvString(iteration.RunId), EscapeCsvString(iteration.RunId), EscapeCsvString(iteration.TestName), iteration.MetricValues[DurationMetricName].ToString()));
                        }
                    }
                }
            }
        }

        internal static IEnumerable<string> ExpandFilePath(string path)
        {
            if (File.Exists(path))
            {
                yield return path;
            }
            else if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*.xml"))
                    yield return file;
            }
        }

        private class TestResult
        {
            public string TestName;
            public string RunId;
            public bool isBaseline = false;
            public Dictionary<string, RunningStatistics> Stats = new Dictionary<string, RunningStatistics>();
            public List<TestIterationResult> Iterations = new List<TestIterationResult>();
            public Dictionary<string, MetricDegradeBar> DegradeBars { get { return Iterations.FirstOrDefault().DegradeBars; } }
        }

        private class TestIterationResult
        {
            public string EtlPath;
            public string RunId;
            public string TestName;
            public int TestIteration;
            public bool isBaseline = false;
            public Dictionary<string, double> MetricValues = new Dictionary<string, double>();
            public Dictionary<string, string> MetricUnits = new Dictionary<string, string>();
            public Dictionary<string, MetricDegradeBar> DegradeBars;
        }

        private class TestResultComparison
        {
            public string TestName;
            public TestResult BaselineResult;
            public TestResult ComparisonResult;

            public Dictionary<string, MetricComparison> MetricComparisons;
        }

        private class MetricDegradeBar
        {
            public MetricDegradeBar(string metricName, string metricValue)
            {
                this.metricName = metricName;
                if (metricValue.EndsWith("#"))
                    this.metricDegradeBarType = MetricDegradeBarType.Value;
                else if (metricValue.EndsWith("%"))
                    this.metricDegradeBarType = MetricDegradeBarType.Percent;
                else if (metricValue == "None")
                    this.metricDegradeBarType = MetricDegradeBarType.None;
                else
                    throw new Exception($"Metric Degrade Bar for metric {metricName} must be None or end with # or %");
                if (!(metricDegradeBarType == MetricDegradeBarType.None) && !double.TryParse(metricValue.TrimEnd('#', '%'), out this.metricDegradeBar))
                    throw new Exception($"Could not parse {metricValue.TrimEnd('#', '%')} as a double.");
            }

            public string metricName;
            public double metricDegradeBar;
            public MetricDegradeBarType metricDegradeBarType;
        }

        public enum MetricDegradeBarType
        {
            Value = 0,
            Percent = 1,
            None = 2
        }

        private class MetricComparison
        {
            public MetricComparison(TestResultComparison parent, string metricName) { this.parent = parent; this.MetricName = metricName; }
            private TestResultComparison parent;
            public string MetricName;
            public IEnumerable<double> BaselineValues()
            {
                foreach (var iteration in parent.BaselineResult.Iterations)
                {
                    yield return iteration.MetricValues[MetricName];
                }
            }
            public IEnumerable<double> ComparisonValues()
            {
                foreach (var iteration in parent.ComparisonResult.Iterations)
                {
                    yield return iteration.MetricValues[MetricName];
                }
            }
            public double BaselineMean { get { return parent.BaselineResult.Stats[MetricName].Mean; } }
            public double ComparisonMean { get { return parent.ComparisonResult.Stats[MetricName].Mean; } }
            public double PercentChange;
            public double PercentChangeError;
            public double SortChange => (PercentChange > 0) ? Math.Max(PercentChange - PercentChangeError, 0) : Math.Min(PercentChange + PercentChangeError, 0);
            public bool? Passed
            {
                get
                {
                    if (!parent.BaselineResult.DegradeBars.ContainsKey(MetricName))
                    {
                        return PassedNoDegradeBar;
                    }
                    else
                    {
                        return PassedWithDegradeBar;
                    }
                }
            }

            private bool? PassedNoDegradeBar
            {
                get
                {
                    if (PercentChange > 0 && PercentChange > PercentChangeError)
                    {
                        if ((ComparisonMean - BaselineMean) < 1.0) // there's sometimes nondeterministic 0/1 behavior... ignore these
                            return null;
                        return false;
                    }
                    if (PercentChange < 0 && PercentChange < -PercentChangeError)
                    {
                        if ((BaselineMean - ComparisonMean ) < 1.0) // there's sometimes nondeterministic 0/1 behavior... ignore these
                            return null;
                        return true;
                    }
                    else
                        return null;
                }
            }

            private bool? PassedWithDegradeBar
            {
                get
                {
                    var degradeBar = parent.BaselineResult.DegradeBars[MetricName];
                    if (degradeBar.metricDegradeBarType == MetricDegradeBarType.Percent)
                    {
                        if (PercentChange > 0 && PercentChange * 100 > degradeBar.metricDegradeBar)
                        {
                            if ((ComparisonMean - BaselineMean) < 1.0) // there's sometimes nondeterministic 0/1 behavior... ignore these
                                return null;
                            return false;
                        }
                        else if (PercentChange < 0 && PercentChange * 100 < -degradeBar.metricDegradeBar)
                        {
                            if ((BaselineMean - ComparisonMean) < 1.0) // there's sometimes nondeterministic 0/1 behavior... ignore these
                                return null;
                            return true;
                        }
                        else
                            return null;
                    }
                    else if (degradeBar.metricDegradeBarType == MetricDegradeBarType.Value)
                    {
                        var difference = ComparisonMean - BaselineMean;
                        if (difference > 0 && difference > degradeBar.metricDegradeBar)
                            return false;
                        else if (difference < 0 && difference < -degradeBar.metricDegradeBar)
                            return true;
                        else
                            return null;
                    }
                    else if (degradeBar.metricDegradeBarType == MetricDegradeBarType.None)
                        return PassedNoDegradeBar;
                    else // no other degradebar types implemented
                        return null;
                }
            }
        }

        private static IEnumerable<TestIterationResult> ParseXmlFiles(IEnumerable<string> etlPaths, string baseline)
        {
            return
                from path in etlPaths.AsParallel()
                from result in ParseOneXmlFile(path, baseline)
                select result;
        }

        private static IEnumerable<TestIterationResult> ParseOneXmlFile(string path, string baseline)
        {
            Console.WriteLine($"Parsing {path}");

            var doc = XDocument.Load(path);

            foreach (var testElem in doc.Descendants("test"))
            {
                var testName = testElem.Attribute("name").Value;

                var perfElem = testElem.Element("performance");
                var runId = perfElem.Attribute("runid").Value;
                var etlPath = perfElem.Attribute("etl").Value;
                var degradeBars = new Dictionary<string, MetricDegradeBar>();
                foreach (var xmlDegradeBars in doc.Descendants("MetricDegradeBars"))
                {
                    foreach (var degradeBar in xmlDegradeBars.Descendants())
                    {
                        degradeBars[degradeBar.Name.ToString()] = new MetricDegradeBar(degradeBar.Name.ToString(), degradeBar.Attribute("degradeBar").Value.ToString());
                    }
                }

                foreach (var iteration in perfElem.Descendants("iteration"))
                {
                    var index = int.Parse(iteration.Attribute("index").Value);

                    if (index == 0)
                        continue; // ignore iteration 0

                    var result = new TestIterationResult();
                    result.TestName = testName;
                    result.TestIteration = index;
                    result.RunId = runId;
                    result.EtlPath = etlPath;
                    if (path == baseline)
                        result.isBaseline = true;

                    foreach (var metricAttr in iteration.Attributes().Where(a => a.Name != "index"))
                    {
                        var metricName = metricAttr.Name.LocalName;
                        var metricVal = double.Parse(metricAttr.Value);

                        result.MetricValues.Add(metricName, metricVal);
                    }

                    foreach (var metric in perfElem.Descendants("metrics").Descendants())
                    {
                        var metricName = metric.Name.LocalName;
                        var metricUnits = metric.Attribute("unit").Value;
                        result.MetricUnits.Add(metricName, metricUnits);
                    }

                    result.DegradeBars = degradeBars;

                    yield return result;
                }
            }
        }

        private static Stream fileNameToStream(string input)
        {
            return File.Open(input, FileMode.Create);
        }
    }

    public static class MathExtensions
    {
        /// <summary>
        /// Calculates a confidence interval as a percentage of the mean
        /// </summary>
        /// <remarks>
        /// This assumes a roughly normal distribution in the sample data.
        /// </remarks>
        /// <param name="stats">A <see cref="RunningStatistics"/> object pre-populated with the sample data.</param>
        /// <param name="confidence">The desired confidence in the resulting interval.</param>
        /// <returns>The confidence interval, as a percentage of the mean.</returns>
        public static double MarginOfError(this RunningStatistics stats, double confidence)
        {
            if (stats.Count < 2)
                return double.NaN;

            var stderr = stats.StandardDeviation / Math.Sqrt(stats.Count);
            var t = TInv(1.0 - confidence, (int)stats.Count - 1);
            var mean = stats.Mean;
            var interval = t * stderr;

            return interval / mean;
        }

        [ThreadStatic]
        private static Dictionary<double, Dictionary<int, double>> t_TInvCache = new Dictionary<double, Dictionary<int, double>>();

        private static double TInv(double probability, int degreesOfFreedom)
        {
            Dictionary<int, double> dofCache;
            if (!t_TInvCache.TryGetValue(probability, out dofCache))
                t_TInvCache[probability] = dofCache = new Dictionary<int, double>();
            double result;
            if (!dofCache.TryGetValue(degreesOfFreedom, out result))
                dofCache[degreesOfFreedom] = result = ExcelFunctions.TInv(probability, degreesOfFreedom);
            return result;
        }
    }
}

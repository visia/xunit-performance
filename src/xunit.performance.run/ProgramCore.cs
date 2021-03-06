﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Xunit.Performance.Sdk;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Xunit.Performance
{
    public abstract class ProgramCore
    {

        internal bool _nologo = false;
        private bool _verbose = false;

        protected abstract IPerformanceMetricLogger GetPerformanceMetricLogger(XunitPerformanceProject project);

        protected abstract string GetRuntimeVersion();

        public int Run(string[] args)
        {
            if (args.Length == 0 || args[0] == "-?")
            {
                PrintHeader();
                PrintUsage();
                return 1;
            }

            try
            {
                var project = ParseCommandLine(args);
                if (!_nologo)
                {
                    PrintHeader();
                }

                using (AssemblyHelper.SubscribeResolve())
                {
                    PrintIfVerbose($"Creating output directory: {project.OutputDir}");
                    if (!Directory.Exists(project.OutputDir))
                        Directory.CreateDirectory(project.OutputDir);

                    RunTests(project);
                }
            }
            catch (Exception ex)
            {
                Console.Error.Write("Error: ");
                ReportExceptionToStderr(ex);
            }

            return 0;
        }

        internal void RunTests(XunitPerformanceProject project)
        {
            string xmlPath = Path.Combine(project.OutputDir, project.OutputBaseFileName + ".xml");
            string scenarioRangesPath = Path.Combine(project.OutputDir, "ScenarioRanges.txt");
            string zipPath = Path.Combine(project.OutputDir, project.OutputBaseFileName + ".etl.zip");

            var commandLineArgs = new StringBuilder();

            if (!string.IsNullOrEmpty(project.RunnerHost))
            {
                commandLineArgs.Append("\"");
                commandLineArgs.Append(project.RunnerCommand);
                commandLineArgs.Append("\" ");
            }

            foreach (var assembly in project.Assemblies)
            {
                commandLineArgs.Append("\"");
                commandLineArgs.Append(assembly.AssemblyFilename);
                commandLineArgs.Append("\" ");
            }

            foreach (var testClass in project.Filters.IncludedClasses)
            {
                commandLineArgs.Append("-class ");
                commandLineArgs.Append(testClass);
                commandLineArgs.Append(" ");
            }

            foreach (var testMethod in project.Filters.IncludedMethods)
            {
                commandLineArgs.Append("-method ");
                commandLineArgs.Append(testMethod);
                commandLineArgs.Append(" ");
            }

            foreach (var trait in project.Filters.IncludedTraits)
            {
                foreach (var traitValue in trait.Value)
                {
                    commandLineArgs.Append("-trait \"");
                    commandLineArgs.Append(trait.Key);
                    commandLineArgs.Append("=");
                    commandLineArgs.Append(traitValue);
                    commandLineArgs.Append("\" ");
                }
            }

            foreach (var trait in project.Filters.ExcludedTraits)
            {
                foreach (var traitValue in trait.Value)
                {
                    commandLineArgs.Append("-notrait \"");
                    commandLineArgs.Append(trait.Key);
                    commandLineArgs.Append("=");
                    commandLineArgs.Append(traitValue);
                    commandLineArgs.Append("\" ");
                }
            }

            if (!string.IsNullOrEmpty(project.RunnerArgs))
            {
                commandLineArgs.Append(project.RunnerArgs);
                commandLineArgs.Append(" ");
            }

            commandLineArgs.Append("-xml \"");
            commandLineArgs.Append(xmlPath);
            commandLineArgs.Append("\"");

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = project.RunnerHost ?? project.RunnerCommand,
                Arguments = commandLineArgs.ToString(),
                UseShellExecute = false,
            };

            startInfo.Environment["XUNIT_PERFORMANCE_RUN_ID"] = project.RunId;
            startInfo.Environment["XUNIT_PERFORMANCE_MIN_ITERATION"] = RunConfiguration.XUNIT_PERFORMANCE_MAX_ITERATION.ToString();
            startInfo.Environment["XUNIT_PERFORMANCE_MAX_ITERATION"] = RunConfiguration.XUNIT_PERFORMANCE_MIN_ITERATION.ToString();
            startInfo.Environment["XUNIT_PERFORMANCE_MAX_TOTAL_MILLISECONDS"] = RunConfiguration.XUNIT_PERFORMANCE_MAX_TOTAL_MILLISECONDS.ToString();
            startInfo.Environment["COMPLUS_gcConcurrent"] = "0";
            startInfo.Environment["COMPLUS_gcServer"] = "0";

            if (project.UseLocalUser)
            {
                startInfo.Domain = project.runComputer;
                startInfo.UserName = project.runCredentialsUsername;
                startInfo.Password = project.runCredentialsPassword;
                startInfo.LoadUserProfile = true;
                foreach (var envvar in project.runEnvVars)
                {
                    startInfo.Environment[envvar.Key.ToString()] = envvar.Value.ToString();
                }
            }

            var logger = GetPerformanceMetricLogger(project);
            using (logger.StartLogging(startInfo))
            {
                PrintIfVerbose($@"Launching runner:
Runner:    {startInfo.FileName}
Arguments: {startInfo.Arguments}");

                try
                {
                    using (var proc = Process.Start(startInfo))
                    {
                        proc.EnableRaisingEvents = true;
                        proc.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Could not launch the test runner, {startInfo.FileName}", innerException: ex);
                }
            }

            List<ScenarioRange> ScenarioRanges;
            using (var evaluationContext = logger.GetReader())
            {
                ScenarioRanges = evaluationContext.GetScenarioRanges();
                var xmlDoc = XDocument.Load(xmlPath);
                foreach (var assembly in xmlDoc.Descendants("assembly")) // create MetricDegradeBars section
                {
                    var MetricDegradeBars = new XElement("MetricDegradeBars");
                    assembly.AddFirst(MetricDegradeBars);


                    foreach (var testElem in assembly.Descendants("test"))
                    {
                        var testName = testElem.Attribute("name").Value;

                        var perfElem = new XElement("performance", new XAttribute("runid", project.RunId), new XAttribute("etl", Path.GetFullPath(evaluationContext.LogPath)));
                        testElem.Add(perfElem);

                        var metrics = evaluationContext.GetMetrics(testName);
                        if (metrics != null)
                        {
                            var metricsElem = new XElement("metrics");
                            perfElem.Add(metricsElem);

                            foreach (var metric in metrics)
                            {
                                switch (metric.Unit)
                                {
                                    case PerformanceMetricUnits.ListCount:
                                        metricsElem.Add(new XElement(metric.Id, new XAttribute("displayName", metric.DisplayName), new XAttribute("unit", PerformanceMetricUnits.List)));
                                        metricsElem.Add(new XElement(metric.Id + "Count", new XAttribute("displayName", metric.DisplayName + " Count"), new XAttribute("unit", PerformanceMetricUnits.Count)));
                                        CreateMetricDegradeBars(MetricDegradeBars, metric.Id + "Count");
                                        break;
                                    case PerformanceMetricUnits.ListBytes:
                                        metricsElem.Add(new XElement(metric.Id, new XAttribute("displayName", metric.DisplayName), new XAttribute("unit", PerformanceMetricUnits.List)));
                                        metricsElem.Add(new XElement(metric.Id + "Bytes", new XAttribute("displayName", metric.DisplayName + " Bytes"), new XAttribute("unit", PerformanceMetricUnits.Bytes)));
                                        CreateMetricDegradeBars(MetricDegradeBars, metric.Id + "Bytes");
                                        break;
                                    case PerformanceMetricUnits.ListCountBytes:
                                        metricsElem.Add(new XElement(metric.Id, new XAttribute("displayName", metric.DisplayName), new XAttribute("unit", PerformanceMetricUnits.List)));
                                        metricsElem.Add(new XElement(metric.Id + "Bytes", new XAttribute("displayName", metric.DisplayName + " Bytes"), new XAttribute("unit", PerformanceMetricUnits.Bytes)));
                                        metricsElem.Add(new XElement(metric.Id + "Count", new XAttribute("displayName", metric.DisplayName + " Count"), new XAttribute("unit", PerformanceMetricUnits.Count)));
                                        CreateMetricDegradeBars(MetricDegradeBars, metric.Id + "Count");
                                        CreateMetricDegradeBars(MetricDegradeBars, metric.Id + "Bytes");
                                        break;
                                    default:
                                        metricsElem.Add(new XElement(metric.Id, new XAttribute("displayName", metric.DisplayName), new XAttribute("unit", metric.Unit)));
                                        if (metric.Unit != PerformanceMetricUnits.List)
                                            CreateMetricDegradeBars(MetricDegradeBars, metric.Id);
                                        break;
                                }
                            }
                        }

                        var iterations = evaluationContext.GetValues(testName);
                        if (iterations != null)
                        {
                            var iterationsElem = new XElement("iterations");
                            perfElem.Add(iterationsElem);

                            for (int i = 0; i < iterations.Count; i++)
                            {
                                var iteration = iterations[i];
                                if (iteration != null)
                                {
                                    var iterationElem = new XElement("iteration", new XAttribute("index", i));
                                    iterationsElem.Add(iterationElem);

                                    foreach (var value in iteration)
                                    {
                                        double result;
                                        if (double.TryParse(value.Value.ToString(), out result))
                                        { // result is a double, add it as an attribute
                                            iterationElem.Add(new XAttribute(value.Key, result.ToString("R")));
                                        }
                                        else // result is a list, add the list as a new element
                                        {
                                            ListMetricInfo listMetricInfo = (ListMetricInfo)value.Value;
                                            if (listMetricInfo.hasCount)
                                            {
                                                string metricName = value.Key + "Count";
                                                iterationElem.Add(new XAttribute(metricName, listMetricInfo.count.ToString()));
                                            }
                                            if (listMetricInfo.hasBytes)
                                            {
                                                string metricName = value.Key + "Bytes";
                                                iterationElem.Add(new XAttribute(metricName, listMetricInfo.bytes.ToString()));
                                            }
                                            var listResult = new XElement("ListResult");
                                            listResult.Add(new XAttribute("Name", value.Key));
                                            listResult.Add(new XAttribute("Iteration", i));
                                            iterationElem.Add(listResult);
                                            foreach (ListMetricInfo.Metrics listMetric in listMetricInfo.MetricList)
                                            {
                                                var ListMetric = new XElement("ListMetric");
                                                ListMetric.Add(new XAttribute("Name", listMetric.Name));
                                                ListMetric.Add(new XAttribute("Unit", listMetric.Unit));
                                                ListMetric.Add(new XAttribute("Type", listMetric.Type.Name));
                                                listResult.Add(ListMetric);
                                            }
                                            foreach (var listItem in listMetricInfo.Items.OrderByDescending(key => key.Value.Size))
                                            {
                                                var ListItem = new XElement("ListItem");
                                                ListItem.Add(new XAttribute("Name", listItem.Key));
                                                ListItem.Add(new XAttribute("Size", listItem.Value.Size));
                                                ListItem.Add(new XAttribute("Count", listItem.Value.Count));
                                                listResult.Add(ListItem);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Create xunit results: runID.xml
                using (var xmlFile = File.Create(xmlPath))
                {
                    System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
                    settings.CheckCharacters = false;
                    settings.Indent = true;
                    settings.IndentChars = "  ";
                    using (System.Xml.XmlWriter writer = System.Xml.XmlWriter.Create(xmlFile, settings))
                        xmlDoc.Save(writer);
                }
                
                // Create ScenarioRanges.txt
                using (var scenarioRangesFile = new StreamWriter(File.Create(scenarioRangesPath)))
                {
                    scenarioRangesFile.WriteLine("ScenarioName,Scenario Start (in ms),Scenario Stop (in ms),PerfView start/stop range (for copy/paste)");
                    foreach(var scenarioRange in ScenarioRanges)
                    {
                        scenarioRangesFile.WriteLine($"{scenarioRange.ScenarioName},{scenarioRange.ScenarioStartTime},{scenarioRange.ScenarioStopTime},{scenarioRange.ScenarioStartTime} {scenarioRange.ScenarioStopTime}");
                    }
                }

                // Create PerformanceTempResults - runID.xml
                string tempResultsPath = Consumption.FormatXML.formatXML(xmlPath);

                // Create PerformanceAnalysisResults - runID.html
                List<string> xmlPaths = new List<string>();
                xmlPaths.Add(xmlPath);
                string analysisPath = Path.Combine(project.OutputDir, "performanceAnalysisResults - " + project.OutputBaseFileName + ".html");
                Analysis.AnalysisHelpers.runAnalysis(xmlPaths, project.baselineXML, htmlOutputPath: analysisPath);
                var failedTests = Analysis.AnalysisHelpers.getFailedTests(analysisPath);
                if(failedTests.Count != 0)
                {
                    string trailingS = failedTests.Count == 1 ? string.Empty : "s";
                    Console.WriteLine($"Performance regressions were found in the following {failedTests.Count} test{trailingS}:");
                    foreach(var test in failedTests)
                    {
                        Console.WriteLine($"  {test}");
                    }
                }

                // Prepare ngen symbols for zipping
                string srcNGENPath = Path.Combine(project.OutputDir, project.OutputBaseFileName + ".etl" + ".NGENPDB");
                string destNGENPath = Path.Combine(project.OutputDir, "symbols");
                if (Directory.Exists(destNGENPath))
                    Directory.Delete(destNGENPath, recursive:true);
                if (Directory.Exists(srcNGENPath))
                    Directory.Move(srcNGENPath, destNGENPath);

                // Zip files to runID.etl.zip
                string[] filesToZip = new string[] 
                {
                    xmlPath,
                    scenarioRangesPath,
                    tempResultsPath,
                    analysisPath,
                    project.EtlPath
                };

                foreach(var file in filesToZip)
                {
                    Stopwatch sw = new Stopwatch(); // poor man's Thread.Sleep()
                    sw.Start();
                    while(IsFileLocked(file))
                    {
                        for(int i=0; ; i++)
                        {
                            if(i % 500 == 0)
                            {
                                sw.Stop();
                                if (sw.ElapsedMilliseconds > 100)
                                    break;
                                else
                                    sw.Start();
                            }
                        }
                    }
                }

                // Zipping does not work for xunit.performance.run.core. Replaced with noops
                ZipperWrapper zipper = new ZipperWrapper(zipPath, filesToZip);
                if (Directory.Exists(destNGENPath))
                    zipper.QueueAddFileOrDir(destNGENPath);
                zipper.CloseZipFile();
                File.Delete(project.EtlPath);
                File.Delete(scenarioRangesPath);
            }
        }

        internal XunitPerformanceProject ParseCommandLine(string[] args)
        {
            var arguments = new Stack<string>();
            for (var i = args.Length - 1; i >= 0; i--)
                arguments.Push(args[i]);

            var assemblies = new List<Tuple<string, string>>();

            while (arguments.Count > 0)
            {
                if (arguments.Peek().StartsWith("-", StringComparison.Ordinal))
                    break;

                var assemblyFile = arguments.Pop();

                if (IsConfigFile(assemblyFile))
                    throw new ArgumentException($"expecting assembly, got config file: {assemblyFile}");
                if (!File.Exists(assemblyFile))
                    throw new ArgumentException($"file not found: {assemblyFile}");

                string configFile = null;
                if (arguments.Count > 0)
                {
                    var value = arguments.Peek();
                    if (!value.StartsWith("-", StringComparison.Ordinal) && IsConfigFile(value))
                    {
                        configFile = arguments.Pop();
                        if (!File.Exists(configFile))
                            throw new ArgumentException($"config file not found: {configFile}");
                    }
                }

                assemblies.Add(Tuple.Create(assemblyFile, configFile));
            }

            var project = GetProjectFile(assemblies);

            while (arguments.Count > 0)
            {
                var option = PopOption(arguments);
                var optionName = option.Key.ToLowerInvariant();

                if (!optionName.StartsWith("-", StringComparison.Ordinal))
                    throw new ArgumentException($"unknown command line option: {option.Key}");

                optionName = optionName.Substring(1);

                switch (optionName)
                {
                    case "nologo":
                        _nologo = true;
                        break;

                    case "verbose":
                        _verbose = true;
                        break;

                    case "trait":
                        {
                            if (option.Value == null)
                                throw new ArgumentException("missing argument for -trait");

                            var pieces = option.Value.Split('=');
                            if (pieces.Length != 2 || string.IsNullOrEmpty(pieces[0]) || string.IsNullOrEmpty(pieces[1]))
                                throw new ArgumentException("incorrect argument format for -trait (should be \"name=value\")");

                            var name = pieces[0];
                            var value = pieces[1];
                            project.Filters.IncludedTraits.Add(name, value);
                        }
                        break;

                    case "notrait":
                        {
                            if (option.Value == null)
                                throw new ArgumentException("missing argument for -notrait");

                            var pieces = option.Value.Split('=');
                            if (pieces.Length != 2 || string.IsNullOrEmpty(pieces[0]) || string.IsNullOrEmpty(pieces[1]))
                                throw new ArgumentException("incorrect argument format for -notrait (should be \"name=value\")");

                            var name = pieces[0];
                            var value = pieces[1];
                            project.Filters.ExcludedTraits.Add(name, value);
                        }
                        break;

                    case "class":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -class");

                        project.Filters.IncludedClasses.Add(option.Value);
                        break;

                    case "method":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -method");

                        project.Filters.IncludedMethods.Add(option.Value);
                        break;

                    case "runnerhost":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -runnerhost");

                        project.RunnerHost = option.Value;
                        break;

                    case "runner":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -runner");

                        project.RunnerCommand = option.Value;
                        if (Directory.Exists(project.RunnerCommand))
                            project.RunnerCommand = Path.Combine(project.RunnerCommand, "xunit.console.exe");
                        break;

                    case "runnerargs":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -runnerargs");

                        project.RunnerArgs = option.Value;
                        break;

                    case "runid":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -runid");

                        if (option.Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                            throw new ArgumentException($"runname contains invalid characters.", optionName);

                        project.RunId = option.Value;
                        break;

                    case "outdir":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -outdir");

                        if (option.Value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                            throw new ArgumentException($"outdir contains invalid characters.", optionName);

                        project.OutputDir = option.Value;
                        break;

                    case "outfile":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -outfile");

                        if (option.Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                            throw new ArgumentException("outfile contains invalid characters.", optionName);

                        project.OutputBaseFileName = option.Value;
                        break;

                    case "baseline":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -baseline");

                        if (!File.Exists(option.Value))
                            throw new ArgumentException($"baseline file {option.Value} could not be found.");

                        project.baselineXML = option.Value;
                        break;

                    case "stacksenabled":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -stacksenabled");
                        bool stacksEnabled;
                        if (bool.TryParse(option.Value, out stacksEnabled))
                            RunConfiguration._stacksEnabled = stacksEnabled;
                        else
                            throw new ArgumentException("invalid argument for -stacksenabled");
                        break;

                    case "maxiterations":
                        if(option.Value == null)
                            throw new ArgumentException("missing argument for -maxiterations");
                        int maxIterations;
                        if (int.TryParse(option.Value, out maxIterations))
                            RunConfiguration._maxIterations = maxIterations;
                        else
                            throw new ArgumentException("invalid argument for -maxiterations");
                        break;

                    case "miniterations":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -miniterations");
                        int minIterations;
                        if (int.TryParse(option.Value, out minIterations))
                            RunConfiguration._minIterations = minIterations;
                        else
                            throw new ArgumentException("invalid argument for -miniterations");
                        break;

                    case "maxtotalmilliseconds":
                        if (option.Value == null)
                            throw new ArgumentException("missing argument for -maxtotalmilliseconds");
                        int maxTotalMilliseconds;
                        if (int.TryParse(option.Value, out maxTotalMilliseconds))
                            RunConfiguration._maxTotalMilliseconds = maxTotalMilliseconds;
                        else
                            throw new ArgumentException("invalid argument for -maxtotalmilliseconds");
                        break;

                    default:
                        if (option.Value == null)
                            throw new ArgumentException($"missing filename for {option.Key}");

                        project.Output.Add(optionName, option.Value);
                        break;
                }
            }

            return project;
        }

        private static void CreateMetricDegradeBars(XElement MetricDegradeBars, string metricName, string degradeBarValue = "None")
        {
            if (MetricDegradeBars.Element(metricName) == null)
                MetricDegradeBars.Add(new XElement(metricName, new XAttribute("degradeBar", "None")));
        }

        private static bool IsConfigFile(string fileName)
        {
            return fileName.EndsWith(".config", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static XunitPerformanceProject GetProjectFile(List<Tuple<string, string>> assemblies)
        {
            var result = new XunitPerformanceProject();

            foreach (var assembly in assemblies)
                result.Add(new XunitProjectAssembly
                {
                    AssemblyFilename = Path.GetFullPath(assembly.Item1),
                    ConfigFilename = assembly.Item2 != null ? Path.GetFullPath(assembly.Item2) : null,
                });

            return result;
        }

        private static KeyValuePair<string, string> PopOption(Stack<string> arguments)
        {
            var option = arguments.Pop();
            string value = null;

            if (option.Equals("-runnerargs", StringComparison.OrdinalIgnoreCase))
            {
                // Special case, just grab all of the args to pass along.
                if (arguments.Count > 0)
                {
                    value = arguments.Pop();
                }
            }

            if (arguments.Count > 0 && !arguments.Peek().StartsWith("-", StringComparison.Ordinal))
                value = arguments.Pop();

            return new KeyValuePair<string, string>(option, value);
        }

        private static void GuardNoOptionValue(KeyValuePair<string, string> option)
        {
            if (option.Value != null)
                throw new ArgumentException($"error: unknown command line option: {option.Value}");
        }

        private static void ReportException(Exception ex, TextWriter writer)
        {
            for (; ex != null; ex = ex.InnerException)
            {
                writer.WriteLine(ex.Message);
                writer.WriteLine(ex.StackTrace);
            }
        }

        private static bool IsFileLocked(string fileName)
        {
            FileInfo file = new FileInfo(fileName);
            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                if (stream != null)
                    stream.Dispose();
            }
            
            return false;
        }

        internal static void ReportExceptionToStderr(Exception ex)
        {
            ReportException(ex, Console.Error);
        }

        internal void PrintHeader()
        {
            Console.WriteLine($"xunit.performance Console Runner ({IntPtr.Size * 8}-bit .NET {GetRuntimeVersion()})");
            Console.WriteLine("Copyright (C) 2015 Microsoft Corporation.");
            Console.WriteLine();
        }

        public void PrintIfVerbose(string message)
        {
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }

        internal static void PrintUsage()
        {
            Console.WriteLine($@"usage: xunit.performance.run <assemblyFile> [options]

Valid options:
  -nologo                         : do not show the copyright message
  -maxiterations ""value""          : max number of iterations to run each test
                                  : counts from 0, defaults to {RunConfiguration.XUNIT_PERFORMANCE_MAX_ITERATION}
  -mixiterations ""value""          : min number of iterations to run each test
                                  : counts from 0, defaults to {RunConfiguration.XUNIT_PERFORMANCE_MIN_ITERATION}
  -maxtotalmilliseconds ""value""   : max number of ms to run each test
                                  : 0 for no max, defaults to {RunConfiguration.XUNIT_PERFORMANCE_MAX_TOTAL_MILLISECONDS}
  -trait ""name = value""           : only run tests with matching name/value traits
                                  : if specified more than once, acts as an OR operation
  -notrait ""name=value""           : do not run tests with matching name/value traits
                                  : if specified more than once, acts as an AND operation
  -class ""name""                   : run all methods in a given test class (should be fully
                                  : specified; i.e., 'MyNamespace.MyClass')
                                  : if specified more than once, acts as an OR operation
  -method ""name""                  : run a given test method (should be fully specified;
                                  : i.e., 'MyNamespace.MyClass.MyTestMethod')
  -runnerhost ""name""              : use the given CLR host to launch the runner program.
  -runner ""name""                  : use the specified runner to excecute tests. Defaults
                                  : to xunit.console.exe
  -runnerargs ""args""              : append the given args to the end of the xunit runner's command-line
                                  : a quoted group of arguments, 
                                  : e.g. -runnerargs ""-verbose -nologo -parallel none""
  -runid ""name""                   : a run identifier used to create unique output filenames.
  -outdir ""name""                  : folder for output files.
  -outfile ""name""                 : base file name (without extension) for output files.
                                  : Defaults to the same value as runid.
  -stacksenabled ""true/false""     : enables stacks for PerfView investigations
                                  : (adds significant overhead)
  -verbose                        : verbose logging
");
        }
    }
}

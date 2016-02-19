// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MathNet.Numerics.Statistics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Runtime.Serialization;

namespace Microsoft.Xunit.Performance.Analysis
{
    internal class Program
    {
        private static int Usage()
        {
            Console.Error.WriteLine(
                "usage: xunit.performance.analysis <xmlPaths> [-baseline \"baselineXmlPath\"]  [-xml <output.xml>] [-html <output.html>]");
            return 1;
        }

        private static int Main(string[] args)
        {
            var xmlPaths = new List<string>();
            var allComparisonIds = new List<Tuple<string, string>>();
            var xmlOutputPath = (string)null;
            var htmlOutputPath = (string)null;
            var csvOutputPath = (string)null;
            string baseline = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("-") || args[i].StartsWith("/"))
                {
                    string switchName = args[i].Substring(1).ToLowerInvariant();
                    switch (switchName)
                    {
                        case "baseline":
                            if (++i >= args.Length)
                                return Usage();
                            bool foundFile = false;
                            foreach (var file in AnalysisHelpers.ExpandFilePath(args[i]))
                            {
                                if (file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                                {
                                    foundFile = true;
                                    xmlPaths.Add(file);
                                    baseline = file;
                                }
                                else
                                {
                                    Console.Error.WriteLine($"{file}' is not a .xml file.");
                                    return 1;
                                }
                            }
                            if (!foundFile)
                            {
                                Console.Error.WriteLine($"The path '{args[i]}' could not be found.");
                                return 1;
                            }
                            break;

                        case "xml":
                            if (++i >= args.Length)
                                return Usage();
                            xmlOutputPath = args[i];
                            break;

                        case "html":
                            if (++i >= args.Length)
                                return Usage();
                            htmlOutputPath = args[i];
                            break;

                        case "csv":
                            if (++i >= args.Length)
                                return Usage();
                            csvOutputPath = args[i];
                            break;

                        default:
                            return Usage();
                    }
                }
                else
                {
                    bool foundFile = false;
                    foreach (var file in AnalysisHelpers.ExpandFilePath(args[i]))
                    {
                        if (file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        {
                            foundFile = true;
                            xmlPaths.Add(file);
                        }
                        else
                        {
                            Console.Error.WriteLine($"{file}' is not a .xml file.");
                            return 1;
                        }
                    }
                    if (!foundFile)
                    {
                        Console.Error.WriteLine($"The path '{args[i]}' could not be found.");
                        return 1;
                    }
                }
            }

            if (xmlPaths.Count == 0)
                return Usage();

            AnalysisHelpers.runAnalysis(xmlPaths, baseline, xmlOutputPath, htmlOutputPath, csvOutputPath);

            return 0;
        }

        
    }
}

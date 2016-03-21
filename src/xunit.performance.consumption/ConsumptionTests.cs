using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Xunit.Performance.Consumption;
using Microsoft.Xunit.Performance;

namespace FunctionalTests
{
    /*[MeasureGCCounts]
    [MeasureGCAllocations]
    [MeasureExceptions]
    [MeasureFileIORead]
    [MeasureFileIOWrite]
    [MeasureDiskIORead]
    [MeasureDiskIOWrite]
    [MeasureAppDomainLoad]
    [MeasureAppDomainUnLoad]
    [MeasureAssemblyLoad]
    [MeasureAssemblyUnload]
    [MeasureModuleLoad]
    [MeasureModuleUnload]
    [MeasureFilesRead]
    [MeasureObjectsAllocated]
    [MeasureDllsLoaded]*/
    [MeasureDefaultMetrics]
    public static class ConsumptionTests
    {
        [Fact]
        public static void consumptionTest()
        {
            /*FormatXML.formatXML(@"E:\xunit.performance-maddog\TestResults\VSP55378-4218672\Microsoft.VisualStudio.Telemetry.Tests.dll\2016-03-21-09-01-30.xml",
                @"E:\xunit.performance-maddog\TestResults\VSP55378-4218672\Microsoft.VisualStudio.Telemetry.Tests.dll\perfresults - 2016-03-21-09-01-30.xml");*/
        }

        [Benchmark]
        [InlineData(10)]
        //[InlineData(100)]
        public static void myPerfTest(int iterations)
        {
            foreach (var iteration in Benchmark.Iterations)
            {
                using (iteration.StartMeasurement())
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        using (System.IO.StreamReader a = new System.IO.StreamReader(@"D:\PerfUnitTest\log2.txt"))
                        {
                            a.Read();
                        }
                    }
                }
            }
        }

    }
}

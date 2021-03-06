﻿using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Xunit.Performance.Sdk;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace Microsoft.Xunit.Performance
{
    internal class FileIOWriteMetricDiscoverer : IPerformanceMetricDiscoverer
    {
        public IEnumerable<PerformanceMetricInfo> GetMetrics(IAttributeInfo metricAttribute)
        {
            yield return new FileIOWriteMetric();
        }

        private class FileIOWriteMetric : PerformanceMetric
        {
            public FileIOWriteMetric()
                : base("FileWrite", "File Write", PerformanceMetricUnits.ListCountBytes)
            {
            }

            public override IEnumerable<ProviderInfo> ProviderInfo
            {
                get
                {
                    yield return new UserProviderInfo()
                    {
                        ProviderGuid = KernelTraceEventParser.ProviderGuid,
                        Level = TraceEventLevel.Verbose,
                        Keywords = (ulong)KernelTraceEventParser.Keywords.FileIO
                                 | (ulong)KernelTraceEventParser.Keywords.FileIOInit
                    };
                }
            }

            public override PerformanceMetricEvaluator CreateEvaluator(PerformanceMetricEvaluationContext context)
            {
                return new FileIOWriteEvaluator(context);
            }
        }

        private class FileIOWriteEvaluator : PerformanceMetricEvaluator
        {
            private readonly PerformanceMetricEvaluationContext _context;
            private ListMetricInfo _data = null;

            public FileIOWriteEvaluator(PerformanceMetricEvaluationContext context)
            {
                _context = context;
                context.TraceEventSource.Kernel.FileIOWrite += Kernel_FileIOWrite;
            }

            private void Kernel_FileIOWrite(FileIOReadWriteTraceData data)
            {
                if (_context.IsTestEvent(data))
                    _data.addItem(data.FileName, data.IoSize);
            }

            public override void BeginIteration(TraceEvent beginEvent)
            {
                _data = new ListMetricInfo();
                _data.clear();
                _data.hasCount = true;
                _data.hasBytes = true;
            }

            public override object EndIteration(TraceEvent endEvent)
            {
                var ret = _data;
                _data = null;
                return ret;
            }
        }
    }
}

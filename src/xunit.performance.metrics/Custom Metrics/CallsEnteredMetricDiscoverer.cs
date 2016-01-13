using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Xunit.Performance.Sdk;
using System;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace Microsoft.Xunit.Performance
{
    internal class CallsEnteredMetricDiscoverer : IPerformanceMetricDiscoverer
    {
        public IEnumerable<PerformanceMetricInfo> GetMetrics(IAttributeInfo metricAttribute)
        {
            yield return new CallsEnteredMetric();
        }

        private class CallsEnteredMetric : PerformanceMetric
        {
            public CallsEnteredMetric()
                : base("CallsEntered", "Calls Entered", PerformanceMetricUnits.List)
            {
            }

            public override IEnumerable<ProviderInfo> ProviderInfo
            {
                get
                {
                    yield return new UserProviderInfo()
                    {
                        ProviderGuid = ETWClrProfilerTraceEventParser.ProviderGuid,
                        Level = TraceEventLevel.Verbose,
                        Keywords = (ulong)(ETWClrProfilerTraceEventParser.Keywords.Call
                                         | ETWClrProfilerTraceEventParser.Keywords.CallSampled)
                    };
                }
            }

            public override PerformanceMetricEvaluator CreateEvaluator(PerformanceMetricEvaluationContext context)
            {
                return new CallsEnteredEvaluator(context);
            }
        }

        private class CallsEnteredEvaluator : PerformanceMetricEvaluator
        {
            private readonly PerformanceMetricEvaluationContext _context;
            private static ListMetricInfo _objects = null;
            const int MINCALLS = 10;

            public CallsEnteredEvaluator(PerformanceMetricEvaluationContext context)
            {
                _context = context;
                var etwClrProfilerTraceEventParser = new ETWClrProfilerTraceEventParser(context.TraceEventSource);
                etwClrProfilerTraceEventParser.CallEnter += Parser_CallEnter;
            }

            private void Parser_CallEnter(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.CallEnterArgs data)
            {
                if (_context.IsTestEvent(data))
                {
                    var functionName = data.FunctionName.ToString();
                    _objects.addItem(functionName, 1);
                }
            }

            private void cleanObjects()
            {
                List<string> toRemove = new List<string>();
                foreach(var item in _objects.Items)
                {
                    if(item.Value.Count <= MINCALLS)
                    {
                        toRemove.Add(item.Key);
                    }
                }
                foreach(var item in toRemove)
                {
                    _objects.Items.Remove(item);
                }
            }

            public override void BeginIteration(TraceEvent beginEvent)
            {
                _objects = new ListMetricInfo();
                _objects.clear();
            }

            public override object EndIteration(TraceEvent endEvent)
            {
                cleanObjects();
                var ret = _objects;
                _objects = null;
                return ret;
            }
        }
    }
}

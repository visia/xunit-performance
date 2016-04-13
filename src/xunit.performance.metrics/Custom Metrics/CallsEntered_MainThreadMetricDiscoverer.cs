using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Xunit.Performance.Sdk;
using System;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace Microsoft.Xunit.Performance
{
    internal class CallsEntered_MainThreadMetricDiscoverer : IPerformanceMetricDiscoverer
    {
        public IEnumerable<PerformanceMetricInfo> GetMetrics(IAttributeInfo metricAttribute)
        {
            yield return new CallsEntered_MainThreadMetric();
        }

        private class CallsEntered_MainThreadMetric : PerformanceMetric
        {
            public CallsEntered_MainThreadMetric()
                : base("CallsEntered_MainThread", "Calls Entered Main Thread", PerformanceMetricUnits.ListCount)
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
                        Keywords = (ulong)(ETWClrProfilerTraceEventParser.Keywords.Call),
                        StacksEnabled = true
                    };
                }
            }

            public override PerformanceMetricEvaluator CreateEvaluator(PerformanceMetricEvaluationContext context)
            {
                return new CallsEntered_MainThreadEvaluator(context);
            }
        }

        private class CallsEntered_MainThreadEvaluator : PerformanceMetricEvaluator
        {
            private readonly PerformanceMetricEvaluationContext _context;
            private ListMetricInfo _objects = null;
            const int MINCALLS = 0;

            public CallsEntered_MainThreadEvaluator(PerformanceMetricEvaluationContext context)
            {
                _context = context;
                var etwClrProfilerTraceEventParser = new ETWClrProfilerTraceEventParser(context.TraceEventSource);
                etwClrProfilerTraceEventParser.CallEnter += Parser_CallEnter;
            }

            private void Parser_CallEnter(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.CallEnterArgs data)
            {
                if (_context.IsTestEvent(data) && _context.IsMainThread(data))
                {
                    var functionName = data.FunctionName.ToString();
                    if (_objects != null)
                        _objects.addItem(functionName, 1);
                }
            }

            public override void BeginIteration(TraceEvent beginEvent)
            {
                _objects = new ListMetricInfo();
                _objects.clear();
                _objects.hasCount = true;
            }

            public override object EndIteration(TraceEvent endEvent)
            {
                var ret = _objects;
                _objects = null;
                return ret;
            }
        }
    }
}

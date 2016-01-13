﻿using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Xunit.Performance.Sdk;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace Microsoft.Xunit.Performance
{
    internal class _MetricDiscoverer : IPerformanceMetricDiscoverer
    {
        public IEnumerable<PerformanceMetricInfo> GetMetrics(IAttributeInfo metricAttribute)
        {
            yield return new _Metric();
        }

        private class _Metric : PerformanceMetric
        {
            public _Metric()
                : base("internalName", "displayName", PerformanceMetricUnits.Bytes)
            {
            }

            public override IEnumerable<ProviderInfo> ProviderInfo
            {
                get
                {
                    yield return new UserProviderInfo()
                    {
                        ProviderGuid = ClrTraceEventParser.ProviderGuid,
                        Level = TraceEventLevel.Verbose,
                        Keywords = (ulong)ClrTraceEventParser.Keywords.Exception
                    };
                }
            }

            public override PerformanceMetricEvaluator CreateEvaluator(PerformanceMetricEvaluationContext context)
            {
                return new _Evaluator(context);
            }
        }

        private class _Evaluator : PerformanceMetricEvaluator
        {
            private readonly PerformanceMetricEvaluationContext _context;
            private long _count;
            //private long _bytes;

            public _Evaluator(PerformanceMetricEvaluationContext context)
            {
                _context = context;
                context.TraceEventSource.Clr.ExceptionStart += Clr_action;
            }

            private void Clr_action(TraceEvent data)
            {
                if (_context.IsTestEvent(data))
                    _count += 1;
            }

            public override void BeginIteration(TraceEvent beginEvent)
            {
                _count = 0;
            }

            public override double EndIteration(TraceEvent endEvent)
            {
                return _count;
            }
        }
    }
}

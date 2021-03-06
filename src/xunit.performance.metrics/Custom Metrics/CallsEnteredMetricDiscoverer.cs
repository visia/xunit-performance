﻿using Microsoft.Diagnostics.Tracing;
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
                : base("CallsEntered", "Calls Entered", PerformanceMetricUnits.ListCount)
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
                return new CallsEnteredEvaluator(context);
            }
        }

        private class CallsEnteredEvaluator : PerformanceMetricEvaluator
        {
            private readonly PerformanceMetricEvaluationContext _context;
            private ListMetricInfo _objects = null;

            public CallsEnteredEvaluator(PerformanceMetricEvaluationContext context)
            {
                _context = context;
                var etwClrProfilerTraceEventParser = new ETWClrProfilerTraceEventParser(context.TraceEventSource);
                etwClrProfilerTraceEventParser.CallEnter += Parser_CallEnter;
                etwClrProfilerTraceEventParser.FunctionIDDefinition += Parser_FunctionIDDefinition;
            }

            private void Parser_FunctionIDDefinition(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.FunctionIDDefinitionArgs data)
            {
                var functionID = data.FunctionID.ToString();
                var functionName = data.FunctionName;
                IDDefinitionDictionaries.FunctionID_FunctionName[functionID] = functionName;
            }

            private void Parser_CallEnter(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.CallEnterArgs data)
            {
                if (_context.IsTestEvent(data))
                {
                    var functionID = data.FunctionID.ToString();
                    string functionName;
                    if (!IDDefinitionDictionaries.FunctionID_FunctionName.TryGetValue(functionID, out functionName))
                    {
                        return;
                    }
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

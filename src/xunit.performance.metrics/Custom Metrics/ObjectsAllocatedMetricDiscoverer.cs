using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Xunit.Performance.Sdk;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace Microsoft.Xunit.Performance
{
    internal class ObjectsAllocatedMetricDiscoverer : IPerformanceMetricDiscoverer
    {
        public IEnumerable<PerformanceMetricInfo> GetMetrics(IAttributeInfo metricAttribute)
        {
            yield return new ObjectsAllocatedMetric();
        }

        private class ObjectsAllocatedMetric : PerformanceMetric
        {
            public ObjectsAllocatedMetric()
                : base("ObjectsAllocated", "Objects Allocated", PerformanceMetricUnits.ListCountBytes)
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
                        Keywords = (ulong)( ETWClrProfilerTraceEventParser.Keywords.GCAlloc
                                         | ETWClrProfilerTraceEventParser.Keywords.GCAllocSampled
                                         | ETWClrProfilerTraceEventParser.Keywords.Call
                                         | ETWClrProfilerTraceEventParser.Keywords.CallSampled),
                        StacksEnabled = true
                    };
                }
            }

            public override PerformanceMetricEvaluator CreateEvaluator(PerformanceMetricEvaluationContext context)
            {
                return new ObjectsAllocatedEvaluator(context);
            }
        }

        private class ObjectsAllocatedEvaluator : PerformanceMetricEvaluator
        {
            private readonly PerformanceMetricEvaluationContext _context;
            private ListMetricInfo _objects = null;
            private static Dictionary<string, string> _objectNameDict = new Dictionary<string, string>();

            public ObjectsAllocatedEvaluator(PerformanceMetricEvaluationContext context)
            {
                _context = context;
                var etwClrProfilerTraceEventParser = new ETWClrProfilerTraceEventParser(context.TraceEventSource);
                etwClrProfilerTraceEventParser.ClassIDDefintion += Parser_ClassIDDefinition;
                etwClrProfilerTraceEventParser.ObjectAllocated += Parser_ObjectAllocated;
            }

            private void Parser_ClassIDDefinition(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.ClassIDDefintionArgs data)
            {
                var classID = data.ClassID.ToString();
                var className = data.Name;
                _objectNameDict[classID] = className;
            }

            private void Parser_ObjectAllocated(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.ObjectAllocatedArgs data)
            {
                if (_context.IsTestEvent(data))
                {
                    var classID = data.ClassID.ToString();
                    string className;
                    if (!_objectNameDict.TryGetValue(classID, out className))
                    {
                        return;
                    }
                    var size = data.Size;
                    if (_objects != null)
                        _objects.addItem(className, size);
                }
            }

            public override void BeginIteration(TraceEvent beginEvent)
            {
                _objects = new ListMetricInfo();
                _objects.clear();
                _objects.hasCount = true;
                _objects.hasBytes = true;
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

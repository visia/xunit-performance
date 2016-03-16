using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Xunit.Performance.Sdk;
using System.Collections.Generic;
using Xunit.Abstractions;

namespace Microsoft.Xunit.Performance
{
    internal class DllsLoadedMetricDiscoverer : IPerformanceMetricDiscoverer
    {
        public IEnumerable<PerformanceMetricInfo> GetMetrics(IAttributeInfo metricAttribute)
        {
            yield return new DllsLoadedMetric();
        }

        private class DllsLoadedMetric : PerformanceMetric
        {
            public DllsLoadedMetric()
                : base("DllsLoaded", "Dlls Loaded", PerformanceMetricUnits.List)
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
                        Keywords = (ulong)(ETWClrProfilerTraceEventParser.Keywords.GCAlloc
                                         | ETWClrProfilerTraceEventParser.Keywords.Call),
                        StacksEnabled = true
                    };
                }
            }

            public override PerformanceMetricEvaluator CreateEvaluator(PerformanceMetricEvaluationContext context)
            {
                return new DllsLoadedEvaluator(context);
            }
        }

        private class DllsLoadedEvaluator : PerformanceMetricEvaluator
        {
            private readonly PerformanceMetricEvaluationContext _context;
            private ListMetricInfo _objects = null;
            private static Dictionary<string, string> _objectModuleDict = new Dictionary<string, string>();
            private static Dictionary<string, string> _className_classIDDict = new Dictionary<string, string>();
            private static Dictionary<string, string> _moduleNameDict = new Dictionary<string, string>();

            public DllsLoadedEvaluator(PerformanceMetricEvaluationContext context)
            {
                _context = context;
                var etwClrProfilerTraceEventParser = new ETWClrProfilerTraceEventParser(context.TraceEventSource);
                etwClrProfilerTraceEventParser.ClassIDDefintion += Parser_ClassIDDefinition;
                etwClrProfilerTraceEventParser.ModuleIDDefintion += Parser_ModuleIDDefinition;
                etwClrProfilerTraceEventParser.ObjectAllocated += Parser_ObjectAllocated;
            }

            private void Parser_ModuleIDDefinition(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.ModuleIDDefintionArgs data)
            {
                var moduleID = data.ModuleID.ToString();
                var moduleName = data.Path;

                _moduleNameDict[moduleID] = moduleName;
            }

            private void Parser_ClassIDDefinition(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.ClassIDDefintionArgs data)
            {

                var classID = data.ClassID.ToString();
                var className = data.Name;
                var moduleID = data.ModuleID.ToString();

                if (moduleID != "0")
                {
                    _className_classIDDict[className] = classID;
                    _objectModuleDict[classID] = moduleID;
                }

                else
                {
                    if (!className.EndsWith("[]"))
                    {
                        // lazy messy way to do handle commas. fix later
                        className = className.Replace(",", "");
                        if (!className.EndsWith("[]"))
                            throw new System.Exception($"Cannot find module for class {className}.");
                    }
                    var fixedClassName = className;
                    while (fixedClassName.EndsWith("[]"))
                    {
                        fixedClassName = fixedClassName.Substring(0, fixedClassName.Length - 2);
                    }
                    string fixedClassID;

                    if (!_className_classIDDict.TryGetValue(fixedClassName, out fixedClassID))
                    {
                        //throw new System.Exception($"Cannot find class ID for class {fixedClassName}");
                        return;
                    }

                    if(!_objectModuleDict.TryGetValue(fixedClassID, out moduleID))
                        throw new System.Exception($"Cannot find module for class {className}.");
                    _objectModuleDict[classID] = moduleID;
                }
            }

            private void Parser_ObjectAllocated(Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfiler.ObjectAllocatedArgs data)
            {
                if (_context.IsTestEvent(data))
                {
                    var classID = data.ClassID.ToString();
                    string moduleID;
                    if(!_objectModuleDict.TryGetValue(classID, out moduleID))
                    {
                        return;
                    }
                    string moduleName;
                    if (!_moduleNameDict.TryGetValue(moduleID, out moduleName))
                    {
                        return;
                    }
                    var size = data.Size;
                    if(_objects != null)
                        _objects.addItem(moduleName, size);
                }
            }

            public override void BeginIteration(TraceEvent beginEvent)
            {
                _objects = new ListMetricInfo();
                _objects.clear();
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

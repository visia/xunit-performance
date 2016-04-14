using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Xunit.Performance.Sdk
{
    /// <summary>
    /// Holds dictionaries that map various things for profiling.
    /// </summary>
    public static class IDDefinitionDictionaries
    {
        public static Dictionary<string, string> ClassID_ModuleID = new Dictionary<string, string>();
        public static Dictionary<string, string> ClassName_ClassID = new Dictionary<string, string>();
        public static Dictionary<string, string> ClassID_ClassName = new Dictionary<string, string>();
        public static Dictionary<string, string> ModuleID_ModuleName = new Dictionary<string, string>();
        public static Dictionary<string, string> FunctionID_FunctionName = new Dictionary<string, string>();
    }
}

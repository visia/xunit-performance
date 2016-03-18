using System;

namespace Microsoft.Xunit.Performance
{
    public static class RunConfiguration
    {
        internal static int _minIterations = 9;
        /// <summary>
        /// Minimum number of iterations to run each test (counts from 0)
        /// </summary>
        public static int XUNIT_PERFORMANCE_MIN_ITERATION { get { return _minIterations; } }

        internal static int _maxIterations = 9;
        /// <summary>
        /// Maximum number of iterations to run each test (counts from 0)
        /// </summary>
        public static int XUNIT_PERFORMANCE_MAX_ITERATION { get { return _maxIterations; } }

        internal static int _maxTotalMilliseconds = 9;
        /// <summary>
        /// Maximum amount of time to run each test (0 = no limit)
        /// </summary>
        public static int XUNIT_PERFORMANCE_MAX_TOTAL_MILLISECONDS { get { return _maxTotalMilliseconds; } }

        internal static bool _stacksEnabled = false;
        /// <summary>
        /// If stacks should be enabled for PerfView investigation
        /// </summary>
        public static bool XUNIT_PERFORMANCE_STACKS_ENABLED { get { return _stacksEnabled; } }
    }
}

﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;

namespace Microsoft.Xunit.Performance.Sdk
{
    public abstract class PerformanceMetricEvaluator : IDisposable
    {
        public abstract void BeginIteration(TraceEvent beginEvent);

        public abstract object EndIteration(TraceEvent endEvent);

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        ~PerformanceMetricEvaluator()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}

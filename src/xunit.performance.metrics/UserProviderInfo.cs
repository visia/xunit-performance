﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Diagnostics.Tracing;
using System;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Generic;

namespace Microsoft.Xunit.Performance
{
    [Serializable]
    public sealed class UserProviderInfo : ProviderInfo
    {
        public Guid ProviderGuid { get; set; }
        public ulong Keywords { get; set; }

        public TraceEventLevel Level { get; set; } = TraceEventLevel.Verbose;

        internal override void MergeInto(Dictionary<Guid, UserProviderInfo> userInfo, KernelProviderInfo kernelInfo, Dictionary<string, CpuCounterInfo> cpuInfo)
        {
            UserProviderInfo current;
            if (!userInfo.TryGetValue(ProviderGuid, out current))
            {
                userInfo.Add(ProviderGuid, this);
            }
            else
            {
                userInfo[ProviderGuid] = new UserProviderInfo()
                {
                    ProviderGuid = this.ProviderGuid,
                    Keywords = current.Keywords | this.Keywords,
                    Level = (this.Level > current.Level) ? this.Level : current.Level,
                    StacksEnabled = this.StacksEnabled || current.StacksEnabled                    
                };
            }
        }

        public bool StacksEnabled { get; set; } = false;
    }
}

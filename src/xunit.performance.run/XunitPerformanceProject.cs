// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Xunit.Performance
{
    public class XunitPerformanceProject : XunitProject
    {
        private List<XunitProjectAssembly> _baselineAssemblies = new List<XunitProjectAssembly>();
        private string _baselineRunnerCommand;

        public IEnumerable<XunitProjectAssembly> BaselineAssemblies { get { return _baselineAssemblies; } }

        public void AddBaseline(XunitProjectAssembly assembly) { _baselineAssemblies.Add(assembly); }

        public string RunnerHost { get; set; } = null;

        public string RunnerCommand { get; set; } = "xunit.console.exe";

        public string RunnerArgs { get; set; }

        public string BaselineRunnerCommand
        {
            get { return _baselineRunnerCommand ?? RunnerCommand; }
            set { _baselineRunnerCommand = value; }
        }

        public string RunId { get; set; } = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss");

        private string _outputDir;
        public string OutputDir {
            get
            {
                return _outputDir ?? System.IO.Path.Combine(".", OutputBaseFileName);
            }
            set
            {
                _outputDir = value;
            }
        }

        private string _outputBaseFileName;
        public string OutputBaseFileName {
            get
            {
                return _outputBaseFileName ?? RunId;
            }
            set
            {
                _outputBaseFileName = value;
            }
        }

        private bool _useLocalUser = false;
        /// <summary>
        /// Whether xunit.console.exe is run on a different user account
        /// </summary>
        public bool UseLocalUser {
            get
            {
                return _useLocalUser;
            }
            set
            {
                _useLocalUser = value;
            }
        }

        /// <summary>
        /// Computer to run this xunit.console.exe on
        /// </summary>
        public string runComputer { get; set; }

        /// <summary>
        /// User to run xunit.console.exe on (format: Environment.MachineName\LocalAccount.Username)
        /// </summary>
        public string runCredentialsUsername { get; set; } 

        /// <summary>
        /// Secure string password of user account
        /// </summary>
        public System.Security.SecureString runCredentialsPassword { get; set; }

        /// <summary>
        /// Environment variables to set for xunit.console.exe
        /// </summary>
        public System.Collections.DictionaryEntry[] runEnvVars { get; set; }
    }
}

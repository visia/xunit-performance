using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Xunit.Performance
{
    /// <summary>
    /// Can't use xunit to test in this project (conflicts with framework...) so use mstest...
    /// </summary>
    [TestClass]
    public class CmdletTests
    {
        public string RunComputer { get; set; }

        //public PSCredential RunCredential { get; set; }

        public System.Collections.DictionaryEntry[] RunEnvVars { get; set; }

        public string CommandLineArgs { get; set; }

        public string[] args { get { return  InvokePerfTestsCmdlet.CommandLineToArgs(CommandLineArgs.TrimEnd(' ')); } }

        public bool UseLocalUser { get; set; }

        [TestMethod()]
        public void RunFullCmdletTest()
        {
            RunComputer = "VISIA1";
            UseLocalUser = true;
            CommandLineArgs = @"D:\VS\out\Tests\SimplePerfTests.dll -outdir D:\VS\out\Tests\TestResults\test -runner D:\PerfUnitTest\xunit\src\xunit.console\bin\Release\xunit.console.exe ";
            int arraySize = 0;
            foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
            {
                arraySize++;
            }
            RunEnvVars = new System.Collections.DictionaryEntry[arraySize];
            arraySize = 0;
            foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
            {
                RunEnvVars[arraySize] = de;
                arraySize++;
            }
            var pass = "qwer1234!".ToCharArray();
            System.Security.SecureString securepass = new System.Security.SecureString();
            foreach (char ch in pass)
                securepass.AppendChar(ch);





            if (UseLocalUser)
            {
                if (RunComputer == null || RunEnvVars == null)
                    throw new Exception("If using local user, must specify runcomputer, runcredential, runenvvars.");
            }

            Program p = new Program();

            if (args.Length == 0 || args[0] == "-?")
            {
                p.PrintHeader();
                Program.PrintUsage();
                return;
            }

            try
            {
                var project = p.ParseCommandLine(args);
                string UserName = "Test-D__VS_src-2";

                if (UseLocalUser)
                {
                    project.UseLocalUser = true;
                    project.runComputer = RunComputer;
                    project.runCredentialsUsername = UserName;
                    project.runCredentialsPassword = securepass;
                    project.runEnvVars = RunEnvVars;
                }

                if (!p._nologo)
                {
                    p.PrintHeader();
                }

                using (AssemblyHelper.SubscribeResolve())
                {
                    p.PrintIfVerbose($"Creating output directory: {project.OutputDir}");
                    if (!Directory.Exists(project.OutputDir))
                        Directory.CreateDirectory(project.OutputDir);

                    p.RunTests(project);

                }

                return;
            }
            catch (Exception ex)
            {
                Console.Error.Write("Error: ");
                Program.ReportExceptionToStderr(ex);
            }
        }
    }
}

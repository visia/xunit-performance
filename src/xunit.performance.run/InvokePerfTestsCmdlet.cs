using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management.Automation;
using Xunit;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Xunit.Performance
{
    [Cmdlet(VerbsLifecycle.Invoke, "PerfTests")]
    public class InvokePerfTestsCmdlet : Cmdlet
    {
        [Parameter(Mandatory = false)]
        public string RunComputer { get; set; }

        [Parameter(Mandatory = false)]
        public PSCredential RunCredential { get; set; }

        [Parameter(Mandatory = false)]
        public System.Collections.DictionaryEntry[] RunEnvVars { get; set; }

        [Parameter(Mandatory = true)]
        public string CommandLineArgs { get; set; }

        public string[] args { get { return CommandLineToArgs(CommandLineArgs.TrimEnd(' ')); } }

        [Parameter(Mandatory =  true)]
        public bool UseLocalUser { get; set; }

        public void run()
        {
            ProcessRecord();
        }

        /// <summary>
        /// Assume Invoke-Tests was run previously, aka all dependencies are coppied
        /// </summary>
        protected override void ProcessRecord()
        {
            if(UseLocalUser)
            {
                if (RunComputer == null || RunCredential == null || RunEnvVars == null)
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
                string UserName = RunCredential.UserName.Substring(RunComputer.Length + 1);

                if (UseLocalUser)
                {
                    project.UseLocalUser = true;
                    project.runComputer = RunComputer;
                    project.runCredentialsUsername = UserName;
                    project.runCredentialsPassword = RunCredential.Password;
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

                WriteObject(project.OutputBaseFileName + ".xml");
            }
            catch (Exception ex)
            {
                Console.Error.Write("Error: ");
                Program.ReportExceptionToStderr(ex);
            }
        }

        /// <summary>
        /// Parses a string into string[] args
        /// </summary>
        public static string[] CommandLineToArgs(string argstring)
        {
            char[] argchars = argstring.ToCharArray();
            bool quote = false;
            for(int i=0; i < argchars.Length; i++)
            {
                if (argchars[i] == '"')
                    quote = !quote;
                if (!quote && argchars[i] == ' ')
                    argchars[i] = '\n';
            }
            string ret = new string(argchars);
            return ret.Split('\n');
        }
    }
}

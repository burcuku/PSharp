﻿// ------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt in the repo root for full license information.
// ------------------------------------------------------------------------------------------------

using System.Diagnostics;

using Microsoft.Build.Framework;
using Microsoft.PSharp.IO;
using Microsoft.PSharp.LanguageServices.Compilation;
using Microsoft.PSharp.LanguageServices.Parsing;
using Microsoft.PSharp.LanguageServices;

namespace Microsoft.PSharp
{
    /// <summary>
    /// The P# syntax rewriter.
    /// </summary>
    public class SyntaxRewriterProcess
    {
        static void Main(string[] args)
        {
            // number of args must be even
            if (args.Length % 2 != 0)
            {
                Output.WriteLine("Usage: PSharpSyntaxRewriterProcess.exe file1.psharp, outfile1.cs, file2.pshap, outfile2.cs, ...");
                return;
            }

            int count = 0;
            while (count < args.Length)
            {
                string inputFileName = args[count];
                count++;
                string outputFileName = args[count];
                count++;
                // Get input file as string
                var inputString = string.Empty;
                try
                {
                    inputString = System.IO.File.ReadAllText(inputFileName);
                }
                catch (System.IO.IOException e)
                {
                    Output.WriteLine("Error: {0}", e.Message);
                    return;
                }

                // Translate and write to output file
                string errors = string.Empty;
                var outputString = Translate(inputString, out errors);
                if (outputString == null)
                {
                    // replace Program.psharp with the actual file name
                    errors = errors.Replace("Program.psharp", System.IO.Path.GetFileName(inputFileName));
                    // print a compiler error with log
                    System.IO.File.WriteAllText(outputFileName,
                        string.Format("#error Psharp Compiler Error {0} /* {0} {1} {0} */ ", "\n", errors));
                }
                else
                {
                    // Tagging the generated .cs files with the "<auto-generated>" tag so as to avoid StyleCop build errors.
                    outputString = "//  <auto-generated />\n" + outputString;

                    System.IO.File.WriteAllText(outputFileName, outputString);
                }
            }
        }

        /// <summary>
        /// Translates the specified text from P# to C#.
        /// </summary>
        /// <param name="text">Text</param>
        /// <returns>Text</returns>
        public static string Translate(string text, out string errors)
        {
            var configuration = Configuration.Create();
            configuration.Verbose = 2;
            errors = null;

            var context = CompilationContext.Create(configuration).LoadSolution(text);

            try
            {
                ParsingEngine.Create(context).Run();
                RewritingEngine.Create(context).Run();

                var syntaxTree = context.GetProjects()[0].PSharpPrograms[0].GetSyntaxTree();

                return syntaxTree.ToString();
            }
            catch (ParsingException ex)
            {
                errors = ex.Message;
                return null;
            }
            catch (RewritingException ex)
            {
                errors = ex.Message;
                return null;
            }
        }
    }

    public class RewriterAsSeparateProcess : ITask
    {
        public IBuildEngine BuildEngine { get; set; }
        public ITaskHost HostObject { get; set; }

        public ITaskItem[] InputFiles { get; set; }

        [Output]
        public ITaskItem[] OutputFiles { get; set; }

        public bool Execute()
        {
            string processInputString = string.Empty;
            for (int i = 0; i < InputFiles.Length; i++)
            {
                processInputString += InputFiles[i].ItemSpec;
                processInputString += " ";
                processInputString += OutputFiles[i].ItemSpec;
                if (i + 1 < InputFiles.Length)
                {
                    processInputString += " ";
                }
            }
            var process = new Process();
            var processStartInfo = new ProcessStartInfo(this.GetType().Assembly.Location, processInputString);
            processStartInfo.CreateNoWindow = true;
            processStartInfo.UseShellExecute = false;
            processStartInfo.RedirectStandardOutput = true;
            process.StartInfo = processStartInfo;
            process.Start();
            process.WaitForExit();
            return true;
        }
    }
}

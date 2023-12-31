﻿using GroupChatExample.DotnetInteractiveService;
using GroupChatExample.Helper;
using Microsoft.DotNet.Interactive.Documents.Jupyter;
using Microsoft.DotNet.Interactive.Documents;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GroupChatExample.Helper
{
    public partial class DotnetInteractiveFunction : IDisposable
    {
        private readonly InteractiveService? _interactiveService = null;
        private readonly Logger? _logger;
        private string? _notebookPath;


        public DotnetInteractiveFunction(InteractiveService interactiveService, string? notebookPath = null, Logger? logger = null, bool continueFromExistingNotebook = false)
        {
            this._interactiveService = interactiveService;
            this._logger = logger;
            this._notebookPath = notebookPath;

            if (this._notebookPath != null)
            {
                if (continueFromExistingNotebook == false)
                {
                    // remove existing notebook
                    if (File.Exists(this._notebookPath))
                    {
                        logger?.Log("Removing existing notebook.");
                        File.Delete(this._notebookPath);
                    }

                    // create an empty notebook
                    logger?.Log("Creating an empty notebook.");
                    var document = new InteractiveDocument();

                    using var stream = File.OpenWrite(_notebookPath);
                    Notebook.Write(document, stream);
                    stream.Flush();
                    stream.Dispose();
                    logger?.Log($"A new Notebook: {_notebookPath} is created.");
                }
                else if (continueFromExistingNotebook == true && File.Exists(this._notebookPath))
                {
                    logger?.Log("Continue from existing notebook.");
                    // load existing notebook
                    using var readStream = File.OpenRead(this._notebookPath);
                    var document = Notebook.ReadAsync(readStream).Result;
                    foreach(var cell in document.Elements)
                    {
                        if (cell.KernelName == "csharp")
                        {
                            var code = cell.Contents;
                            logger?.Log($"Submitting existing code: {code}");
                            this._interactiveService.SubmitCSharpCodeAsync(code, default).Wait();
                        }
                    }
                }
                else
                {
                    // create an empty notebook
                    logger?.Log("Creating an empty notebook.");
                    var document = new InteractiveDocument();

                    using var stream = File.OpenWrite(_notebookPath);
                    Notebook.Write(document, stream);
                    stream.Flush();
                    stream.Dispose();
                    logger?.Log($"A new Notebook: {_notebookPath} is created.");
                }
            }
        }

        /// <summary>
        /// Run existing dotnet code from message. Don't modify the code, run it as is.
        /// </summary>
        /// <param name="code">code.</param>
        [FunctionAttribution]
        public async Task<string> RunCode(string code)
        {
            if (this._interactiveService == null)
            {
                throw new Exception("InteractiveService is not initialized.");
            }

            await (_logger?.LogToFile($"{nameof(RunCode)}.cs", code) ?? Task.CompletedTask);

            var result = await this._interactiveService.SubmitCSharpCodeAsync(code, default);
            if (result != null)
            {
                // if result contains Error, return entire message
                if (result.Contains("Error"))
                {
                    return result;
                }

                // add cell if _notebookPath is not null
                if (this._notebookPath != null)
                {
                    await AddCellAsync(code, "csharp");
                }

                // if result is over 100 characters, only return the first 100 characters.
                if (result.Length > 100)
                {
                    result = result.Substring(0, 100) + " (...too long to present)";

                    return result;
                }

                return result;
            }

            // add cell if _notebookPath is not null
            if (this._notebookPath != null)
            {
                await AddCellAsync(code, "csharp");
            }

            return "Code run successfully. no output is available.";
        }

        /// <summary>
        /// Install nuget packages.
        /// </summary>
        /// <param name="nugetPackages">nuget package to install.</param>
        [FunctionAttribution]
        public async Task<string> InstallNugetPackages(string[] nugetPackages)
        {
            if (this._interactiveService == null)
            {
                throw new Exception("InteractiveService is not initialized.");
            }

            var codeSB = new StringBuilder();
            foreach (var nuget in nugetPackages ?? Array.Empty<string>())
            {
                var nugetInstallCommand = $"#r \"nuget:{nuget}\"";
                codeSB.AppendLine(nugetInstallCommand);
                await this._interactiveService.SubmitCSharpCodeAsync(nugetInstallCommand, default);
            }

            var code = codeSB.ToString();
            if (this._notebookPath != null)
            {
                await AddCellAsync(code, "csharp");
            }

            var sb = new StringBuilder();
            sb.AppendLine("Installed nuget packages:");
            foreach (var nuget in nugetPackages ?? Array.Empty<string>())
            {
                sb.AppendLine($"- {nuget}");
            }

            return sb.ToString();
        }

        private async Task AddCellAsync(string cellContent, string kernelName)
        {
            if (!File.Exists(this._notebookPath))
            {
                using var stream = File.OpenWrite(this._notebookPath);
                Notebook.Write(new InteractiveDocument(), stream);
                stream.Dispose();
            }

            using var readStream = File.OpenRead(this._notebookPath);
            var document = await Notebook.ReadAsync(readStream);
            readStream.Dispose();

            var cell = new InteractiveDocumentElement(cellContent, kernelName);

            document.Add(cell);

            using var writeStream = File.OpenWrite(this._notebookPath);
            Notebook.Write(document, writeStream);
            // sleep 3 seconds
            await Task.Delay(3000);
            writeStream.Flush();
            writeStream.Dispose();
        }

        public void Dispose()
        {
            this._interactiveService?.Dispose();
        }
    }
}

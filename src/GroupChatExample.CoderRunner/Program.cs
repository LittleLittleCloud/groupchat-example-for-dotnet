﻿// See https://aka.ms/new-console-template for more information
using Azure.AI.OpenAI;
using GroupChatExample.DotnetInteractiveService;
using GroupChatExample.Helper;
using System.Reflection;
using System.Text;

var workDir = Path.Combine(Path.GetTempPath(), "InteractiveService");

// remove workDir if exists
if (Directory.Exists(workDir))
{
    Directory.Delete(workDir, recursive: true);
}

// create workDir
Directory.CreateDirectory(workDir);

// set up openai client and model id to use.
var model = Constant.GPT_35_MODEL_ID;
var openAIClient = Constant.GPT;

using var service = new InteractiveService(workDir);
await service.StartAsync(workDir, default);
var logger = new Logger(workDir);
using var dotnetInteractiveFunction = new DotnetInteractiveFunction(service, logger: logger);
var fixInvalidJsonFunction = new FixInvalidJsonFunctionWrapper(openAIClient, model);

var coder = new GPTAgent(
        openAIClient,
        model,
        "Coder",
        @"You act as dotnet coder, you write dotnet script to resolve tasks.
Here's the workflow you follow:
-workflow-
if no_current_step
    ask_for_current_step
else
    write_code_to_resolve_current_step

if code_has_error
    fix_code_error

wait_for_next_step

-end-

Here're some rules to follow on write_code_to_resolve_current_step:
- put code between ```csharp and ```
- Use top-level statements, remove main function, just write code, like what python does.
- Remove all `using` statement. Runner can't handle it.
- Try to use `var` instead of explicit type.
- Try avoid using external library.
- Don't use external data source, like file, database, etc. Create a dummy dataset if you need.
- Always print out the result to console. Don't write code that doesn't print out anything.

Here are some examples for ask_for_current_step:
- No current step is provided. Please provide current step.

Here are some examples for write_code_to_resolve_current_step:
```nuget
xxx
```
```csharp
xxx
```

Here are some examples for fix_code_error:
The error is caused by xxx. Here's the fix code
```csharp
xxx
```
");

var runner = new GPTAgent(
        openAIClient,
        model,
        "Runner",
        @"You act as dotnet runner, you run dotnet script and install nuget packages. Here's the workflow you follow:
-workflow-
if code_is_available_from_latest_message
    if nuget_packages_is_available_from_latest_message
        install_nuget_packages
    run_code_from_latest_message
else
    ask_for_code_to_run
-end-

Here are some examples for ask_for_code_to_run:
- No code is provided. Please provide code to run.

Here are some examples for run_code_from_latest_message:
- run_code // code to run

Here are some examples for install_nuget_packages:
- install_nuget_packages // nuget packages to install
",
        new Dictionary<FunctionDefinition, Func<string, Task<string>>>
        {
            { dotnetInteractiveFunction.RunCodeFunction, fixInvalidJsonFunction.FixInvalidJsonWrapper(dotnetInteractiveFunction.RunCodeWrapper) },
            { dotnetInteractiveFunction.InstallNugetPackagesFunction, dotnetInteractiveFunction.InstallNugetPackagesWrapper },
        });

var admin = new GPTAgent(
    openAIClient,
    model,
    "Admin",
    @"You act as group admin that lead other agents to resolve task together. Here's the workflow you follow:
-workflow-
if all_steps_are_resolved
    terminate_chat
else
    resolve_step
-end-

The task is
Retrieve the latest PR from mlnet repo, print the result and save the result to pr.txt.
The steps to resolve the task are:
1. Send a GET request to the GitHub API to retrieve the list of pull requests for the mlnet repo.
2. Parse the response JSON to extract the latest pull request.
3. Print the result to the console and save the result to a file named ""pr.txt"".

Here are some examples for resolve_step:
- The step to resolve is xxx, let's work on this step.
");

var groupChat = new GroupChat(
    openAIClient,
    model,
    admin,
    new[]
    {
        coder,
        runner,
    });

admin.FunctionMaps.Add(groupChat.TerminateGroupChatFunction, groupChat.TerminateGroupChatWrapper);

groupChat.AddInitializeMessage("Welcome to the group chat! Work together to resolve my task.", admin.Name);
groupChat.AddInitializeMessage("Hey I'm Coder", coder.Name);
groupChat.AddInitializeMessage("Hey I'm Runner", runner.Name);
groupChat.AddInitializeMessage($"The link to mlnet repo is: https://github.com/dotnet/machinelearning. you don't need a token to use github pr api. Make sure to include a User-Agent header, otherwise github will reject it.", admin.Name);
groupChat.AddInitializeMessage(@$"Here's the workflow for this group chat
-groupchat workflow-
if all_steps_are_resolved
    admin_terminate_chat
else

admin_give_step_to_resolve
coder_write_code_to_resolve_step
runner_run_code_from_coder
if code_is_correct
    admin_give_next_step
else
    coder_fix_code_error
", admin.Name);

var conversation = await admin.SendMessageAsync("Here's the first step to resolve: Send a GET request to the GitHub API to retrieve the list of pull requests for the mlnet repo.", groupChat, 30, false);

// log conversation to chat_history.txt
if(conversation is not null)
{
    var sb = new StringBuilder();
    foreach(var (message, name) in conversation)
    {
        var fmtMsg = groupChat.FormatMessage(message, name);
        sb.AppendLine(fmtMsg);
    }

    var chatHistoryPath = Path.Combine(workDir, "chat_history.txt");
    await logger.LogToFile("chat_history.txt", sb.ToString());
    logger.Log($"Chat history is logged to {chatHistoryPath}");
}

﻿// See https://aka.ms/new-console-template for more information
using Azure.AI.OpenAI;
using GroupChatExample.CoderRunner;
using GroupChatExample.DotnetInteractiveService;
using GroupChatExample.Helper;

var workDir = Path.Combine(Path.GetTempPath(), "InteractiveService");
if (!Directory.Exists(workDir))
{
    Directory.CreateDirectory(workDir);
}

using var service = new InteractiveService(workDir);
await service.StartAsync(workDir, default);
var logger = new Logger(workDir);
using var dotnetInteractiveFunction = new DotnetInteractiveFunction(service, logger);
var OPENAI_API_KEY = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new InvalidOperationException("OPENAI_API_KEY is not set");
var model = "gpt-3.5-turbo-0613";
var openAIClient = new OpenAIClient(OPENAI_API_KEY);
var fixInvalidJsonFunction = new FixInvalidJsonFunctionWrapper(openAIClient, model);

var coder = new ChatAgent(
        openAIClient,
        model,
        "Coder",
        @"You are a helpful dotnet coder, you write dotnet script to resolve tasks.
First, create a step-by-step plan for the task. Then implement each step one by one. Once you complete one step, reply [COMPLETE] to admin.
Complete one step at a time, once you complete a step, you ask runner to run that step for you.
e.g.
- step1: download file
```
...
```

runner, run step 1 for me.

Once you complete all steps, rely [COMPLETE].
Here're some tips when you write dotnet code:
- Write code in dotnet interactive. You can use `#r` to reference nuget package.
- Don't use `using` statement. Runner can't handle it.
- Try to use `var` instead of explicit type.
- Try avoid using external library.
");

var runner = new ChatAgent(
        openAIClient,
        model,
        "Runner",
        @"You are a dotnet runner,
If the code contains Main() function, reject it.
Otherwise, call RunCode to run the code from the most recent message if it contains code block. Don't modify the code block, pass it as is.
You call InstallNugetPackages if the most recent message contains nuget package installation.
Otherwise you simply return 'no code block found'",
        new Dictionary<FunctionDefinition, Func<string, Task<string>>>
        {
            { dotnetInteractiveFunction.RunCodeFunction, fixInvalidJsonFunction.FixInvalidJsonWrapper(dotnetInteractiveFunction.RunCodeWrapper) },
            { dotnetInteractiveFunction.InstallNugetPackagesFunction, dotnetInteractiveFunction.InstallNugetPackagesWrapper },
        });

var admin = new ChatAgent(
    openAIClient,
    model,
    "Admin",
    @"You are admin, you provide task to coder and runner. You terminate group chat when task resolved successfully. Otherwise you ask coder to resolve your task.");

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

groupChat.AddMessage("Welcome to the group chat! Work together to resolve my task.", admin.Name);
groupChat.AddMessage("I'll write dotnet script to resolve Admin's task. I'll fix any bugs from Runner", coder.Name);
groupChat.AddMessage("I'll run dotnet code from Coder and return result.", runner.Name);
groupChat.AddMessage($"The task is: retrieve the latest PR from mlnet repo and save the result to pr.txt.", admin.Name);
groupChat.AddMessage($"The link to mlnet repo is: https://github.com/dotnet/machinelearning. You don't need to pass a token as this api is public available. Make sure to include a User-Agent header, otherwise github will reject it.", admin.Name);

var conversation = await groupChat.CallAsync(maxRound: 20);
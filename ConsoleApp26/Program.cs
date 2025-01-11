using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;

IConfigurationRoot config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

string deploymentName = config["OpenAI:DeploymentName"] ?? throw new InvalidOperationException("OpenAI:DeploymentName is not set.");
string endpoint = config["OpenAI:Endpoint"] ?? throw new InvalidOperationException("OpenAI:BaseUrl is not set.");

IKernelBuilder builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(
       deploymentName,
   endpoint,
   new DefaultAzureCredential());

builder.Services.AddLogging(c => c.AddDebug().SetMinimumLevel(LogLevel.Trace));
Kernel kernel = builder.Build();


#pragma warning disable SKEXP0110 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
ChatCompletionAgent AzureAgent = new()
{
    Name = "Azure",
    Instructions = "Azureが大好きな人です。一方AWSは大嫌いです。Azureのよいところを提示する一方AWSの悪いところをとことん追求します。",
    Kernel = kernel,
};
ChatCompletionAgent AwsAgent = new()
{
    Name = "AWS",
    Instructions = "AWSが大好きな人です。一方Azureは大嫌いです。AWSのよいところを提示する一方Azureの悪いところをとことん追求します。",
    Kernel = kernel,
};
ChatCompletionAgent facilitator = new ChatCompletionAgent()
{
    Name = "Facilitator",
    Instructions = $"""
    それぞれのエージェントがAzureとAWSの長所を提示して議論してください。
    Azure大好きな{AzureAgent.Name}とAWS大好きな{AwsAgent.Name}が議論します。
    議論の結果を受けて、どちらが勝ったかを判定します。
    必ずどちらが勝利しているか5ターン程度で最終的に結論を出してください。
    
    例）Azureが勝利している場合は「Azureが勝利しました」と回答してください。
""",
    Kernel = kernel,
};

KernelFunction terminationFunction = AgentGroupChat.CreatePromptFunctionForStrategy($$$"""
    会話の履歴を確認して{{{facilitator.Name}}}がAzureかAWSかの勝利を宣言しているか判定してください。
    勝利を宣言している場合はDoneと回答してください。
    まだ勝利を宣言していない場合はDoingと回答してください。
    返答には余計な装飾や会話を含めずDone かDoing のみを返してください。

    {{{facilitator.Name}}}の結論だけを見て判定してください。

    {{{AzureAgent.Name}}}や{{{AwsAgent.Name}}}の発言は無視してください。

    会話の履歴：{{${{{KernelFunctionTerminationStrategy.DefaultHistoryVariableName}}}}}
    """);

AgentGroupChat chat = new(AzureAgent, AwsAgent, facilitator)
{
    ExecutionSettings = new Microsoft.SemanticKernel.Agents.Chat.AgentGroupChatSettings()
    {
        TerminationStrategy = new KernelFunctionTerminationStrategy(terminationFunction, kernel)
        {
            Agents = [AzureAgent, AwsAgent, facilitator],
            MaximumIterations = 30,
            ResultParser = result => result.GetValue<string>() == "Done"
        }
    }
};

ChatHistory history = [];
chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, """
    PaaSサービスを比較した上でAzureとAWSのどちらが有利かについて議論してください。
    """));
await foreach (var message in chat.InvokeAsync())
{
    history.Add(message);
    Console.WriteLine($"{message.AuthorName!}: {message.Role} {message}" );
}
#pragma warning restore SKEXP0110 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。

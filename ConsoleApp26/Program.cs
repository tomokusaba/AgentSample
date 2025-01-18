using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.Agents.History;
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

IKernelBuilder builder2 = Kernel.CreateBuilder();
builder2.AddAzureOpenAIChatCompletion(
       deploymentName,
   endpoint,
   new DefaultAzureCredential());

builder2.Services.AddLogging(c => c.AddDebug().SetMinimumLevel(LogLevel.Trace));
Kernel kernel2 = builder2.Build();


#pragma warning disable SKEXP0110 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
ChatCompletionAgent AzureAgent = new()
{
    Name = "Azure",
    Instructions = "Azureが大好きな人です。一方AWSは大嫌いです。Azureのよいところを提示する一方AWSの悪いところをとことん追求します。5個程度の例をステップバイステップで考えて具体的に提示します。",
    Kernel = kernel2,
    Description = "Azure大好きな人",
};
ChatCompletionAgent AwsAgent = new()
{
    Name = "AWS",
    Instructions = "AWSが大好きな人です。一方Azureは大嫌いです。AWSのよいところを提示する一方Azureの悪いところをとことん追求します。5個程度の例を提示します。",
    Kernel = kernel,
    Description = "AWS大好きな人",
};
ChatCompletionAgent facilitator = new ChatCompletionAgent()
{
    Name = "Facilitator",
    Instructions = $"""
    これから、.NETFrameworkのWebFormsアプリケーションをモダナイズしようとしています。
    それぞれ、移行先のアプリケーションをAzureとAWSで構築することを考えています。
    アプリケーションアークテクチャーをそれぞれ明確に提案してください。
    どのような言語でアプリケーションを構築するか、どのようなデータベースを利用するか、どのようなキャッシュを利用するか、どのようなセキュリティを考慮するか、どのようなネットワーク構成を考慮するか、どのようなモニタリングを行うか、どのようなCI/CDを行うか、
    どのようなフレームワークを使用するのか詳しく説明してください。

    その上で、どのようなサービスをつかってアプリケーションを構築するか提案してください。

    
    まず、Azureが大好きな{AzureAgent}が提案してください。
    次に、AWSが大好きな{AwsAgent}が提案してください。
    その後、Azureが大好きな{AzureAgent}がAWSの提案について反論します。
    そして、AWSが大好きな{AwsAgent}がAzureの提案について反論します。

    その後、ファシリテーターの判断で議論を進めます。
    Azure大好きな{AzureAgent}とAWS大好きな{AwsAgent}が議論します。
    {AzureAgent}と{AwsAgent}の議論をファシリテーターが進行します。
    議論の結果を受けて、どちらが勝ったかを判定します。

    ファシリテーターは議論に介入して誘導してください。

    どちらのエージェントが発言しているかを分かるようにしてください。
    
    **途中の議論を決して省略しないですべて書き出してください**

    ファシリテーターはAzureが大好きです。Azureの事例を持ち出し議論を進めてください。

    必ずどちらが勝利しているか30ターン程度で最終的に結論を出してください。
    ジャッジは公平に行ってください。
    
    例）Azureが勝利している場合は「Azureが勝利しました」と回答してください。
""",
    Description = "ファシリテーター",
    Kernel = kernel2,
};

KernelFunction terminationFunction = AgentGroupChat.CreatePromptFunctionForStrategy($$$"""
    会話の履歴を確認して{{{facilitator}}}がAzureかAWSかの勝利を宣言しているか判定してください。
    勝利を宣言している場合はDoneと回答してください。
    まだ勝利を宣言していない場合はDoingと回答してください。
    返答には余計な装飾や会話を含めずDone かDoing のみを返してください。

    {{{facilitator}}}の結論だけを見て判定してください。

    {{{AzureAgent}}}や{{{AwsAgent}}}の発言は無視してください。

    会話の履歴：{{$history}}
    """);
ChatHistoryTruncationReducer historyTruncationReducer = new(2);
AgentGroupChat chat = new(AzureAgent, AwsAgent, facilitator)
{
    ExecutionSettings = new Microsoft.SemanticKernel.Agents.Chat.AgentGroupChatSettings()
    {
        SelectionStrategy = new KernelFunctionSelectionStrategy(AgentGroupChat.CreatePromptFunctionForStrategy(
            $$$"""
                会話の履歴：{{$history}}

                # 話す人の候補
                - {{{facilitator}}} : ファシリテーター
                - {{{AzureAgent}}} : Azureが大好きな人
                - {{{AwsAgent}}} : AWSが大好きな人

                # 会話の進行
                - 最初は{{{facilitator}}}から話を始めてください。
                - つぎに{{{AzureAgent}}}が話します。
                - もし、{{{AzureAgent}}}が話終わったら{{{AwsAgent}}}が話します。
                - もし、{{{AwsAgent}}}が話終わったら{{{facilitator}}}が話します。


                議論が尽きるまでとことん話し合ってください。

                """, safeParameterNames: "history"), kernel2)
        {
            InitialAgent = facilitator,
            HistoryReducer = historyTruncationReducer,
            HistoryVariableName = "history",
            UseInitialAgentAsFallback = true
        },
        TerminationStrategy = new KernelFunctionTerminationStrategy(terminationFunction, kernel)
        {
            Agents = [facilitator],
            MaximumIterations = 30,
            HistoryReducer = historyTruncationReducer,
            ResultParser = result => result.GetValue<string>() == "Done",
            HistoryVariableName = "history",
        }
    }
};

ChatHistory history = [];
//chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, """
//    .NETアプリケーションをうごかす上で様々な角度からAzureとAWSのどちらが有利かについて議論してください。
//    どちらが有利かを示す5つの例を提示してください。
//    その上で、お互いにすべての論点について具体的な例をもって反論してください。
//    そして、とことん話し合ってください。
//    """));
await foreach (var message in chat.InvokeStreamingAsync())
{
    //history.Add(message);
    //Console.WriteLine($"{message.AuthorName!}: {message.Role} {message}");
    Console.Write(message);
}
#pragma warning restore SKEXP0110 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。

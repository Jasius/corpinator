using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CorpinatorBot
{
    public class Program
    {
        private static CancellationTokenSource _cts;
        static async Task Main(string[] args)
        {
            try
            {
                _cts = new CancellationTokenSource();
                Console.CancelKeyPress += OnCancelKeyPress;

                var configBuilder = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", true)
                    .AddEnvironmentVariables();


                if (Debugger.IsAttached) //todo: detect somehow collect hosting environment
                {
                    configBuilder.AddUserSecrets("CorpinatorBot");
                }

                var builtConfig = configBuilder.Build();
                var secrets = new BotSecretsConfig();
                
                builtConfig.Bind(nameof(BotSecretsConfig), secrets);
                configBuilder.AddAzureKeyVault(secrets.AkvVault, secrets.AkvClientId, secrets.AkvSecret);
                
                builtConfig = configBuilder.Build();
                builtConfig.Bind(nameof(BotSecretsConfig), secrets);

                var storageClient = CloudStorageAccount.Parse(secrets.TableStorageConnectionString);
                var tableClient = storageClient.CreateCloudTableClient();

                var verificationsReference = tableClient.GetTableReference("verifications");
                await verificationsReference.CreateIfNotExistsAsync();
                var configurationReference = tableClient.GetTableReference("configuration");
                await configurationReference.CreateIfNotExistsAsync();

                var services = new ServiceCollection();
                services.AddSingleton(tableClient);

                var host = new DiscordBotHost()
                    .WithConfiguration(configBuilder)
                    .WithBinding<DiscordSocketConfig>()
                    .WithBinding<BotSecretsConfig>()
                    .WithLogging(a => a.AddConsole().AddDebug())
                    .WithServices(services)
                    .WithDiscordBot<DiscordBot>();

                await host.Run(_cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                if(Debugger.IsAttached) {
                    Console.ReadKey();
                }
                Environment.Exit(ex.HResult);
            }
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _cts.Cancel(true);
        }
    }
}

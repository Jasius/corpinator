using CorpinatorBot.ConfigModels;
using CorpinatorBot.Discord;
using CorpinatorBot.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.IO;
using System.Threading.Tasks;

namespace CorpinatorBot
{
    public class Program
    {
        private static async Task Main(string[] args)
        {
            IHostBuilder hostBuilder = new HostBuilder()
            .ConfigureHostConfiguration(config =>
            {
                config.AddEnvironmentVariables("BOTHOSTING:");
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", false)
                .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", true)
                .AddEnvironmentVariables();

                if (context.HostingEnvironment.IsDevelopment())
                {
                    config.AddUserSecrets("CorpinatorBot");
                }

                BotSecretsConfig secrets = new BotSecretsConfig();
                IConfigurationRoot builtConfig;
                if (context.HostingEnvironment.IsProduction())
                {
                    builtConfig = config.Build();
                    builtConfig.Bind(nameof(BotSecretsConfig), secrets);
                    config.AddAzureKeyVault(secrets.AkvVault, secrets.AkvClientId, secrets.AkvSecret);
                }

                // build and bind a second time to populate secrets found in AKV
                builtConfig = config.Build();
                builtConfig.Bind(nameof(BotSecretsConfig), secrets);

                context.Properties.Add("botSecrets", secrets);
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.AddConsole();
                if (context.HostingEnvironment.IsDevelopment())
                {
                    logging.AddDebug();
                }
            })
            .ConfigureServices((context, services) =>
            {
                BotSecretsConfig secrets = context.Properties["botSecrets"] as BotSecretsConfig;
                var socketConfig = new DiscordSocketConfig();
                context.Configuration.Bind(socketConfig);
                services.AddSingleton(socketConfig);

                CloudTableClient tableClient = InitializeStorage(secrets).ConfigureAwait(false).GetAwaiter().GetResult();
                services.AddSingleton(tableClient);
                services.AddSingleton(secrets);
                RegisterDiscordClient(services, secrets, socketConfig);

                services.AddTransient<IVerificationService, AzureVerificationService>();

                services.AddHostedService<DiscordBot>();
            })
            .UseConsoleLifetime();

            await hostBuilder.RunConsoleAsync();

        }

        private static void RegisterDiscordClient(IServiceCollection services, BotSecretsConfig config, DiscordSocketConfig socketConfig)
        {
            DiscordSocketClient client = new DiscordSocketClient(socketConfig);

            services.AddSingleton<IDiscordClient>(client);
            services.AddSingleton(client);
        }

        private static async Task<CloudTableClient> InitializeStorage(BotSecretsConfig secrets)
        {
            CloudStorageAccount storageClient = CloudStorageAccount.Parse(secrets.TableStorageConnectionString);
            CloudTableClient tableClient = storageClient.CreateCloudTableClient();

            CloudTable verificationsReference = tableClient.GetTableReference("verifications");
            await verificationsReference.CreateIfNotExistsAsync();
            CloudTable configurationReference = tableClient.GetTableReference("configuration");
            await configurationReference.CreateIfNotExistsAsync();
            return tableClient;
        }
    }
}

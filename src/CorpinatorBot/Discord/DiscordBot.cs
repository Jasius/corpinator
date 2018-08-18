using CorpinatorBot.ConfigModels;
using CorpinatorBot.Extensions;
using CorpinatorBot.Services;
using CorpinatorBot.VerificationModels;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CorpinatorBot.Discord
{
    public class DiscordBot : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DiscordBot> _logger;
        private readonly BotSecretsConfig _connectionConfig;
        private readonly CloudTable _table;
        private readonly DiscordSocketConfig _botConfig;
        private readonly DiscordSocketClient _discordClient;
        private readonly Timer _cleanupTimer;


        private CommandService _commandService;
        private bool _exiting;

        public DiscordBot(IServiceProvider serviceProvider, ILogger<DiscordBot> logger, DiscordSocketConfig botConfig, BotSecretsConfig connectionConfig, CloudTableClient tableClient)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _connectionConfig = connectionConfig;
            _table = tableClient.GetTableReference("configuration");
            _botConfig = botConfig;
            _discordClient = new DiscordSocketClient(botConfig);
            _cleanupTimer = new Timer(CleanupUsers, null, Timeout.Infinite, Timeout.Infinite);
        }

        public Task StartAsync(CancellationToken cancellationToken) => StartBotAsync();

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _exiting = true;
            _cleanupTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _cleanupTimer.Dispose();

            return StopBotAsync();
        }

        public async Task StartBotAsync()
        {
            _discordClient.Ready += OnReady;
            _discordClient.Log += OnLog;
            _discordClient.Disconnected += OnDisconnected;

            try
            {
                await ConnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to connect.");
                throw;
            }
        }

        public async Task StopBotAsync()
        {
            _logger.LogInformation("Received stop signal, shutting down.");
            await _discordClient.StopAsync();
            await _discordClient.LogoutAsync();
        }

        private async Task ConnectAsync()
        {
            var maxAttempts = 10;
            var currentAttempt = 0;
            do
            {
                currentAttempt++;
                try
                {
                    await _discordClient.LoginAsync(TokenType.Bot, _connectionConfig.BotToken);
                    await _discordClient.StartAsync();
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Fialed to connect: {ex.Message}");
                    await Task.Delay(currentAttempt * 1000);
                }
            }
            while (currentAttempt < maxAttempts);
        }

        private Task OnDisconnected(Exception arg)
        {
            if (!_exiting)
            {
                Environment.Exit(0);
            }
            return Task.CompletedTask;
        }

        private Task OnReady()
        {
            _discordClient.MessageReceived += OnMessageReceived;
            _commandService = new CommandService(new CommandServiceConfig
            {
                LogLevel = _botConfig.LogLevel,
                SeparatorChar = ' ',
                ThrowOnError = true
            });

            _commandService.AddModulesAsync(Assembly.GetExecutingAssembly());
            _cleanupTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromDays(1));
            return Task.CompletedTask;
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot)
            {
                return;
            }

            if (!(message is SocketUserMessage userMessage))
            {
                return;
            }

            if (!(message.Channel is SocketGuildChannel guildChannel))
            {
                return;
            }

            var argPos = 0;

            var guildId = guildChannel.Guild.Id.ToString();

            GuildConfiguration config;
            var configResult = await _table.ExecuteAsync(TableOperation.Retrieve<GuildConfiguration>("config", guildId));
            if (configResult.Result == null)
            {
                config = new GuildConfiguration
                {
                    PartitionKey = "config",
                    RowKey = guildId,
                    Prefix = "!",
                    RequiresOrganization = false,
                    Organization = string.Empty,
                    RoleId = default
                };
            }
            else
            {
                config = configResult.Result as GuildConfiguration;
            }

            if (!userMessage.HasStringPrefix(config.Prefix, ref argPos))
            {
                return;
            }

            var context = new GuildConfigSocketCommandContext(_discordClient, userMessage, config);

            var result = await _commandService.ExecuteAsync(context, argPos, _serviceProvider, MultiMatchHandling.Best);

            if (!result.IsSuccess && (result.Error != CommandError.UnknownCommand || result.Error != CommandError.BadArgCount))
            {
                _logger.LogError($"{result.Error}: {result.ErrorReason}");
                await userMessage.AddReactionAsync(new Emoji("⚠"));
            }
        }

        private Task OnLog(LogMessage arg)
        {
            var severity = MapToLogLevel(arg.Severity);
            _logger.Log(severity, 0, arg, arg.Exception, (state, ex) => state.ToString());
            return Task.CompletedTask;
        }

        private LogLevel MapToLogLevel(LogSeverity severity)
        {
            switch (severity)
            {
                case LogSeverity.Critical:
                    return LogLevel.Critical;
                case LogSeverity.Error:
                    return LogLevel.Error;
                case LogSeverity.Warning:
                    return LogLevel.Warning;
                case LogSeverity.Info:
                    return LogLevel.Information;
                case LogSeverity.Verbose:
                    return LogLevel.Trace;
                case LogSeverity.Debug:
                    return LogLevel.Debug;
                default:
                    return LogLevel.Information;
            }
        }

        private async void CleanupUsers(object state)
        {
            _logger.LogInformation("Begin clean up of users");
            try
            {
                var tableClient = _serviceProvider.GetRequiredService<CloudTableClient>();
                var verificationService = _serviceProvider.GetRequiredService<IVerificationService>();

                var verificationsTable = tableClient.GetTableReference("verifications");
                var configTable = tableClient.GetTableReference("configuration");
                var verifications = await verificationsTable.GetAllRecords<Verification>();
                var guilds = await configTable.GetAllRecords<GuildConfiguration>();

                foreach (var config in guilds)
                {
                    var guildUsers = verifications.Where(a => a.PartitionKey == config.RowKey);

                    foreach (var guildUser in guildUsers)
                    {
                        var exists = await verificationService.VerifyUser(guildUser, config);

                        if (!exists)
                        {
                            _logger.LogWarning($"{guildUser.Alias} is either no longer with the company, or no longer reports to {config.Organization}, about to remove verification role and storage.");
                            //todo: remove from role, remove from table
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // to avoid crashing the whole thing because async void heressy
                _logger.LogError(ex, $"Error while running the {nameof(CleanupUsers)} background job");
            }
            _logger.LogInformation("Done cleaning up users");
        }
    }
}

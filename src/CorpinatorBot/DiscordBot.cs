using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace CorpinatorBot
{
    public class DiscordBot : IDiscordBot
    {
        private IServiceProvider _serviceProvider;
        private ILogger<DiscordBot> _logger;
        private BotSecretsConfig _connectionConfig;
        private CloudTable _table;
        private DiscordSocketConfig _botConfig;
        private DiscordSocketClient _discordClient;
        private CommandService _commandService;

        public DiscordBot(IServiceProvider serviceProvider, ILogger<DiscordBot> logger, DiscordSocketConfig botConfig, BotSecretsConfig connectionConfig, CloudTableClient tableClient)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _connectionConfig = connectionConfig;
            _table = tableClient.GetTableReference("configuration");
            _botConfig = botConfig;
            _discordClient = new DiscordSocketClient(botConfig);
            _discordClient.Log += OnLog;
            _discordClient.Ready += OnReady;
            _discordClient.Disconnected += OnDisconnected;
        }

        private Task OnDisconnected(Exception arg)
        {
            Environment.Exit(0);
            return Task.CompletedTask;
        }

        public async Task Start()
        {
            await _discordClient.LoginAsync(TokenType.Bot, _connectionConfig.BotToken);
            await _discordClient.StartAsync();

            //start rank check timer
        }

        public async Task Stop()
        {
            _logger.LogInformation("Received stop signal, shutting down.");
            await _discordClient.StopAsync();
            await _discordClient.LogoutAsync();
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

            int argPos = 0;

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

            if (!result.IsSuccess)
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
    }
}

using CorpinatorBot.TableModels;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CorpinatorBot
{
    [Group("teamxbox"), Alias("microsoft")]
    public class TeamXboxModule : ModuleBase<GuildConfigSocketCommandContext>
    {
        private CloudTable _table;
        private CloudTable _configurationTable;
        private BotSecretsConfig _secretsConfig;
        private ILogger<TeamXboxModule> _logger;

        public TeamXboxModule(CloudTableClient tableClient, BotSecretsConfig secretsConfig, ILogger<TeamXboxModule> logger)
        {
            _table = tableClient.GetTableReference("verifications");
            _configurationTable = tableClient.GetTableReference("configuration");
            _secretsConfig = secretsConfig;
            _logger = logger;
        }

        [Command("verify", RunMode = RunMode.Async)]
        public async Task Verify()
        {
            if (!(Context.User is SocketGuildUser guildUser))
            {
                return;
            }

            var userId = guildUser.Id.ToString();
            var guildId = Context.Guild.Id.ToString();
            var verificationResult = await _table.ExecuteAsync(TableOperation.Retrieve<Verification>(guildId, userId));

            if (verificationResult.HttpStatusCode == 200)
            {
                await ReplyAsync("You are already verified in this server.");
                return;
            }

            var verification = new Verification { PartitionKey = guildId, RowKey = userId };

            var authContext = new AuthenticationContext("https://login.microsoftonline.com/microsoft.onmicrosoft.com");
            var code = await authContext.AcquireDeviceCodeAsync("https://graph.microsoft.com", _secretsConfig.DeviceAuthAppId);

            var dmChannel = await Context.User.GetOrCreateDMChannelAsync();
            try
            {
                //do the thing to get the stuff;
                await dmChannel.SendMessageAsync(code.Message);
                await ReplyAsync($"{Context.User.Mention}, check your DMs for instructions");
            }
            catch (HttpException ex) when (ex.DiscordCode == 50007)
            {
                await ReplyAsync($"{Context.User.Mention}, Please temporarily allow DMs from this server and try again.");
                return;
            }

            try
            {
                var result = await authContext.AcquireTokenByDeviceCodeAsync(code);

                var corpUserId = Guid.Parse(result.UserInfo.UniqueId);
                var alias = result.UserInfo.DisplayableId;

                verification.CorpUserId = corpUserId;
                verification.StatusMessage = "verified";
                verification.ValidatedOn = DateTimeOffset.UtcNow;
                verification.Validated = true;

                var mergeResult = await _table.ExecuteAsync(TableOperation.InsertOrMerge(verification));

                await dmChannel.SendMessageAsync($"Thanks for validating your status with Microsoft, {alias}");
                var role = Context.Guild.Roles.SingleOrDefault(a => a.Id == ulong.Parse(Context.Configuration.RoleId));
                await guildUser.AddRoleAsync(role);

            }
            catch (Exception ex)
            {
                await dmChannel.SendMessageAsync("An error occurred saving your validation. Please try again later.");
                _logger.LogCritical(ex, ex.Message);
            }
        }

        [Command("leave")]
        public async Task Leave()
        {
            var userId = Context.User.Id.ToString();
            var guildId = Context.Guild.Id.ToString();
            var verificationResult = await _table.ExecuteAsync(TableOperation.Retrieve<Verification>(guildId, userId));

            if (verificationResult.HttpStatusCode == 200)
            {
                var deleteResult = await _table.ExecuteAsync(TableOperation.Delete(verificationResult.Result as Verification));
                await ReplyAsync("Your verified status has been removed");
            }
            else
            {
                await ReplyAsync("You are not currently validated");
            }
        }

        [Command("setrole"), RequireOwner]
        public async Task ConfigureRole(IRole role)
        {
            var guildRole = Context.Guild.Roles.SingleOrDefault(a => a.Id == role.Id);

            if (guildRole == null)
            {
                await ReplyAsync("That role does not exist on this server.");
                return;
            }

            Context.Configuration.RoleId = guildRole.Id.ToString();

            var result = await _configurationTable.ExecuteAsync(TableOperation.InsertOrReplace(Context.Configuration));
            await ReplyAsync($"Role {guildRole.Name} saved.");
        }

        [Command("setprefix"), RequireOwner]
        public async Task SetPrefix(string prefix)
        {
            Context.Configuration.Prefix = prefix;

            await _configurationTable.ExecuteAsync(TableOperation.InsertOrReplace(Context.Configuration));
            await ReplyAsync($"The prefix is now {prefix}");
        }

    }
}

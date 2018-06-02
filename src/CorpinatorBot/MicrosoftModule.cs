using CorpinatorBot.TableModels;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CorpinatorBot
{
    [Group("microsoft"), Alias("teamxbox")]
    public class MicrosoftModule : ModuleBase<GuildConfigSocketCommandContext>
    {
        private CloudTable _verificationTable;
        private CloudTable _configurationTable;
        private BotSecretsConfig _secretsConfig;
        private ILogger<MicrosoftModule> _logger;

        public MicrosoftModule(CloudTableClient tableClient, BotSecretsConfig secretsConfig, ILogger<MicrosoftModule> logger)
        {
            _verificationTable = tableClient.GetTableReference("verifications");
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
            var verificationResult = await _verificationTable.ExecuteAsync(TableOperation.Retrieve<Verification>(guildId, userId));

            if (verificationResult.HttpStatusCode == 200)
            {
                await ReplyAsync("You are already verified in this server.");
                return;
            }

            var verification = new Verification { PartitionKey = guildId, RowKey = userId };

            var authContext = new AuthenticationContext($"https://login.microsoftonline.com/{_secretsConfig.AadTenant}");
            var code = await authContext.AcquireDeviceCodeAsync("https://graph.microsoft.com", _secretsConfig.DeviceAuthAppId);

            var dmChannel = await Context.User.GetOrCreateDMChannelAsync();
            try
            {
                await dmChannel.SendMessageAsync("After you authenticate with your corp account, we will collect and store your department, alias, " +
                " and your corp user id. This data will only be used to validate your current status for the purpose of managing the verified role on this server.");
                await dmChannel.SendMessageAsync(code.Message);
                await ReplyAsync($"{Context.User.Mention}, check your DMs for instructions.");
            }
            catch (HttpException ex) when (ex.DiscordCode == 50007)
            {
                await ReplyAsync($"{Context.User.Mention}, Please temporarily allow DMs from this server and try again.");
                return;
            }

            try
            {
                var result = await authContext.AcquireTokenByDeviceCodeAsync(code);

                var (inOrg, alias, department) = await GetUserDetails(result, Context.Configuration.Organization);

                if (Context.Configuration.RequiresOrganization && !inOrg)
                {
                    await dmChannel.SendMessageAsync($"We see that you are a current Microsoft employee, however, this server requires that you be in a specific org in order to receive the validated status.");
                    return;
                }

                if(result.UserInfo.DisplayableId.Substring(1,1) == "-" && result.UserInfo.DisplayableId.Substring(0, 2) != "t-")
                {
                    await dmChannel.SendMessageAsync($"Verification requires that you be a full time Microsoft employee.");
                    return;
                }

                var corpUserId = Guid.Parse(result.UserInfo.UniqueId);

                verification.CorpUserId = corpUserId;
                verification.Alias = alias;
                verification.ValidatedOn = DateTimeOffset.UtcNow;
                verification.Department = department;

                var mergeResult = await _verificationTable.ExecuteAsync(TableOperation.InsertOrMerge(verification));

                await dmChannel.SendMessageAsync($"Thanks for validating your status with Microsoft. You can unlink your accounts at any time with the `{Context.Configuration.Prefix}microsoft leave` command.");
                var role = Context.Guild.Roles.SingleOrDefault(a => a.Id == ulong.Parse(Context.Configuration.RoleId));
                await guildUser.AddRoleAsync(role);
            }
            catch(AdalServiceException ex) when (ex.ErrorCode == "code_expired")
            {
                _logger.LogInformation($"Code expired for {Context.User.Username}#{Context.User.Discriminator}");
                await dmChannel.SendMessageAsync("Your code has expired.");
            }
            catch (Exception ex)
            {
                await dmChannel.SendMessageAsync("An error occurred saving your validation. Please try again later.");
                _logger.LogCritical(ex, ex.Message);
            }
        }

        private async Task<(bool isInOrg, string alias, string department)> GetUserDetails(AuthenticationResult result, string reportsTo)
        {
            var graph = new GraphServiceClient("https://graph.microsoft.com/beta", new GraphAuthenticationProvider(result));
            var user = await graph.Me.Request().GetAsync();
            var currentManager = result.UserInfo.UniqueId;

            if(string.IsNullOrWhiteSpace(reportsTo)) {
            return (false, user.MailNickname, user.Department);
            }

            while (true)
            {
                DirectoryObject manager;
                try
                {
                    manager = await graph.Users[currentManager].Manager.Request().GetAsync();
                    currentManager = manager.Id;
                    var tlmUser = await graph.Users[currentManager].Request().GetAsync();
                    
                    if (tlmUser.MailNickname.Equals(reportsTo, StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, user.MailNickname, user.Department);
                    }
                }
                catch (ServiceException ex) when (ex.Error.Code == "Request_ResourceNotFound" && ex.Error.Message.Contains("manager"))
                {
                    break;
                }
            }

            return (false, user.MailNickname, user.Department);            
        }

        [Command("leave")]
        public async Task Leave()
        {
            if (!(Context.User is SocketGuildUser guildUser))
            {
                return;
            }

            var userId = guildUser.Id.ToString();
            var guildId = Context.Guild.Id.ToString();
            var verificationResult = await _verificationTable.ExecuteAsync(TableOperation.Retrieve<Verification>(guildId, userId));

            if (verificationResult.HttpStatusCode == 200)
            {
                var deleteResult = await _verificationTable.ExecuteAsync(TableOperation.Delete(verificationResult.Result as Verification));

                if (deleteResult.HttpStatusCode == 204)
                {
                    var role = Context.Guild.GetRole(ulong.Parse(Context.Configuration.RoleId));
                    await guildUser.RemoveRoleAsync(role);

                    await ReplyAsync("Your verified status has been removed.");
                }
                else
                {
                    await ReplyAsync($"Unable to remove your verification.");
                }
            }
            else
            {
                await ReplyAsync("You are not currently verified.");
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
            await ReplyAsync($"Role is now set to {guildRole.Name}.");
        }

        [Command("setprefix"), RequireOwner]
        public async Task SetPrefix(string prefix)
        {
            Context.Configuration.Prefix = prefix;

            await _configurationTable.ExecuteAsync(TableOperation.InsertOrReplace(Context.Configuration));
            await ReplyAsync($"Prefix is now set to {prefix}.");
        }

        [Command("requireorg"), RequireOwner]
        public async Task RequireOrg(bool require)
        {
            Context.Configuration.RequiresOrganization = require;

            await _configurationTable.ExecuteAsync(TableOperation.InsertOrReplace(Context.Configuration));
            await ReplyAsync($"RequireOrganization now set to {require}.");
        }

        [Command("setorg"), RequireOwner]
        public async Task SetOrg(string alias)
        {
            Context.Configuration.Organization = alias;

            await _configurationTable.ExecuteAsync(TableOperation.InsertOrReplace(Context.Configuration));
            await ReplyAsync($"Organization now set to {alias}.");
        }

        [Command("settings"), RequireOwner]
        public async Task Settings()
        {
            var settings = JsonConvert.SerializeObject(Context.Configuration, Formatting.Indented);
            await ReplyAsync(Format.Code(settings));
        }

        [Command("query"), RequireOwner]
        public async Task List(IUser user)
        {
            var userId = user.Id.ToString();
            var guildId = Context.Guild.Id.ToString();
            var verificationResult = await _verificationTable.ExecuteAsync(TableOperation.Retrieve<Verification>(guildId, userId));

            if (verificationResult.HttpStatusCode == 200)
            {
                var userJson = JsonConvert.SerializeObject(verificationResult.Result, Formatting.Indented);
                await ReplyAsync(userJson);
            }
            else
            {
                await ReplyAsync($"{user.Username}#{user.DiscriminatorValue} is not a validated user.");
            }
        }

        [Command("remove"), RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task Remove(IUser user)
        {
            if (!(user is SocketGuildUser guildUser))
            {
                await ReplyAsync($"{user.Username}#{user.DiscriminatorValue} is not a valid user.");
                return;
            }

            var userId = guildUser.Id.ToString();
            var guildId = Context.Guild.Id.ToString();
            var verificationResult = await _verificationTable.ExecuteAsync(TableOperation.Retrieve<Verification>(guildId, userId));

            if (verificationResult.HttpStatusCode == 200)
            {
                var deleteResult = await _verificationTable.ExecuteAsync(TableOperation.Delete(verificationResult.Result as Verification));
                if (deleteResult.HttpStatusCode == 204)
                {
                    var role = Context.Guild.GetRole(uint.Parse(Context.Configuration.RoleId));
                    await guildUser.RemoveRoleAsync(role);
                    await ReplyAsync($"{user.Username}#{user.DiscriminatorValue} removed from validation.");
                }
                else
                {
                    await ReplyAsync($"Unable to remove verification for {user.Username}#{user.DiscriminatorValue}.");
                }
            }
            else
            {
                await ReplyAsync($"{user.Username}#{user.DiscriminatorValue} is not a validated user.");
            }

        }
    }
}

using CorpinatorBot.ConfigModels;
using CorpinatorBot.VerificationModels;
using Microsoft.Graph;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System;
using System.Threading.Tasks;

namespace CorpinatorBot.Services
{
    public class AzureVerificationService : IVerificationService
    {
        private readonly BotSecretsConfig _secretsConfig;
        private readonly AuthenticationContext _authContext;

        private AuthenticationResult _authResult = default;
        private DeviceCodeResult _deviceCode;

        public string UserId { get; private set; }
        public string Alias { get; private set; }
        public string Organization { get; private set; }
        public string Department { get; private set; }
        public UserType UserType { get; private set; }

        public AzureVerificationService(BotSecretsConfig secretsConfig)
        {
            _secretsConfig = secretsConfig;
            _authContext = new AuthenticationContext($"https://login.microsoftonline.com/{_secretsConfig.AadTenant}");
        }

        public async Task LoadUserDetails(string shouldReportTo)
        {
            if (_authResult == default)
            {
                throw new InvalidOperationException($"Auth result is unavailable. Make sure to call {nameof(VerifyCode)} before this method");
            }

            var graph = new GraphServiceClient("https://graph.microsoft.com/beta", new GraphAuthenticationProvider(_authResult));

            UserId = _authResult.UserInfo.UniqueId;

            var user = await graph.Me.Request().GetAsync();

            Department = user.Department;
            Alias = user.MailNickname;

            if (Alias.StartsWith("t-"))
            {
                UserType = UserType.Intern;
            }
            else if (Alias.Substring(1, 1) == "-")
            {
                UserType = UserType.Contractor;
            }
            else
            {
                UserType = UserType.FullTimeEmployee;
            }

            if (string.IsNullOrWhiteSpace(shouldReportTo))
            {
                return;
            }
            Organization = await GetOrg(UserId, shouldReportTo, graph);
        }

        public async Task<string> GetCode()
        {
            _deviceCode = await _authContext.AcquireDeviceCodeAsync("https://graph.microsoft.com", _secretsConfig.DeviceAuthAppId);
            return _deviceCode.Message;
        }

        public async Task VerifyCode()
        {
            if (_deviceCode == default)
            {
                throw new InvalidOperationException($"Device code unavailable. Make sure {nameof(GetCode)} is called before this method.");
            }

            try
            {
                _authResult = await _authContext.AcquireTokenByDeviceCodeAsync(_deviceCode);
            }
            catch (AdalServiceException ex) when (ex.ErrorCode == "code_expired")
            {
                throw new VerificationException(ex.Message, ex, ex.ErrorCode);
            }
        }

        public async Task<bool> VerifyUser(Verification verification, GuildConfiguration guild)
        {
            var botAuthResult = await _authContext.AcquireTokenAsync("https://graph.microsoft.com", new ClientCredential(_secretsConfig.AkvClientId, _secretsConfig.AkvSecret));

            var graph = new GraphServiceClient("https://graph.microsoft.com/beta", new GraphAuthenticationProvider(botAuthResult));

            try
            {
                var user = await graph.Users[verification.CorpUserId.ToString()].Request().GetAsync();
                if (!user.AccountEnabled ?? false)
                {
                    return false;
                }

                if (guild.RequiresOrganization)
                {
                    var org = GetOrg(verification.CorpUserId.ToString(), guild.Organization, graph);

                    if (org == null)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (ServiceException ex) when (ex.Error.Code == "Request_ResourceNotFound")
            {
                return false;
            }
        }

        private async Task<string> GetOrg(string userId, string shouldReportTo, GraphServiceClient graph)
        {
            var currentManager = userId;
            while (true)
            {
                DirectoryObject manager;
                try
                {
                    manager = await graph.Users[currentManager].Manager.Request().GetAsync();
                    currentManager = manager.Id;
                    var tlmUser = await graph.Users[currentManager].Request().GetAsync();

                    if (tlmUser.MailNickname.Equals(shouldReportTo, StringComparison.OrdinalIgnoreCase))
                    {
                        Organization = shouldReportTo;
                    }
                }
                catch (ServiceException ex) when (ex.Error.Code == "Request_ResourceNotFound" && ex.Error.Message.Contains("manager"))
                {
                    break;
                }
            }

            return currentManager == userId ? null : currentManager;
        }
    }
}

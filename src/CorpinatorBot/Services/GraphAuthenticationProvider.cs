using Microsoft.Graph;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace CorpinatorBot.Services
{
    class GraphAuthenticationProvider : IAuthenticationProvider
    {
        private AuthenticationResult _authresult;

        public GraphAuthenticationProvider(AuthenticationResult adalAuthResult) {
            _authresult = adalAuthResult;
        }

        public Task AuthenticateRequestAsync(HttpRequestMessage request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(_authresult.AccessTokenType, _authresult.AccessToken);

            return Task.CompletedTask;
        }
    }
}

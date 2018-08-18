using CorpinatorBot.ConfigModels;
using CorpinatorBot.VerificationModels;
using System.Threading.Tasks;

namespace CorpinatorBot.Services
{
    public interface IVerificationService
    {
        string UserId { get; }
        string Alias { get;  }
        string Organization { get; }
        string Department { get; }
        UserType UserType { get; }

        Task LoadUserDetails(string shouldReportTo);
        Task<string> GetCode();
        Task VerifyCode();
        Task<bool> VerifyUser(Verification verification, GuildConfiguration guild);
    }
}
using System.Threading.Tasks;

namespace CorpinatorBot
{
    public interface IDiscordBot
    {
        Task Start();
        Task Stop();
    }
}
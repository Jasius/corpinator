using Microsoft.WindowsAzure.Storage.Table;

namespace CorpinatorBot
{
    public class GuildConfiguration : TableEntity
    {
        public string Prefix { get; set; }
        public string RoleId { get; set; }
        public bool RequiresOrginization { get; set; }
        public string Organization { get; set; }
    }
}
using CorpinatorBot.VerificationModels;
using Microsoft.WindowsAzure.Storage.Table;

namespace CorpinatorBot.ConfigModels
{
    public class GuildConfiguration : TableEntity
    {
        public string Prefix { get; set; }
        public string RoleId { get; set; }
        public int AllowedUserTypes { get; set; }
        public bool RequiresOrganization { get; set; }
        public string Organization { get; set; }

        [IgnoreProperty]
        public UserType AllowedUserTypesFlag
        {
            get => (UserType)AllowedUserTypes;
            set => AllowedUserTypes = (int)value;
        }
    }
}
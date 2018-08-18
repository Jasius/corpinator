using Microsoft.WindowsAzure.Storage.Table;
using System;

namespace CorpinatorBot.VerificationModels
{
    public class Verification : TableEntity
    {
        public Guid CorpUserId { get; set; }
        public string Alias { get; set; }
        public DateTimeOffset? ValidatedOn { get; set; }
        public string Department { get; set; }

    }
}

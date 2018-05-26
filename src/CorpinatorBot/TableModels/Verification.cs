using Microsoft.WindowsAzure.Storage.Table;
using System;

namespace CorpinatorBot.TableModels
{
    class Verification : TableEntity
    {
        public Guid CorpUserId { get; set; }
        public string Organization { get; set; }
        public string StatusMessage { get; set; }
        public bool Validated { get; set; }
        public DateTimeOffset? ValidatedOn { get; set; }

    }
}

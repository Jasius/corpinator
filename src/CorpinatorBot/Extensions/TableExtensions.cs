using Microsoft.WindowsAzure.Storage.Table;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CorpinatorBot.Extensions
{
    public static class TableExtensions
    {
        public static async Task<List<T>> GetAllRecords<T>(this CloudTable table) where T: TableEntity, new ()
        {
            var data = new List<T>();
            TableContinuationToken token = default;
            do
            {
                var segment = await table.ExecuteQuerySegmentedAsync(new TableQuery<T>(), token);
                data.AddRange(segment.Results);

                token = segment.ContinuationToken;
            }
            while (token != default);

            return data;
        }
    }
}

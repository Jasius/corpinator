namespace CorpinatorBot.ConfigModels
{
    public class BotSecretsConfig
    {
        public string BotToken { get; set; }
        public string AkvSecret { get; set; }
        public string AkvClientId { get; set; }
        public string AkvVault { get; set; }
        public string TableStorageConnectionString { get; set; }
        public string DeviceAuthAppId { get; set; }
        public object AadTenant { get; set; }
    }
}
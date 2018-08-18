namespace CorpinatorBot.VerificationModels
{
    public class VerifiedUserDetails
    {
        public string UserId { get; internal set; }
        public UserType UserType { get; internal set; }
        public string Organization { get; internal set; }
        public string Alias { get; internal set; }
        public string Department { get; internal set; }
    }
}
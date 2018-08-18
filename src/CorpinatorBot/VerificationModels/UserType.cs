using System;

namespace CorpinatorBot.VerificationModels
{
    [Flags]
    public enum UserType
    {
        None = 0,
        FullTimeEmployee = 1,
        Intern = 2,
        Contractor = 4
    }
}
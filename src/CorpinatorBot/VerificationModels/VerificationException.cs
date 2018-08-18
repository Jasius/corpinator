using System;

namespace CorpinatorBot.VerificationModels
{
    public class VerificationException : Exception
    {
        public string ErrorCode { get; private set; }

        public VerificationException()
        {
        }

        public VerificationException(string errorCode)
        {
            ErrorCode = errorCode;
        }

        public VerificationException(string message, string errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public VerificationException(string message, Exception innerException, string errorCode) : base(message, innerException)
        {
            ErrorCode = ErrorCode;
        }
    }
}
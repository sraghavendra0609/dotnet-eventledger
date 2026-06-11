namespace EventGateway.Application.Exceptions;

public sealed class AccountServiceUnavailableException : Exception
{
    public AccountServiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

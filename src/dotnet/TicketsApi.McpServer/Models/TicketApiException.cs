namespace TicketsApi.McpServer.Models;

public class TicketApiException : Exception
{
    public int StatusCode { get; }

    public TicketApiException(string message, int statusCode = 0) : base(message)
    {
        StatusCode = statusCode;
    }

    public TicketApiException(string message, Exception inner, int statusCode = 0) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

using System.ComponentModel.DataAnnotations;

namespace AgenticResolution.Api.Models;

public class TicketComment
{
    public Guid Id { get; set; }
    public Guid TicketId { get; set; }

    [MaxLength(100)]
    public string Author { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool IsInternal { get; set; }

    public DateTime CreatedAt { get; set; }

    public Ticket? Ticket { get; set; }
}

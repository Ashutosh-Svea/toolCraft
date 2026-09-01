namespace IncidentSandbox.Data;

/// <summary>
/// The complete in-memory dataset the sandbox serves: services, telemetry, and
/// tickets, all invented and generated deterministically from a seed and an anchor
/// instant. The anchor acts as the sandbox's frozen "now" so demo runs reproduce
/// exactly no matter when the tools are called.
/// </summary>
public sealed class SandboxDataset
{
    /// <summary>The frozen "now" every relative time in the dataset hangs off.</summary>
    public required DateTimeOffset Anchor { get; init; }

    /// <summary>The seed the dataset was generated from.</summary>
    public required int Seed { get; init; }

    /// <summary>All services, in a stable order.</summary>
    public required IReadOnlyList<ServiceInfo> Services { get; init; }

    /// <summary>All telemetry events, ordered by timestamp ascending.</summary>
    public required IReadOnlyList<TelemetryEvent> Telemetry { get; init; }

    /// <summary>All tickets, ordered by opened time descending (newest first).</summary>
    public required IReadOnlyList<Ticket> Tickets { get; init; }

    /// <summary>All service names, in the same stable order as <see cref="Services"/>.</summary>
    public IReadOnlyList<string> ServiceNames => _serviceNames ??= Services.Select(s => s.Name).ToArray();

    /// <summary>All ticket ids, newest first.</summary>
    public IReadOnlyList<string> TicketIds => _ticketIds ??= Tickets.Select(t => t.Id).ToArray();

    private string[]? _serviceNames;
    private string[]? _ticketIds;

    /// <summary>Finds a ticket by id, case insensitive, or null.</summary>
    public Ticket? FindTicket(string ticketId)
        => Tickets.FirstOrDefault(t => string.Equals(t.Id, ticketId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a service by name, case insensitive, or null.</summary>
    public ServiceInfo? FindService(string name)
        => Services.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The service names related to a ticket for correlation purposes: the ticket's own
    /// services, everything they depend on, and everything that depends on them.
    /// </summary>
    public IReadOnlyList<string> RelatedServices(Ticket ticket)
    {
        var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ticket.Services)
        {
            related.Add(name);
            var service = FindService(name);
            if (service is null)
            {
                continue;
            }

            foreach (var dependency in service.DependsOn)
            {
                related.Add(dependency);
            }

            foreach (var dependent in Services.Where(s => s.DependsOn.Contains(name, StringComparer.OrdinalIgnoreCase)))
            {
                related.Add(dependent.Name);
            }
        }

        return Services.Select(s => s.Name).Where(related.Contains).ToArray();
    }
}

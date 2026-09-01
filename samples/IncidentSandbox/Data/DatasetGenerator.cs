namespace IncidentSandbox.Data;

/// <summary>
/// Generates the invented sandbox dataset: 10 services, 200 telemetry events, and
/// 30 tickets, including three scripted incident arcs. Generation is fully
/// deterministic: the same seed and anchor always produce byte-identical data,
/// down to ids, timestamps, and message text, so demos reproduce exactly.
/// </summary>
public static class DatasetGenerator
{
    /// <summary>The seed the sandbox server uses unless overridden.</summary>
    public const int DefaultSeed = 7;

    /// <summary>Number of background (non-arc) telemetry events.</summary>
    private const int NoiseEventCount = 150;

    /// <summary>Number of background (non-arc) tickets.</summary>
    private const int NoiseTicketCount = 23;

    /// <summary>
    /// Generates the full dataset from a seed and an anchor instant. The anchor is the
    /// dataset's frozen "now"; all timestamps are placed relative to it, spanning
    /// roughly the trailing 24 hours for telemetry and 72 hours for tickets.
    /// </summary>
    /// <param name="seed">Seed for the deterministic random source.</param>
    /// <param name="anchor">The instant that acts as "now" for the dataset.</param>
    public static SandboxDataset Generate(int seed, DateTimeOffset anchor)
    {
        var random = new DeterministicRandom(seed);
        var services = BuildServices();
        var serviceNames = services.Select(s => s.Name).ToArray();

        var events = new List<TelemetryEvent>(NoiseEventCount + 50);
        AddNoiseTelemetry(events, random, serviceNames, anchor);
        AddCertExpiryArc(events, anchor);
        AddBadDeployArc(events, anchor);
        AddQueueBacklogArc(events, anchor);

        // Ids are assigned in timestamp order so event numbering reads naturally.
        var orderedEvents = events
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Service, StringComparer.Ordinal)
            .ThenBy(e => e.Message, StringComparer.Ordinal)
            .Select((e, index) => e with { Id = $"EVT-{index + 1:0000}" })
            .ToArray();

        var tickets = new List<Ticket>(NoiseTicketCount + 7);
        AddNoiseTickets(tickets, random, anchor);
        AddArcTickets(tickets, anchor);

        // Ticket numbers increase with opened time, oldest first, like a real queue.
        var numbered = tickets
            .OrderBy(t => t.OpenedAt)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .Select((t, index) => t with { Id = $"TCK-{index + 1001}" })
            .OrderByDescending(t => t.OpenedAt)
            .ToArray();

        return new SandboxDataset
        {
            Anchor = anchor,
            Seed = seed,
            Services = services,
            Telemetry = orderedEvents,
            Tickets = numbered,
        };
    }

    private static IReadOnlyList<ServiceInfo> BuildServices()
        =>
        [
            new() { Name = "gateway", Description = "Public API gateway and request router.", DependsOn = ["auth", "checkout", "checkout-v2", "catalog", "search"] },
            new() { Name = "auth", Description = "Sign-in, sessions, and token issuing." },
            new() { Name = "checkout", Description = "Order placement flow, current version.", DependsOn = ["payments", "queue-broker"] },
            new() { Name = "checkout-v2", Description = "Order placement flow, next version behind a flag.", DependsOn = ["payments", "queue-broker"] },
            new() { Name = "payments", Description = "Card authorization and capture." },
            new() { Name = "catalog", Description = "Product data and pricing." },
            new() { Name = "search", Description = "Product search and suggestions.", DependsOn = ["catalog"] },
            new() { Name = "notifications", Description = "Customer-facing email and push notifications.", DependsOn = ["queue-broker"] },
            new() { Name = "email-worker", Description = "Background consumer that renders and sends queued email.", DependsOn = ["queue-broker"] },
            new() { Name = "queue-broker", Description = "Internal message queue between services." },
        ];

    private static void AddNoiseTelemetry(
        List<TelemetryEvent> events,
        DeterministicRandom random,
        IReadOnlyList<string> serviceNames,
        DateTimeOffset anchor)
    {
        string[] debugMessages =
        [
            "cache warm completed in {0} ms",
            "gc pause of {0} ms observed",
            "configuration reloaded without changes",
            "healthcheck passed",
        ];
        string[] infoMessages =
        [
            "request completed in {0} ms",
            "scheduled job finished in {0} ms",
            "connection pool utilization at {0} percent",
            "background sync completed",
        ];
        string[] warningMessages =
        [
            "retry 1 of 3 for downstream call after {0} ms",
            "slow query took {0} ms",
            "connection pool utilization above 80 percent",
        ];
        string[] errorMessages =
        [
            "request failed with timeout after {0} ms",
            "failed to refresh feature flags, using cached values",
        ];

        for (var i = 0; i < NoiseEventCount; i++)
        {
            var service = random.Pick(serviceNames);
            var minutesAgo = random.Next(0, 24 * 60);
            var secondsJitter = random.Next(0, 60);
            var roll = random.Next(0, 100);
            var (level, template) = roll switch
            {
                < 25 => (Severity.Debug, random.Pick(debugMessages)),
                < 75 => (Severity.Info, random.Pick(infoMessages)),
                < 92 => (Severity.Warning, random.Pick(warningMessages)),
                _ => (Severity.Error, random.Pick(errorMessages)),
            };
            var number = random.Next(3, 4800);

            events.Add(new TelemetryEvent
            {
                Id = "pending",
                Service = service,
                Timestamp = anchor.AddMinutes(-minutesAgo).AddSeconds(-secondsJitter),
                Level = level,
                Message = string.Format(null, template, number),
            });
        }
    }

    /// <summary>Arc 1, ongoing: an expired TLS certificate is failing logins. 20 events.</summary>
    private static void AddCertExpiryArc(List<TelemetryEvent> events, DateTimeOffset anchor)
    {
        void Add(string service, int minutesAgo, Severity level, string message)
            => events.Add(new TelemetryEvent
            {
                Id = "pending",
                Service = service,
                Timestamp = anchor.AddMinutes(-minutesAgo),
                Level = level,
                Message = message,
            });

        Add("auth", 95, Severity.Info, "TLS certificate bundle loaded; leaf certificate expires within 2 hours");

        // Twelve handshake failures, roughly every seven minutes since expiry.
        for (var i = 0; i < 12; i++)
        {
            Add("auth", 88 - (i * 7), Severity.Error,
                "TLS handshake failed: certificate for auth.sandbox.internal expired");
        }

        // The gateway sees the failure as bad upstream responses.
        for (var i = 0; i < 6; i++)
        {
            Add("gateway", 84 - (i * 14), Severity.Warning,
                "upstream auth returned 502 during token validation");
        }

        Add("auth", 40, Severity.Critical, "login success rate dropped below 5 percent");
    }

    /// <summary>Arc 2, resolved: a bad deploy caused checkout latency. 16 events.</summary>
    private static void AddBadDeployArc(List<TelemetryEvent> events, DateTimeOffset anchor)
    {
        void Add(string service, int minutesAgo, Severity level, string message)
            => events.Add(new TelemetryEvent
            {
                Id = "pending",
                Service = service,
                Timestamp = anchor.AddMinutes(-minutesAgo),
                Level = level,
                Message = message,
            });

        Add("checkout", 480, Severity.Info, "deploy 2026.08.31.4 rolled out to all instances");

        var latencies = new[] { 900, 1400, 1900, 2400, 2800, 3100 };
        for (var i = 0; i < latencies.Length; i++)
        {
            Add("checkout", 465 - (i * 25), Severity.Warning,
                $"p95 latency {latencies[i]} ms exceeds 500 ms budget");
        }

        for (var i = 0; i < 4; i++)
        {
            Add("checkout", 420 - (i * 20), Severity.Error,
                "order placement timed out after 10000 ms");
        }

        for (var i = 0; i < 3; i++)
        {
            Add("payments", 400 - (i * 25), Severity.Warning,
                "caller closed connection before authorization completed");
        }

        Add("checkout", 310, Severity.Info, "deploy rolled back to 2026.08.31.3");
        Add("checkout", 300, Severity.Info, "p95 latency back to 210 ms baseline");
    }

    /// <summary>Arc 3, resolved: a queue backlog delayed notification email. 14 events.</summary>
    private static void AddQueueBacklogArc(List<TelemetryEvent> events, DateTimeOffset anchor)
    {
        void Add(string service, int minutesAgo, Severity level, string message)
            => events.Add(new TelemetryEvent
            {
                Id = "pending",
                Service = service,
                Timestamp = anchor.AddMinutes(-minutesAgo),
                Level = level,
                Message = message,
            });

        var depths = new[] { 4000, 8000, 12000, 16000, 9000, 2500 };
        for (var i = 0; i < depths.Length; i++)
        {
            Add("queue-broker", 1180 - (i * 60), Severity.Warning,
                $"queue depth {depths[i]} on topic outbound-email, oldest message {12 + (i * 9)} minutes");
        }

        for (var i = 0; i < 3; i++)
        {
            Add("email-worker", 1120 - (i * 80), Severity.Error,
                "processing lag above threshold, consumer cannot keep up");
        }

        Add("email-worker", 840, Severity.Info, "consumer count scaled from 2 to 8");

        for (var i = 0; i < 3; i++)
        {
            Add("notifications", 1060 - (i * 90), Severity.Warning,
                "email delivery latency breached the 15 minute objective");
        }

        Add("queue-broker", 720, Severity.Info, "backlog drained, queue depth back under 100");
    }

    private static void AddNoiseTickets(List<Ticket> tickets, DeterministicRandom random, DateTimeOffset anchor)
    {
        // Invented, mundane background noise: the kind of queue a real support inbox has.
        (string Title, string Description, string Service)[] pool =
        [
            ("Product image missing on detail page", "One product shows a placeholder instead of its photo.", "catalog"),
            ("Search returns no results for plural terms", "Searching 'mugs' finds nothing while 'mug' works.", "search"),
            ("Password reset email has a typo", "The reset email says 'clic here' instead of 'click here'.", "notifications"),
            ("Order history page loads slowly", "Order history takes around six seconds for large accounts.", "gateway"),
            ("Wrong currency symbol for one locale", "Prices show a dollar sign for one European locale.", "catalog"),
            ("Promo code field rejects valid code", "A seasonal promo code is rejected as expired one day early.", "checkout"),
            ("Push notification arrives twice", "Some customers get the same shipping update twice.", "notifications"),
            ("Autocomplete suggests discontinued items", "Search suggestions include items no longer sold.", "search"),
            ("Session expires too quickly on mobile", "Customers report being signed out after a few minutes.", "auth"),
            ("Gift card balance rounds oddly", "A balance of 10.005 displays as 10.01.", "payments"),
            ("Invoice PDF misses company field", "Business customers report the company name is blank.", "checkout"),
            ("Newsletter unsubscribe link loops", "The unsubscribe page redirects back to itself.", "notifications"),
            ("Category page shows stale prices", "A price change took hours to appear on category pages.", "catalog"),
            ("Two-factor prompt appears twice", "Sign-in occasionally asks for the code two times.", "auth"),
            ("Refund confirmation email delayed", "A refund email arrived a day after the refund.", "email-worker"),
            ("API returns 500 for empty cart checkout", "Calling checkout with an empty cart returns a server error.", "checkout"),
            ("Search filter resets on back navigation", "Going back clears the selected filters.", "search"),
            ("Address form rejects long street names", "Street names over 60 characters fail validation.", "checkout"),
            ("Saved card icon shows wrong brand", "A stored card renders the wrong network logo.", "payments"),
            ("Recommendation carousel is empty", "The 'you may also like' section renders blank for new users.", "catalog"),
            ("Order status webhook fires out of order", "A partner reports 'delivered' arriving before 'shipped'.", "queue-broker"),
            ("Login page logo blurry on retina", "The logo looks low resolution on high density screens.", "gateway"),
            ("Locale switcher forgets choice", "The site returns to the default language after one page.", "gateway"),
        ];

        for (var i = 0; i < NoiseTicketCount; i++)
        {
            var (title, description, service) = pool[i];
            var openedMinutesAgo = random.Next(6 * 60, 72 * 60);
            var updateDelay = random.Next(20, 8 * 60);
            var statusRoll = random.Next(0, 100);
            var status = statusRoll switch
            {
                < 20 => TicketStatus.Open,
                < 45 => TicketStatus.Investigating,
                _ => TicketStatus.Resolved,
            };

            tickets.Add(new Ticket
            {
                Id = "pending",
                Title = title,
                Description = description,
                Status = status,
                Services = [service],
                OpenedAt = anchor.AddMinutes(-openedMinutesAgo),
                UpdatedAt = anchor.AddMinutes(-Math.Max(0, openedMinutesAgo - updateDelay)),
                Reporter = $"user-{random.Next(1000, 9999)}",
            });
        }
    }

    private static void AddArcTickets(List<Ticket> tickets, DateTimeOffset anchor)
    {
        void Add(string title, string description, TicketStatus status, string[] services,
            int openedMinutesAgo, int updatedMinutesAgo, string reporter)
            => tickets.Add(new Ticket
            {
                Id = "pending",
                Title = title,
                Description = description,
                Status = status,
                Services = services,
                OpenedAt = anchor.AddMinutes(-openedMinutesAgo),
                UpdatedAt = anchor.AddMinutes(-updatedMinutesAgo),
                Reporter = reporter,
            });

        // Arc 1: ongoing login failures caused by the expired certificate.
        Add("Users cannot log in",
            "Support is getting a spike of reports that sign-in fails with a generic error. Started roughly an hour ago and affects web and mobile.",
            TicketStatus.Open, ["auth", "gateway"], 75, 10, "support-desk");
        Add("Login page shows error 502",
            "Several customers sent screenshots of a 502 page after submitting credentials.",
            TicketStatus.Open, ["gateway"], 60, 60, "user-2214");
        Add("SSO sign-in failing for all tenants",
            "Business customers report SSO redirects ending in an error for every tenant we checked.",
            TicketStatus.Open, ["auth"], 45, 45, "user-8710");

        // Arc 2: resolved checkout latency after a bad deploy.
        Add("Checkout slow, orders timing out",
            "Order placement takes longer than 10 seconds and some attempts time out. Started shortly after the afternoon deploy.",
            TicketStatus.Resolved, ["checkout", "payments"], 450, 290, "support-desk");
        Add("Card charged but order confirmation stuck",
            "One customer was charged while the order screen kept spinning.",
            TicketStatus.Resolved, ["checkout"], 420, 300, "user-5033");

        // Arc 3: resolved email delays caused by a queue backlog.
        Add("Order confirmation emails delayed by an hour",
            "Multiple customers report confirmation emails arriving around an hour late.",
            TicketStatus.Resolved, ["notifications", "email-worker"], 1140, 690, "support-desk");
        Add("Password reset email never arrived",
            "A customer requested three password resets and received them all in one batch much later.",
            TicketStatus.Resolved, ["notifications"], 1080, 700, "user-6402");
    }

    /// <summary>
    /// A tiny xorshift random source with fully specified behavior, used instead of
    /// <see cref="Random"/> so generated data is identical on every runtime and platform.
    /// </summary>
    private sealed class DeterministicRandom(int seed)
    {
        private uint _state = seed == 0 ? 2463534242u : (uint)seed;

        public int Next(int minInclusive, int maxExclusive)
        {
            var range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        public T Pick<T>(IReadOnlyList<T> items) => items[Next(0, items.Count)];

        private uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }
    }
}

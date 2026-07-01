using Dapr.Client;
using System.Text.Json;
using TicketBookingPoc.Models;

namespace TicketBookingPoc.Services
{
    public class NatsWarmupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NatsWarmupService> _logger;

        public NatsWarmupService(IServiceProvider serviceProvider, ILogger<NatsWarmupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1. Wait for Dapr Sidecar to be ready before doing anything
            _logger.LogInformation("⏳ Waiting for Dapr Sidecar to be ready...");
            using var scope = _serviceProvider.CreateScope();
            var daprClient = scope.ServiceProvider.GetRequiredService<DaprClient>();

            await daprClient.WaitForSidecarAsync(stoppingToken);
            _logger.LogInformation("✅ Dapr Sidecar is ready! Starting warmup...");

            try
            {
                // 2. Pull all showtime IDs from DB
                var showtimeQuery = new Dictionary<string, string> { { "sql", "SELECT id FROM showtimes" } };
                var rawShowtimes = await daprClient.InvokeBindingAsync<string, JsonElement>("sqldb", "query", "", showtimeQuery, stoppingToken);

                int successCount = 0;

                // 3. For each showtime, load seat plan into NATS KV
                foreach (var row in rawShowtimes.EnumerateArray())
                {
                    int showtimeId = row[0].GetInt32();
                    var cacheKey = $"showtime-plan-{showtimeId}";

                    var seatQuery = new Dictionary<string, string> {
                        { "sql", $"SELECT seat_code, status, payment_time FROM seats WHERE showtime_id = {showtimeId} ORDER BY seat_code" }
                    };
                    var rawSeats = await daprClient.InvokeBindingAsync<string, JsonElement>("sqldb", "query", "", seatQuery, stoppingToken);

                    var dbSeats = new List<Seat>();
                    foreach (var seatRow in rawSeats.EnumerateArray())
                    {
                        dbSeats.Add(new Seat {
                            SeatCode = seatRow[0].GetString(),
                            Status = seatRow[1].GetString(),
                            PaymentTime = seatRow[2].ValueKind == JsonValueKind.Null ? null : seatRow[2].GetDateTime()
                        });
                    }

                    var plan = new SeatPlanDto { ShowtimeId = showtimeId, Seats = dbSeats };
                    await daprClient.SaveStateAsync("seat-state-nats", cacheKey, plan, cancellationToken: stoppingToken);
                    successCount++;
                }

                _logger.LogInformation("✅ NATS Warm-up completed! Loaded {Count} showtimes into cache.", successCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to warm-up NATS cache.");
            }
        }
    }
}

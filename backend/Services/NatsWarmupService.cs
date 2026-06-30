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
            // หน่วงเวลา 10 วินาทีตอนเริ่มบูต เพื่อรอให้ Dapr Sidecar และ Database พร้อมทำงาน 100%
            _logger.LogInformation("⏳ Waiting 10 seconds for Dapr and Database to be fully ready...");
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            
            _logger.LogInformation("🔥 Starting NATS Cache Warm-up...");

            // ใช้ Scope เพื่อดึง DaprClient ออกมาใช้งานใน Background Task
            using var scope = _serviceProvider.CreateScope();
            var daprClient = scope.ServiceProvider.GetRequiredService<DaprClient>();

            try
            {
                // 1. ดึง ID ของรอบฉายทั้งหมดจาก Database
                var showtimeQuery = new Dictionary<string, string> { { "sql", "SELECT id FROM showtimes" } };
                var rawShowtimes = await daprClient.InvokeBindingAsync<string, JsonElement>("sqldb", "query", "", showtimeQuery, stoppingToken);

                int successCount = 0;

                // 2. วนลูปดึงผังที่นั่งของแต่ละรอบฉาย
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

                    // 3. บันทึกข้อมูลเข้า NATS JetStream KV
                    await daprClient.SaveStateAsync("seat-state-nats", cacheKey, plan, cancellationToken: stoppingToken);
                    successCount++;
                }

                _logger.LogInformation($"✅ NATS Warm-up completed! Successfully loaded {successCount} showtimes into cache.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Failed to warm-up NATS: {ex.Message}");
            }
        }
    }
}
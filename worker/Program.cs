using Dapr;
using Dapr.Client;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprClient();
var app = builder.Build();

// Do NOT call app.UseCloudEvents() — Sequin publishes raw JSON, not CloudEvents
app.MapSubscribeHandler();

app.MapPost("/process-seat-event",
    [Topic("nats-pubsub", "seat.events")]
    [DisableRequestSizeLimit]
    async (HttpContext httpContext, [FromServices] DaprClient daprClient, ILogger<Program> logger) =>
{
    try
    {
        using var reader = new StreamReader(httpContext.Request.Body);
        var rawBody = await reader.ReadToEndAsync();

        logger.LogInformation("📨 [Worker] Raw body: {Body}", rawBody);

        var body = JsonDocument.Parse(rawBody).RootElement;

	if (body.TryGetProperty("data", out var dataEl))
	{
    		body = dataEl; // ทะลวงเข้าไปในชั้น data
	}

        // Sequin wraps the outbox row under "record":
        // { "record": { ... }, "action": "insert", "metadata": { ... } }
        JsonElement record;
        if (body.TryGetProperty("record", out var recordEl))
            record = recordEl;
        else
            record = body; // fallback: flat structure

        var eventType   = record.GetProperty("event_type").GetString();
        var aggregateId = record.GetProperty("aggregate_id").GetString();
        var payloadEl   = record.GetProperty("payload");

        logger.LogInformation("🔥 [Worker] Event: {EventType} | ID: {AggregateId}", eventType, aggregateId);

        // payload column may be stored as a JSON string or as JSONB object
        JsonElement payload;
        if (payloadEl.ValueKind == JsonValueKind.String)
            payload = JsonDocument.Parse(payloadEl.GetString()!).RootElement;
        else
            payload = payloadEl;

        var showtimeId = payload.GetProperty("showtime_id").GetInt32();
        var seatCode   = payload.GetProperty("seat_code").GetString();
        var newStatus  = payload.GetProperty("status").GetString();
        var cacheKey   = $"showtime-plan-{showtimeId}";

        var seatPlan = await daprClient.GetStateAsync<SeatPlanDto>("seat-state-nats", cacheKey);
        if (seatPlan == null || seatPlan.Seats == null)
        {
            logger.LogWarning("⚠️ [Worker] Cache miss for Showtime {ShowtimeId}. กำลัง Rebuild ข้อมูลจาก Database...", showtimeId);

            var selectMetadata = new Dictionary<string, string> { 
                { "sql", $"SELECT seat_code, status, payment_time FROM seats WHERE showtime_id = {showtimeId} ORDER BY seat_code" } 
            };
            var rawData = await daprClient.InvokeBindingAsync<string, JsonElement>("sqldb", "query", "", selectMetadata);
            
            var dbSeats = new List<Seat>();
            foreach (var row in rawData.EnumerateArray())
            {
                dbSeats.Add(new Seat {
                    SeatCode = row[0].GetString(),
                    Status = row[1].GetString(),
                    PaymentTime = row[2].ValueKind == JsonValueKind.Null ? null : row[2].GetDateTime()
                });
            }

            seatPlan = new SeatPlanDto { ShowtimeId = showtimeId, Seats = dbSeats };
            
            // เซฟทับลงไปใน NATS เลย
            await daprClient.SaveStateAsync("seat-state-nats", cacheKey, seatPlan);
            logger.LogInformation("✅ [Worker] Rebuild ผังที่นั่งรอบฉาย {ShowtimeId} ลง NATS สำเร็จ (Self-Healing)!", showtimeId);
            
            // 리เทิร์นได้เลย เพราะข้อมูลที่ดึงมาใหม่สุดจาก DB มันรวมการจองครั้งนี้ไปเรียบร้อยแล้ว
            return Results.Ok(); 
        }

        var seat = seatPlan.Seats.FirstOrDefault(s => s.SeatCode == seatCode);
        if (seat != null)
        {
            seat.Status      = newStatus;
            seat.PaymentTime = newStatus == "Paid" ? DateTime.UtcNow : null;
            await daprClient.SaveStateAsync("seat-state-nats", cacheKey, seatPlan);
            logger.LogInformation("✅ [Worker] Updated: {SeatCode} → {Status} (Showtime {ShowtimeId})", seatCode, newStatus, showtimeId);
        }
        else
        {
            logger.LogWarning("⚠️ [Worker] Seat {SeatCode} not found in Showtime {ShowtimeId}", seatCode, showtimeId);
        }

        return Results.Ok();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ [Worker] Failed to process event");
        return Results.Problem();
    }
});

app.Run();

public class Seat
{
    public string SeatCode { get; set; }
    public string Status { get; set; }
    public DateTime? PaymentTime { get; set; }
}

public class SeatPlanDto
{
    public int ShowtimeId { get; set; }
    public List<Seat> Seats { get; set; } = new();
}

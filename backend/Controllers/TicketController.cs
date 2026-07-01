using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TicketBookingPoc.Models;

namespace TicketBookingPoc.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TicketController : ControllerBase
    {
        private readonly DaprClient _daprClient;
        private const string NatsStateStore = "seat-state-nats";
        private const string SqlBinding = "sqldb";
	// 🛠️ ตัวแปรจำสถานะว่า NATS ล้าหลัง/เคยล่มหรือไม่ (Dirty Flag)
        private static bool _isNatsStale = false;
	// 🛠️ เพิ่มตัวแปร Circuit Breaker จำเวลาที่ต้องข้าม NATS
        private static DateTime _natsCircuitOpenUntil = DateTime.MinValue;

        public TicketController(DaprClient daprClient)
        {
            _daprClient = daprClient;
        }

        // 1. ดึงข้อมูลสาขา (แก้ไขการ Map Array of Arrays)
        [HttpGet("branches")]
        public async Task<IActionResult> GetBranches()
        {
            var metadata = new Dictionary<string, string> { { "sql", "SELECT id, name FROM branches ORDER BY id" } };
            var rawData = await _daprClient.InvokeBindingAsync<string, JsonElement>(SqlBinding, "query", "", metadata);
            
            var branches = new List<Branch>();
            foreach (var row in rawData.EnumerateArray())
            {
                branches.Add(new Branch {
                    Id = row[0].GetInt32(),
                    Name = row[1].GetString()
                });
            }
            return Ok(branches);
        }

        // 2. ดึงรอบฉายแยกตามสาขา (แก้ไขการ Map Array of Arrays)
        [HttpGet("showtimes/{branchId}")]
        public async Task<IActionResult> GetShowtimes(int branchId)
        {
            var metadata = new Dictionary<string, string> { 
                { "sql", $"SELECT s.id, s.cinema_id, c.name, s.movie_title, s.show_time FROM showtimes s JOIN cinemas c ON s.cinema_id = c.id WHERE c.branch_id = {branchId} ORDER BY s.show_time" } 
            };
            var rawData = await _daprClient.InvokeBindingAsync<string, JsonElement>(SqlBinding, "query", "", metadata);
            
            var showtimes = new List<Showtime>();
            foreach (var row in rawData.EnumerateArray())
            {
                showtimes.Add(new Showtime {
                    Id = row[0].GetInt32(),
                    CinemaId = row[1].GetInt32(),
                    CinemaName = row[2].GetString(),
                    MovieTitle = row[3].GetString(),
                    ShowTime = row[4].GetDateTime()
                });
            }
            return Ok(showtimes);
        }

        // 3. ดึงผังที่นั่ง (แก้ตรง Fallback Database ให้รองรับ Array of Arrays)
        [HttpGet("seats/{showtimeId}")]
public async Task<IActionResult> GetSeatPlan(int showtimeId)
        {
            var cacheKey = $"showtime-plan-{showtimeId}";
            var apiResponse = new ApiResponse<SeatPlanDto>();
            
            bool forceDb = _isNatsStale; 

            // 🛠️ 1. ถ้าเวลาปัจจุบันยังไม่หมดช่วงเบรกเกอร์ ให้บังคับไป DB ทันที (รอ 0 วินาที)
            if (DateTime.Now < _natsCircuitOpenUntil)
            {
                apiResponse.Logs.Add(new ActionLog { Message = "🛑 Circuit Breaker ทำงาน: ข้าม NATS ทันทีโดยไม่ต้องรอ (0ms)" });
                forceDb = true;
            }

            if (!forceDb)
            {
                // 🛠️ 2. หั่น Timeout เหลือแค่ 300 ms! (ถ้า NATS ดีจริง ต้องตอบกลับในเสี้ยววิ)
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                try
                {
                    apiResponse.Logs.Add(new ActionLog { Message = "👉 กำลังพยายามดึงข้อมูลผังที่นั่งจาก NATS JetStream KV..." });
                    var seatsFromNats = await _daprClient.GetStateAsync<SeatPlanDto>(NatsStateStore, cacheKey, cancellationToken: cts.Token);
                    
                    if (seatsFromNats != null)
                    {
                        apiResponse.Source = "NATS";
                        apiResponse.Data = seatsFromNats;
                        apiResponse.Logs.Add(new ActionLog { Message = "✅ ดึงข้อมูลสำเร็จจาก NATS JetStream (ความเร็วสูง)" });
                        return Ok(apiResponse);
                    }
                    apiResponse.Logs.Add(new ActionLog { Message = "⚠️ ไม่พบข้อมูลใน NATS KV (Cache Miss)" });
                }
                catch (Exception ex)
                {
                    // 🛠️ 3. ถ้าพัง (Timeout หรือ Error) สับเบรกเกอร์ทันที 10 วินาที!
                    _natsCircuitOpenUntil = DateTime.Now.AddSeconds(10);
                    apiResponse.Logs.Add(new ActionLog { Message = "🚨 NATS ไม่ตอบสนองใน 300ms! สับสวิตช์ Circuit Breaker พักการเชื่อมต่อ 10 วินาที" });
                }
            }
            else if (!_isNatsStale)
            {
                // ข้ามการแจ้งเตือน NATS เพิ่งกู้คืน ถ้ามันเป็นเพราะเบรกเกอร์
            }
            else
            {
                apiResponse.Logs.Add(new ActionLog { Message = "🧹 ตรวจพบว่า NATS เพิ่งกู้คืนกลับมา! ข้าม Cache ชั่วคราวเพื่อซิงค์ข้อมูลใหม่" });
            }

            apiResponse.Logs.Add(new ActionLog { Message = "⚡ กำลังดึงข้อมูลสำรองจากฐานข้อมูล PostgreSQL..." });
            
            var metadata = new Dictionary<string, string> { 
                { "sql", $"SELECT seat_code, status, payment_time FROM seats WHERE showtime_id = {showtimeId} ORDER BY seat_code" } 
            };
            var rawData = await _daprClient.InvokeBindingAsync<string, JsonElement>(SqlBinding, "query", "", metadata);
            
            var dbSeats = new List<Seat>();
            foreach (var row in rawData.EnumerateArray())
            {
                dbSeats.Add(new Seat {
                    SeatCode = row[0].GetString(),
                    Status = row[1].GetString(),
                    PaymentTime = row[2].ValueKind == JsonValueKind.Null ? null : row[2].GetDateTime()
                });
            }
            
            var plan = new SeatPlanDto { ShowtimeId = showtimeId, Seats = dbSeats };
            apiResponse.Source = "DB";
            apiResponse.Data = plan;
            apiResponse.Logs.Add(new ActionLog { Message = "✅ ดึงข้อมูลสำเร็จจาก PostgreSQL" });

		try
            {
                // 🛠️ บังคับให้การเซฟลง NATS มีเวลาแค่ 1 วินาที ถ้าไม่เสร็จให้โยน Error ทันที
                using var saveCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                
                await _daprClient.SaveStateAsync(NatsStateStore, cacheKey, plan, cancellationToken: saveCts.Token);
                apiResponse.Logs.Add(new ActionLog { Message = "🔄 ซิงค์ข้อมูลกลับไปบันทึกที่ NATS KV เรียบร้อย" });
                _isNatsStale = false; 
            }
            catch (Exception)
            {
                apiResponse.Logs.Add(new ActionLog { Message = "⚠️ ไม่สามารถซิงค์ Cache ได้เนื่องจาก NATS ขัดข้อง (Degraded Mode)" });
                _isNatsStale = true;
            }

            return Ok(apiResponse);
        }

        // 4. อัปเดตสถานะ (แก้การดึงข้อมูลเพื่อซิงค์กลับ NATS ให้รองรับ Array of Arrays)
        [HttpPost("seats/update-status")]
public async Task<IActionResult> UpdateSeatStatus([FromBody] UpdateSeatsRequest req)
        {
            var apiResponse = new ApiResponse<string> { Data = "Success" };

            // 🛠️ 1. สร้างคำสั่ง SQL ที่บันทึกทั้งตาราง seats และ outbox_events ใน Transaction เดียวกัน
            var sqlStatements = new List<string>();
            sqlStatements.Add("BEGIN;");
            
            string statusValue = req.Status == "Paid" ? "Paid" : req.Status;
            string paymentTimeStr = req.Status == "Paid" ? "NOW()" : "NULL";
            
            string seatCodesCsv = string.Join(",", req.SeatCodes.Select(c => $"'{c}'"));
            sqlStatements.Add($"UPDATE seats SET status = '{statusValue}', payment_time = {paymentTimeStr} WHERE showtime_id = {req.ShowtimeId} AND seat_code IN ({seatCodesCsv});");

            // 🛠️ 2. วนลูปสร้าง Event ลงตาราง outbox_events (ให้ Sequin ดูดไป)
            foreach(var code in req.SeatCodes)
            {
                string payloadJson = $"{{\"showtime_id\": {req.ShowtimeId}, \"seat_code\": \"{code}\", \"status\": \"{statusValue}\"}}";
                sqlStatements.Add($"INSERT INTO outbox_events (aggregate_type, aggregate_id, event_type, payload) VALUES ('Seat', '{req.ShowtimeId}-{code}', 'SeatUpdated', '{payloadJson}');");
            }
            
            sqlStatements.Add("COMMIT;");
            
            var sqlQuery = string.Join("\n", sqlStatements);
            var metadata = new Dictionary<string, string> { { "sql", sqlQuery } };
            
            // ยิงคำสั่งเข้า PostgreSQL รอบเดียวจบ (และไม่ต้องไปยุ่งกับ NATS ตรงนี้เลย)
            await _daprClient.InvokeBindingAsync(SqlBinding, "exec", "", metadata);
            apiResponse.Logs.Add(new ActionLog { Message = "💾 บันทึกที่นั่งและ Outbox Event ลง PostgreSQL สำเร็จ (ตัดการบันทึก NATS โดยตรงออก)" });

            return Ok(apiResponse);
        }

    }
}
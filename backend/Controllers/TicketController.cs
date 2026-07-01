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

            return Ok(apiResponse);
        }

        // 4. อัปเดตสถานะ (แก้การดึงข้อมูลเพื่อซิงค์กลับ NATS ให้รองรับ Array of Arrays)
[HttpPost("seats/update-status")]
        public async Task<IActionResult> UpdateSeatStatus([FromBody] UpdateSeatsRequest req)
        {
            var apiResponse = new ApiResponse<string> { Data = "Success" };

            // 1. กำหนดเงื่อนไขว่า "ก่อนจะเปลี่ยน สถานะเดิมต้องเป็นอะไร" (ป้องกันคนอื่นแย่งทำไปแล้ว)
            string expectedStatus = ""; 
            if (req.Status == "Lock") expectedStatus = ""; // จะล็อคได้ สถานะเดิมต้อง "ว่าง"
            else if (req.Status == "Paid") expectedStatus = "Lock"; // จะจ่ายเงินได้ สถานะเดิมต้อง "Lock" อยู่
            else if (req.Status == "") expectedStatus = "Lock"; // จะปลดล็อคได้ สถานะเดิมต้อง "Lock" อยู่

            string statusValue = req.Status == "Paid" ? "Paid" : req.Status;
            string paymentTimeStr = req.Status == "Paid" ? "NOW()" : "NULL";
            
            string seatCodesCsv = string.Join(",", req.SeatCodes.Select(c => $"'{c}'"));
            int seatCount = req.SeatCodes.Count;

            // 2. ใช้ DO Block เพื่อทำ Transaction และตรวจสอบ Concurrency
            var sqlStatements = new List<string>
            {
                "DO $$",
                "DECLARE updated_count INT;",
                "BEGIN",
                // พยายามอัปเดต โดยเพิ่มเงื่อนไข AND status = 'expectedStatus' เข้าไปด้วย
                $"  UPDATE seats SET status = '{statusValue}', payment_time = {paymentTimeStr} WHERE showtime_id = {req.ShowtimeId} AND seat_code IN ({seatCodesCsv}) AND status = '{expectedStatus}';",
                // ดึงจำนวนรายการที่สามารถอัปเดตได้จริงๆ
                "  GET DIAGNOSTICS updated_count = ROW_COUNT;",
                // ถ้าอัปเดตได้น้อยกว่าที่ขอ แปลว่ามีเก้าอี้บางตัวเปลี่ยนสถานะไปแล้ว (มีคนแย่งจอง)
                $"  IF updated_count <> {seatCount} THEN",
                "    RAISE EXCEPTION 'CONCURRENCY_CONFLICT';", // โยน Error เพื่อหยุดและ Rollback การทำงานทั้งหมด
                "  END IF;"
            };

            // 3. วนลูปสร้าง Event ลงตาราง outbox_events (ถ้าโค้ดรันมาถึงตรงนี้แปลว่าปลอดภัย 100%)
            foreach(var code in req.SeatCodes)
            {
                string payloadJson = $"{{\"showtime_id\": {req.ShowtimeId}, \"seat_code\": \"{code}\", \"status\": \"{statusValue}\"}}";
                sqlStatements.Add($"  INSERT INTO outbox_events (aggregate_type, aggregate_id, event_type, payload) VALUES ('Seat', '{req.ShowtimeId}-{code}', 'SeatUpdated', '{payloadJson}');");
            }
            
            sqlStatements.Add("END $$;");
            
            var metadata = new Dictionary<string, string> { { "sql", string.Join("\n", sqlStatements) } };

            try
            {
                // ส่งคำสั่งไปประมวลผลที่ PostgreSQL รอบเดียวจบ
                await _daprClient.InvokeBindingAsync(SqlBinding, "exec", "", metadata);
                apiResponse.Logs.Add(new ActionLog { Message = $"💾 บันทึกสถานะ '{req.Status}' สำเร็จ (ไม่พบการชนกัน)" });
                return Ok(apiResponse);
            }
            catch (Exception)
            {
                // ถ้า PostgreSQL โยน Exception 'CONCURRENCY_CONFLICT' ออกมา จะเข้าบล็อกนี้
                apiResponse.Data = "Failed";
                apiResponse.Logs.Add(new ActionLog { Message = $"❌ ถูกปฏิเสธ (Race Condition)! ที่นั่งนี้ถูกทำรายการไปก่อนหน้าคุณเสี้ยววินาที" });
                return BadRequest(apiResponse); // รีเทิร์น 400 Bad Request
            }
        }

    }
}
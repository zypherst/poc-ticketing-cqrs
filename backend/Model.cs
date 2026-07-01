namespace TicketBookingPoc.Models
{
    public class Branch { public int Id { get; set; } public string Name { get; set; } }
    
    public class Showtime 
    { 
        public int Id { get; set; } 
        public int CinemaId { get; set; } 
        public string CinemaName { get; set; } 
        public string MovieTitle { get; set; } 
        public DateTime ShowTime { get; set; } 
    }

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

    public class UpdateSeatsRequest
    {
        public int ShowtimeId { get; set; }
        public List<string> SeatCodes { get; set; }
        public string Status { get; set; }
    }

    public class ActionLog
    {
        public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss");
        public string Message { get; set; }
    }

    public class ApiResponse<T>
    {
        public string Source { get; set; } // "NATS" หรือ "DB"
        public T Data { get; set; }
        public List<ActionLog> Logs { get; set; } = new();
    }

}
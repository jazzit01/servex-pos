namespace ServeX.Models
{
    public class Room
    {
        public int Id { get; set; }

        public string RoomNumber { get; set; }

        public RoomStatus Status { get; set; } = RoomStatus.Available;
    }
}

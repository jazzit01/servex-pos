namespace ServeX.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public bool IsPasswordHashed { get; set; }

        public string Password { get; set; } = string.Empty;
    }
}

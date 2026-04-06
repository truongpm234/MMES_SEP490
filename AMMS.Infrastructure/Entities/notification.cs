namespace AMMS.Infrastructure.Entities
{
    public class notification
    {
        public int Id { get; set; }

        public string Content { get; set; } = null!;

        public int? UserId { get; set; }

        public int? RoleId { get; set; }

        public DateTime Time { get; set; }

        public bool IsCheck { get; set; }

        public string? Status { get; set; }
    }
}

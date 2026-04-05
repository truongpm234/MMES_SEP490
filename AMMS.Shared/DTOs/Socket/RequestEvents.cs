namespace AMMS.Shared.DTOs.Socket
{
    public static class RealtimeGroups
    {
        public const string RequestsAll = "requests:all";
        public static string ByRole(string role) => $"requests:role:{role}";
        public static string Request(int requestId) => $"request:{requestId}";
    }

    public record RequestChangedEvent(
        int? order_id,
        int request_id,
        string? old_status,
        string? new_status,
        string action,
        DateTime changed_at,
        string? changed_by
    );

    public record RequestNoteChangedEvent(
        int request_id,
        string? consultant_note,
        DateTime changed_at
    );
}

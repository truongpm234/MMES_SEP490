using AMMS.Infrastructure.DBContext;
using AMMS.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMMS.Infrastructure.Repositories
{
    public class NotificationsRepository
    {
        private readonly AppDbContext _db;
        public NotificationsRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<notification>> GetNotificationByRoleId(int role_id)
        {
            var res = await _db.notifications.Where(n => n.RoleId == role_id).ToListAsync();
            return res;
        }

        public async Task<string> CreateNoti(int role_id, string content, int? user_id)
        {
            var res = new notification();
            res.RoleId = role_id;
            res.Content = content;
            res.UserId = user_id;
            res.Time = DateTime.UtcNow;
            res.IsCheck = false;
            res.Status = "Active";
            await _db.notifications.AddAsync(res);
            _db.SaveChanges();
            return "Success";
        }

        public async Task<string> UpdateNoti(int id)
        {
            var res = await _db.notifications.FirstOrDefaultAsync(n => n.Id == id);
            if (res != null)
            {
                res.IsCheck = true;
                return "is read";
            }
            return "Failed";
        }
    }
}

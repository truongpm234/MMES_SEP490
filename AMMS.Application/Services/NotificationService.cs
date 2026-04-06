using AMMS.Infrastructure.Entities;
using AMMS.Infrastructure.Repositories;

namespace AMMS.Application.Services
{
    public class NotificationService
    {
        private readonly NotificationsRepository _notiRepo;

        public NotificationService(NotificationsRepository repo)
        {
            _notiRepo = repo;
        }

        public async Task<List<notification>> GetNotiByRoleId(int id)
        {
            return await _notiRepo.GetNotificationByRoleId(id);
        }

        public async Task<List<notification>> GetConsultantNotiByUserId(int id, int? user_id)
        {
            return await _notiRepo.GetConsultantNotificationUserId(id, user_id);
        }

        public async Task<string> CreateNotfi(int role_id, string content, int? user_id)
        {
            return await _notiRepo.CreateNoti(role_id, content, user_id);
        }
    }
}

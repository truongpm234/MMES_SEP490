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
    }
}

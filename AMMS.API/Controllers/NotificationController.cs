using AMMS.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AMMS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationController : ControllerBase
    {
        private readonly NotificationService _notiService;

        public NotificationController(NotificationService service)
        {
            _notiService = service;
        }

        [HttpGet("get-noti-by-role-id")]
        public async Task<IActionResult> GetNotiByRoleId(int id)
        {
            return Ok(_notiService.GetNotiByRoleId(id));
        }
    }
}

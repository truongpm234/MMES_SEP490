using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AMMS.Application.Interfaces;
using Hangfire;

namespace AMMS.Application.Jobs
{
    public class EmailJob
    {
        private readonly IEmailService _emailService;

        public EmailJob(IEmailService emailService)
        {
            _emailService = emailService;
        }

        [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 2, 5, 10, 20, 40 })]
        public async Task SendAsync(string to, string subject, string html)
        {
            await _emailService.SendAsync(to, subject, html);
        }
    }
}

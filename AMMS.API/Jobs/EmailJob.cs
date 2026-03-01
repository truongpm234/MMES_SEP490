//// AMMS.API/Jobs/EmailJob.cs
//using global::AMMS.Application.Helpers;
//using global::AMMS.Application.Interfaces;
//using Polly.Retry;

//namespace AMMS.API.Jobs
//{
//    namespace AMMS.API.Jobs
//    {
//        public class EmailJob
//        {
//            private readonly IEmailService _emailService;
//            private static readonly AsyncRetryPolicy _retry = EmailRetry.CreatePolicy();

//            public EmailJob(IEmailService emailService)
//            {
//                _emailService = emailService;
//            }

//            public async Task SendAsync(string to, string subject, string html)
//            {
//                await _retry.ExecuteAsync(() => _emailService.SendAsync(to, subject, html));
//            }
//        }
//    }
//}

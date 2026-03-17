using AMMS.Application.Interfaces;

namespace AMMS.API.Jobs
{
    public sealed class EmailDispatcherHostedService : BackgroundService
    {
        private readonly IEmailBackgroundQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailDispatcherHostedService> _logger;

        public EmailDispatcherHostedService(
            IEmailBackgroundQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<EmailDispatcherHostedService> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EmailDispatcherHostedService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var item = await _queue.DequeueAsync(stoppingToken);

                    using var scope = _scopeFactory.CreateScope();
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                    await emailService.SendAsync(item.To, item.Subject, item.Html);

                    _logger.LogInformation(
                        "Background email sent. To={To}; Subject={Subject}",
                        item.To, item.Subject);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background email send failed");
                }
            }

            _logger.LogInformation("EmailDispatcherHostedService stopped");
        }
    }
}
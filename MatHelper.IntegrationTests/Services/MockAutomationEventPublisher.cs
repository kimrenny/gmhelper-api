using System.Collections.Concurrent;
using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using Microsoft.Extensions.Logging;

namespace MatHelper.IntegrationTests.Services
{
    public class MockAutomationEventPublisher : IAutomationEventPublisher
    {
        private readonly ILogger<MockAutomationEventPublisher> _logger;
        public ConcurrentBag<AutomationEvent> PublishedEvents { get; } = new();

        public MockAutomationEventPublisher(ILogger<MockAutomationEventPublisher> logger)
        {
            _logger = logger;
        }

        public Task PublishAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(automationEvent);
            _logger.LogInformation("[MOCK PUBLISHER] Published event {Type} with ID {EventId} for user {UserId}",
                automationEvent.Type, automationEvent.Id, automationEvent.UserId);
            return Task.CompletedTask;
        }
    }
}

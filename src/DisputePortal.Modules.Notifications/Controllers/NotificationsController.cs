using DisputePortal.Modules.Notifications.Application;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Modules.Notifications.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = ApiConventions.CustomerPolicy)]
[Tags("Notifications")]
internal sealed class NotificationsController(NotificationService service, ICurrentCustomer customer) : ControllerBase
{
    [HttpGet(Name = "ListNotifications")]
    [EndpointSummary("Lists the customer's inbox, newest first.")]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List([FromQuery] NotificationListQuery query, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(customer.CustomerId, query, cancellationToken));

    [HttpGet("unread-count", Name = "GetUnreadNotificationCount")]
    [EndpointSummary("Returns how many notifications the customer has not read.")]
    [ProducesResponseType<UnreadCountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountDto>> CountUnread(CancellationToken cancellationToken) =>
        Ok(await service.CountUnreadAsync(customer.CustomerId, cancellationToken));

    [HttpPost("{id:guid}/read", Name = "MarkNotificationRead")]
    [EndpointSummary("Marks a notification as read. Safe to repeat.")]
    [ProducesResponseType<NotificationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.MarkReadAsync(customer.CustomerId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}

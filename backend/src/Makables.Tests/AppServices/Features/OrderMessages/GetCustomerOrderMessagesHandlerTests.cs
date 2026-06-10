using FluentAssertions;
using Makables.Core.AppServices.Features.OrderMessages;
using Makables.Core.Domain.Common;
using Makables.Core.Domain.OrderMessages;
using NSubstitute;

namespace Makables.Tests.AppServices.Features.OrderMessages;

/// <summary>
/// T-0079 customer-host paged read handler — thin forwarding shape.
/// IDOR shield is at the SQL level (queries.GetByOrderForCustomerAsync);
/// these tests pin the session resolution + delegation.
/// </summary>
public class GetCustomerOrderMessagesHandlerTests
{
    private const string OrderId = "ord-1";
    private const string CustomerUserId = "user-customer-1";

    private readonly IOrderMessageQueries _queries = Substitute.For<IOrderMessageQueries>();
    private readonly IUserSessionProvider _session = Substitute.For<IUserSessionProvider>();
    private readonly GetCustomerOrderMessages.Handler _sut;

    public GetCustomerOrderMessagesHandlerTests()
    {
        _session.GetUserId().Returns(CustomerUserId);
        _sut = new GetCustomerOrderMessages.Handler(_queries, _session);
    }

    [Fact]
    public async Task Happy_path_forwards_to_query_with_session_id()
    {
        var paged = new PagedData<OrderMessageDto>(
            Array.Empty<OrderMessageDto>(), Page: 1, PageSize: 50, TotalCount: 0);
        _queries.GetByOrderForCustomerAsync(OrderId, CustomerUserId, 1, 50, Arg.Any<CancellationToken>())
            .Returns(paged);

        var result = await _sut.Handle(
            new GetCustomerOrderMessages.Query(OrderId, 1, 50),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Messages.TotalCount.Should().Be(0);
        await _queries.Received(1).GetByOrderForCustomerAsync(
            OrderId, CustomerUserId, 1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_session_returns_Unauthorized()
    {
        _session.GetUserId().Returns((string?)null);

        var result = await _sut.Handle(
            new GetCustomerOrderMessages.Query(OrderId, 1, 50),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }
}

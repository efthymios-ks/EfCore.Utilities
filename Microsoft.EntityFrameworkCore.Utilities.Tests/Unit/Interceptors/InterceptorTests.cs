using Microsoft.EntityFrameworkCore.Utilities.Interceptors;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Unit.Interceptors;

public class InterceptorTests
{
    [Fact]
    public void AuditableTimestampInterceptor_WhenTheTimeProviderIsNull_ShouldFallBackToTheSystemClock()
        => Assert.NotNull(new AuditableTimestampInterceptor(null!));

    [Fact]
    public void EntityChangeInterceptor_WhenTheTimeProviderIsNull_ShouldFallBackToTheSystemClock()
        => Assert.NotNull(new EntityChangeInterceptor(null!));
}

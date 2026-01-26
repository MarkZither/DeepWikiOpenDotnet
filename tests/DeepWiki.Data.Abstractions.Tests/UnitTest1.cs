namespace DeepWiki.Data.Abstractions.Tests;

public class DeepWikiDataAbstractionsSmokeTests
{
    [Fact]
    public void DataAbstractionsAssemblyIsLoadable()
    {
        var assembly = typeof(DeepWiki.Data.Abstractions.Tests.DeepWikiDataAbstractionsSmokeTests).Assembly;
        Xunit.Assert.NotNull(assembly);
    }
}

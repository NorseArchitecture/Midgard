using System.Diagnostics.CodeAnalysis;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

[CollectionDefinition(nameof(SqlServerCollection))]
[SuppressMessage("Design", "CA1711:Identifiers should not have incorrect suffix",
	Justification = "xUnit collection fixture naming convention")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>;

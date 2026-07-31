using Norse.Abstractions.Backend;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// Mirror-law-conformant synthetic pair: every view scalar/collection name+type matches the entity.
// PolicyView.Notes is deliberately view-extra (residual); PolicyEntity.RowStamp is deliberately
// [NotProjected]-shaped (no view counterpart) for the Task 5 validation suite.
public sealed record PolicyClassCodeView(string Code);
public sealed record PolicyClassCodeEntity(string Code);

public sealed record PolicyView
{
	public required Guid Id { get; init; }
	public required string CustomerId { get; init; }
	public required DateOnly EffectiveDate { get; init; }
	public string? Notes { get; init; }
	public IReadOnlyList<PolicyClassCodeView> ClassCodes { get; init; } = [];
}

public sealed record PolicyEntity : IViewBearer<PolicyView>
{
	public required Guid Id { get; init; }
	public required string CustomerId { get; init; }
	public required DateOnly EffectiveDate { get; init; }
	public ICollection<PolicyClassCodeEntity> ClassCodes { get; init; } = [];
	public required PolicyView View { get; init; }
}

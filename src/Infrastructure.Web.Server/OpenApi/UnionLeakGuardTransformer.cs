using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
/// The symmetry law's OpenAPI tripwire (spec §10.4): throws if, after every schema transformer has
/// run, the finished document still carries a component schema named for <c>Result&lt;T&gt;</c>'s or
/// <c>Outcome&lt;T&gt;</c>'s own open-generic reflection shape (<c>ResultOfDateOnly</c>,
/// <c>OutcomeOfSomething</c>, or the bare <c>Result</c>/<c>Outcome</c> names). A document transformer,
/// not a schema transformer — catching this needs the whole finished document, not one schema node at
/// a time.
/// </summary>
/// <remarks>
/// A correctly-unwrapped document never carries these names at all, not merely unreferenced ones:
/// <see cref="ResultSchemaTransformer"/> replaces every Result-wrapped/raw scalar member with an
/// inline scalar schema, so nothing in the final document ever points at the union's own reflected
/// component — and AspNetCore.OpenApi's schema service only serializes components that are actually
/// reachable from a path or another schema, so an orphaned <c>Result&lt;T&gt;</c> component schema
/// never survives into <c>components.schemas</c> in the first place (verified empirically: the
/// generated document carries zero such entries once <see cref="ResultSchemaTransformer"/> runs). If
/// one is present when this transformer runs, either a member was never routed through the schema
/// transformers above (missing <c>[DataContract]</c>, an unsupported scalar, a registration gap), or a
/// future change reintroduced a direct leak. Named after the platform's own "wired not just designed"
/// lesson — <c>OutcomeServerInterceptor</c> shipped, tested, and sat unregistered for a full release —
/// so the equivalent OpenAPI-layer mistake fails the build loudly instead of shipping silently.
/// </remarks>
public sealed class UnionLeakGuardTransformer : IOpenApiDocumentTransformer
{
	/// <inheritdoc/>
	public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(document);

		var schemas = document.Components?.Schemas;
		if (schemas is null)
			return Task.CompletedTask;

		var leaked = schemas.Keys.Where(IsReservedUnionName).ToArray();
		if (leaked.Length > 0)
			throw new InvalidOperationException(
				"the OpenAPI document leaks Norse's own discriminated-union shape by name — the symmetry " +
				"law (spec §10) forbids Result<T>/Outcome<T> from ever reaching the contract document; " +
				$"found: {string.Join(", ", leaked)}");

		return Task.CompletedTask;
	}

	static bool IsReservedUnionName(string schemaName) =>
		schemaName is "Result" or "Outcome" ||
		schemaName.StartsWith("ResultOf", StringComparison.Ordinal) ||
		schemaName.StartsWith("OutcomeOf", StringComparison.Ordinal);
}
